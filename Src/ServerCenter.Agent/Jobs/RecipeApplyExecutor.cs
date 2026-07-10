using System.Text.Json;
using ServerCenter.Agent.Linux;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;
using ServerCenter.Core.Primitives;
using ServerCenter.Core.Recipes;

namespace ServerCenter.Agent.Jobs;

// Executes a "recipe.apply" job: stands a server up from a build recipe by composing the primitives
// in order. Every step is convergent, so re-applying to a half-built or drifted box repairs it
// (build = repair = rebuild). Steps: ensure base packages -> ensure the SteamCMD app -> render/write
// config -> run the idempotent scripts -> write the systemd unit + reload + enable + start. A failed
// step fails the job and stops (later steps depend on earlier ones). Requeueable (convergent).
public sealed class RecipeApplyExecutor(
    IPackageInstaller packages,
    ISteamCmd steam,
    IConfigWriter configWriter,
    ScriptRunner scripts,
    IServiceController services) : IJobExecutor
{
    public string JobType => "recipe.apply";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        RecipeApplyParams request;
        try
        {
            request = RecipeApplyParamsSerializer.Deserialize(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid recipe.apply params: {ex.Message}");
        }

        var recipe = request.Recipe;

        if (recipe.BaseRequirements is { } baseRequirements)
        {
            if (baseRequirements.Provider != packages.Provider)
            {
                return JobOutcome.Failure($"unsupported package provider '{baseRequirements.Provider}'");
            }

            if (!await packages.EnsureInstalledAsync(baseRequirements.Packages, sink, ct))
            {
                return JobOutcome.Failure("base package install failed");
            }
        }

        if (recipe.SteamApp is { } app)
        {
            var result = await steam.EnsureAppAsync(
                new SteamAppRequest(app.AppId, app.InstallDir, app.BetaBranch, Validate: true), sink, ct);
            if (!result.Success)
            {
                return JobOutcome.Failure(result.FailReason ?? "steam install failed");
            }
        }

        if (recipe.ConfigFiles.Count > 0)
        {
            var configGen = new ConfigGenCapability(
                new ConfigGenSpec("config-template", recipe.ConfigFiles),
                new InlineConfigTemplateSource(request.Templates),
                configWriter);
            try
            {
                await configGen.ApplyAsync(new ConfigContext(request.InstanceParams), sink, ct);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or FileNotFoundException)
            {
                return JobOutcome.Failure(ex.Message);
            }
        }

        if (recipe.Scripts.Count > 0)
        {
            var outcome = await scripts.RunAsync(recipe.Scripts, sink, ct);
            if (!outcome.Success)
            {
                return JobOutcome.Failure(outcome.FailReason ?? "recipe script failed");
            }
        }

        if (recipe.ServiceDefinition is { } service)
        {
            sink.Log(LogStream.Note, $"ensure service {service.Unit}");
            await configWriter.WriteAsync($"/etc/systemd/system/{service.Unit}", SystemdUnitRenderer.Render(service), ct);
            await services.ReloadAsync(ct);
            await services.EnsureEnabledAsync(service.Unit, true, ct);
            await services.StartAsync(service.Unit, ct);
        }

        return JobOutcome.Success();
    }
}
