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
        var resolved = await ResolveAsync(instanceId, ct);
        if (resolved.Error is { } error)
        {
            return error;
        }

        var app = resolved.Descriptor!.SteamApp;
        var paramsJson = ServerJobParamsSerializer.Serialize(
            new ServerInstallParams(app.AppId, app.InstallDir, app.BetaBranch, Validate: true));

        // SteamCMD ensure is convergent, so an install is safely requeueable after a disconnect.
        var jobId = await jobs.DispatchAsync(
            agentId, "server.install", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    public async Task<ServerDispatchResult> ConfigApplyAsync(string agentId, string instanceId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(instanceId, ct);
        if (resolved.Error is { } error)
        {
            return error;
        }

        var configGen = resolved.Descriptor!.Capabilities.ConfigGen;
        if (configGen is null)
        {
            return ServerDispatchResult.NotConfigured("descriptor declares no configGen capability");
        }

        var instanceParams = InstanceParamsResolver.Flatten(resolved.Instance!.InstanceParamsJson);

        var templateMap = new Dictionary<string, string>();
        foreach (var file in configGen.Files)
        {
            templateMap[file.SchemaRef] = await templates.GetAsync(file.SchemaRef, ct);
        }

        var paramsJson = ServerJobParamsSerializer.Serialize(
            new ServerConfigApplyParams(configGen.Files, templateMap, instanceParams));

        var jobId = await jobs.DispatchAsync(
            agentId, "server.config-apply", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    public async Task<ServerDispatchResult> ApplyRecipeAsync(string agentId, string instanceId, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return ServerDispatchResult.NotFound($"server instance '{instanceId}' not found");
        }

        if (instance.RecipeId is null || instance.RecipeVersion is null)
        {
            return ServerDispatchResult.NotConfigured("instance has no pinned recipe");
        }

        var recipe = await recipes.GetAsync(instance.RecipeId, instance.RecipeVersion.Value, ct);
        if (recipe is null)
        {
            return ServerDispatchResult.NotFound($"recipe '{instance.RecipeId}' v{instance.RecipeVersion} not found");
        }

        var instanceParams = InstanceParamsResolver.Flatten(instance.InstanceParamsJson);

        var templateMap = new Dictionary<string, string>();
        foreach (var file in recipe.ConfigFiles)
        {
            templateMap[file.SchemaRef] = await templates.GetAsync(file.SchemaRef, ct);
        }

        var paramsJson = RecipeApplyParamsSerializer.Serialize(
            new RecipeApplyParams(recipe, instanceParams, templateMap));

        // A recipe is convergent (every step is idempotent), so it is safely requeueable.
        var jobId = await jobs.DispatchAsync(
            agentId, "recipe.apply", paramsJson, cancellable: false, requeueable: true, ct);
        return ServerDispatchResult.Dispatched(jobId);
    }

    private async Task<ResolvedInstance> ResolveAsync(string instanceId, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return new ResolvedInstance(null, null, ServerDispatchResult.NotFound($"server instance '{instanceId}' not found"));
        }

        if (instance.DescriptorId is null || instance.DescriptorVersion is null)
        {
            return new ResolvedInstance(instance, null, ServerDispatchResult.NotConfigured("instance has no pinned descriptor"));
        }

        var descriptor = await descriptors.GetAsync(instance.DescriptorId, instance.DescriptorVersion.Value, ct);
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
