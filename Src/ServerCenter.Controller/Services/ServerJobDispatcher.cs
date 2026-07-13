using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;
using ServerCenter.Primitives.ConfigTemplating;

namespace ServerCenter.Controller.Services;

// Turns "install / configure / build this server instance" into a persisted job. It resolves the
// instance + its pinned descriptor/recipe controller-side (the agent never sees them), builds the
// concrete job params, and dispatches. Config-apply and recipe-apply ship each config file's template
// text with the job.
public sealed class ServerJobDispatcher(
    GameDescriptorRepository descriptors,
    BuildRecipeRepository recipes,
    ServerInstanceRepository instances,
    JobDispatcher jobs,
    IConfigTemplateSource templates)
{
    public async Task<ServerDispatchResult> InstallAsync(string agentId, string instanceId, CancellationToken ct)
    {
        ResolvedInstance resolved = await ResolveAsync(instanceId, ct);
        if (resolved.Error is { } error)
        {
            return error;
        }

        SteamAppSpec app = resolved.Descriptor!.SteamApp;
        string installDir;
        try
        {
            // Render the (possibly {{instance.id}}-templated) install dir so N instances of the same
            // game land in distinct directories.
            installDir = BuildTokens(resolved.Instance!, app.InstallDir)[InstanceContext.InstanceDirKey];
        }
        catch (KeyNotFoundException ex)
        {
            return ServerDispatchResult.NotConfigured($"install dir template token error: {ex.Message}");
        }

        string paramsJson = ServerJobParamsSerializer.Serialize(
            new ServerInstallParams(app.AppId, installDir, app.BetaBranch, Validate: true));

        // SteamCMD ensure is convergent, so an install is safely requeueable after a disconnect.
        string jobId = await jobs.DispatchAsync(
            agentId, "server.install", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    public async Task<ServerDispatchResult> ConfigApplyAsync(string agentId, string instanceId, CancellationToken ct)
    {
        ResolvedInstance resolved = await ResolveAsync(instanceId, ct);
        if (resolved.Error is { } error)
        {
            return error;
        }

        GameDescriptor descriptor = resolved.Descriptor!;
        ConfigGenSpec? configGen = descriptor.Capabilities.ConfigGen;
        if (configGen is null)
        {
            return ServerDispatchResult.NotConfigured("descriptor declares no configGen capability");
        }

        // Tokens include instance.dir (rendered from the descriptor's install dir) so config file
        // PATHS - and the file contents the agent renders - can be per-instance.
        IReadOnlyList<ConfigFileSpec> renderedFiles;
        Dictionary<string, string> tokens;
        try
        {
            tokens = BuildTokens(resolved.Instance!, descriptor.SteamApp.InstallDir);
            renderedFiles = RenderPaths(configGen.Files, tokens);
        }
        catch (KeyNotFoundException ex)
        {
            return ServerDispatchResult.NotConfigured($"config path template token error: {ex.Message}");
        }

        Dictionary<string, string> templateMap = new Dictionary<string, string>();
        foreach (ConfigFileSpec file in configGen.Files)
        {
            templateMap[file.SchemaRef] = await templates.GetAsync(file.SchemaRef, ct);
        }

        string paramsJson = ServerJobParamsSerializer.Serialize(
            new ServerConfigApplyParams(renderedFiles, templateMap, tokens));

        string jobId = await jobs.DispatchAsync(
            agentId, "server.config-apply", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    public async Task<ServerDispatchResult> ApplyRecipeAsync(string agentId, string instanceId, CancellationToken ct)
    {
        ServerInstance? instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return ServerDispatchResult.NotFound($"server instance '{instanceId}' not found");
        }

        if (instance.RecipeId is null || instance.RecipeVersion is null)
        {
            return ServerDispatchResult.NotConfigured("instance has no pinned recipe");
        }

        BuildRecipe? recipe = await recipes.GetAsync(instance.RecipeId, instance.RecipeVersion.Value, ct);
        if (recipe is null)
        {
            return ServerDispatchResult.NotFound($"recipe '{instance.RecipeId}' v{instance.RecipeVersion} not found");
        }

        // Render the recipe's per-instance strings (install dir, unit name, ExecStart, config paths)
        // controller-side so the agent writes concrete, per-instance values.
        BuildRecipe renderedRecipe;
        Dictionary<string, string> tokens;
        try
        {
            tokens = BuildTokens(instance, recipe.SteamApp?.InstallDir);
            renderedRecipe = RenderRecipe(recipe, tokens);
        }
        catch (KeyNotFoundException ex)
        {
            return ServerDispatchResult.NotConfigured($"recipe template token error: {ex.Message}");
        }

        Dictionary<string, string> templateMap = new Dictionary<string, string>();
        foreach (ConfigFileSpec file in recipe.ConfigFiles)
        {
            templateMap[file.SchemaRef] = await templates.GetAsync(file.SchemaRef, ct);
        }

        string paramsJson = RecipeApplyParamsSerializer.Serialize(
            new RecipeApplyParams(renderedRecipe, tokens, templateMap));

        // A recipe is convergent (every step is idempotent), so it is safely requeueable.
        string jobId = await jobs.DispatchAsync(
            agentId, "recipe.apply", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    // Tears a server instance down: renders its per-instance unit / install dir / config paths from the
    // pinned descriptor + recipe and dispatches server.remove to the instance's node. The caller deletes
    // the controller row. An instance with no recipe has no unit; with no descriptor/recipe, empty
    // strings tell the executor there is nothing on disk to clean (the row delete still happens).
    public async Task<ServerDispatchResult> RemoveAsync(string instanceId, CancellationToken ct)
    {
        Footprint? footprint;
        try
        {
            footprint = await ResolveFootprintAsync(instanceId, ct);
        }
        catch (KeyNotFoundException ex)
        {
            return ServerDispatchResult.NotConfigured($"remove template token error: {ex.Message}");
        }

        if (footprint is null)
        {
            return ServerDispatchResult.NotFound($"server instance '{instanceId}' not found");
        }

        ServerRemoveParams removeParams = new ServerRemoveParams(
            footprint.Unit, footprint.InstallDir, footprint.ConfigPaths);
        string paramsJson = ServerJobParamsSerializer.Serialize(removeParams);
        string jobId = await jobs.DispatchAsync(
            footprint.Instance.NodeId, "server.remove", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    // The rendered config-file paths of an instance (for the operator's file list). Null if the instance
    // doesn't exist. Throws KeyNotFoundException on a bad token (the endpoint maps it to a 400).
    public async Task<IReadOnlyList<string>?> ResolveConfigPathsAsync(string instanceId, CancellationToken ct) =>
        (await ResolveFootprintAsync(instanceId, ct))?.ConfigPaths;

    public Task<ServerDispatchResult> ConfigReadAsync(string instanceId, string path, CancellationToken ct) =>
        DispatchConfigAsync(instanceId, path, "server.config-read",
            _ => new ServerConfigReadParams(path), ct);

    public Task<ServerDispatchResult> ConfigWriteAsync(string instanceId, string path, string content, CancellationToken ct) =>
        DispatchConfigAsync(instanceId, path, "server.config-write",
            _ => new ServerConfigWriteParams(path, content), ct);

    // Shared read/write dispatch: resolve the instance's footprint, refuse a path that isn't one of its
    // rendered config files (so this can't read/write arbitrary files), then dispatch to its node.
    private async Task<ServerDispatchResult> DispatchConfigAsync(
        string instanceId, string path, string jobType, Func<Footprint, object> paramsFactory, CancellationToken ct)
    {
        Footprint? footprint;
        try
        {
            footprint = await ResolveFootprintAsync(instanceId, ct);
        }
        catch (KeyNotFoundException ex)
        {
            return ServerDispatchResult.NotConfigured($"config template token error: {ex.Message}");
        }

        if (footprint is null)
        {
            return ServerDispatchResult.NotFound($"server instance '{instanceId}' not found");
        }

        if (!footprint.ConfigPaths.Contains(path))
        {
            return ServerDispatchResult.NotConfigured($"'{path}' is not a config file of this instance");
        }

        string paramsJson = ServerJobParamsSerializer.Serialize(paramsFactory(footprint));
        string jobId = await jobs.DispatchAsync(
            footprint.Instance.NodeId, jobType, paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    // Resolves an instance + its pinned descriptor/recipe and renders its full per-instance footprint:
    // the systemd unit, install dir, and config-file paths. Shared by remove and config read/write.
    private async Task<Footprint?> ResolveFootprintAsync(string instanceId, CancellationToken ct)
    {
        ServerInstance? instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return null;
        }

        GameDescriptor? descriptor = instance.DescriptorId is not null && instance.DescriptorVersion is not null
            ? await descriptors.GetAsync(instance.DescriptorId, instance.DescriptorVersion.Value, ct)
            : null;
        BuildRecipe? recipe = instance.RecipeId is not null && instance.RecipeVersion is not null
            ? await recipes.GetAsync(instance.RecipeId, instance.RecipeVersion.Value, ct)
            : null;

        string? installDirTemplate = recipe?.SteamApp?.InstallDir ?? descriptor?.SteamApp.InstallDir;
        Dictionary<string, string> tokens = BuildTokens(instance, installDirTemplate);

        string unit = recipe?.ServiceDefinition is { } service
            ? ConfigTemplateRenderer.Render(service.Unit, tokens)
            : string.Empty;
        string installDir = installDirTemplate is not null ? tokens[InstanceContext.InstanceDirKey] : string.Empty;

        List<string> configPaths = new List<string>();
        if (descriptor?.Capabilities.ConfigGen is { } configGen)
        {
            foreach (ConfigFileSpec file in configGen.Files)
            {
                configPaths.Add(ConfigTemplateRenderer.Render(file.Path, tokens));
            }
        }

        if (recipe is not null)
        {
            foreach (ConfigFileSpec file in recipe.ConfigFiles)
            {
                configPaths.Add(ConfigTemplateRenderer.Render(file.Path, tokens));
            }
        }

        return new Footprint(instance, unit, installDir, configPaths.Distinct().ToList());
    }

    private sealed record Footprint(ServerInstance Instance, string Unit, string InstallDir, IReadOnlyList<string> ConfigPaths);

    // Flatten the instance params + reserved instance.*/node.* tokens, and (when an install-dir
    // template is given) render it into instance.dir so unit/ExecStart/config paths can reference it.
    private static Dictionary<string, string> BuildTokens(ServerInstance instance, string? installDirTemplate)
    {
        Dictionary<string, string> tokens = InstanceContext.Build(instance.Id, instance.NodeId, instance.InstanceParamsJson);
        if (!string.IsNullOrEmpty(installDirTemplate))
        {
            tokens[InstanceContext.InstanceDirKey] = ConfigTemplateRenderer.Render(installDirTemplate, tokens);
        }

        return tokens;
    }

    // Render each config file's destination PATH per-instance (contents are rendered agent-side).
    private static IReadOnlyList<ConfigFileSpec> RenderPaths(IReadOnlyList<ConfigFileSpec> files, IReadOnlyDictionary<string, string> tokens)
    {
        List<ConfigFileSpec> rendered = new List<ConfigFileSpec>(files.Count);
        foreach (ConfigFileSpec file in files)
        {
            rendered.Add(file with { Path = ConfigTemplateRenderer.Render(file.Path, tokens) });
        }

        return rendered;
    }

    // A copy of the recipe with its per-instance strings resolved: install dir, unit name + ExecStart,
    // and config-file paths. Base packages / scripts / descriptor ref are unchanged.
    private static BuildRecipe RenderRecipe(BuildRecipe recipe, IReadOnlyDictionary<string, string> tokens) =>
        recipe with
        {
            SteamApp = recipe.SteamApp is { } app
                ? app with { InstallDir = ConfigTemplateRenderer.Render(app.InstallDir, tokens) }
                : null,
            ConfigFiles = RenderPaths(recipe.ConfigFiles, tokens),
            ServiceDefinition = recipe.ServiceDefinition is { } service
                ? service with
                {
                    Unit = ConfigTemplateRenderer.Render(service.Unit, tokens),
                    ExecStart = ConfigTemplateRenderer.Render(service.ExecStart, tokens)
                }
                : null
        };

    private async Task<ResolvedInstance> ResolveAsync(string instanceId, CancellationToken ct)
    {
        ServerInstance? instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return new ResolvedInstance(null, null, ServerDispatchResult.NotFound($"server instance '{instanceId}' not found"));
        }

        if (instance.DescriptorId is null || instance.DescriptorVersion is null)
        {
            return new ResolvedInstance(instance, null, ServerDispatchResult.NotConfigured("instance has no pinned descriptor"));
        }

        GameDescriptor? descriptor = await descriptors.GetAsync(instance.DescriptorId, instance.DescriptorVersion.Value, ct);
        return descriptor is null
            ? new ResolvedInstance(instance, null,
                ServerDispatchResult.NotFound($"descriptor '{instance.DescriptorId}' v{instance.DescriptorVersion} not found"))
            : new ResolvedInstance(instance, descriptor, null);
    }

    private sealed record ResolvedInstance(ServerInstance? Instance, GameDescriptor? Descriptor, ServerDispatchResult? Error);
}

public enum ServerDispatchOutcome
{
    Dispatched,
    NotFound,
    NotConfigured
}

public sealed record ServerDispatchResult(ServerDispatchOutcome Outcome, string? JobId, string? Reason)
{
    public static ServerDispatchResult Dispatched(string jobId) => new(ServerDispatchOutcome.Dispatched, jobId, null);

    public static ServerDispatchResult NotFound(string reason) => new(ServerDispatchOutcome.NotFound, null, reason);

    public static ServerDispatchResult NotConfigured(string reason) => new(ServerDispatchOutcome.NotConfigured, null, reason);
}
