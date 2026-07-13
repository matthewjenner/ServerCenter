using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;

namespace ServerCenter.Controller.Services;

// Seeds ready-to-use game definitions on startup so an operator can create a server without authoring
// a descriptor/recipe by hand (mirrors DefaultPolicies). Idempotent: an id that already exists is left
// as-is, so an operator edit is never overwritten. Every per-instance string is TEMPLATED with the
// reserved {{instance.id}} / {{instance.dir}} tokens so N of the same game coexist on one node.
//
// CS2 (Counter-Strike 2): SteamCMD anonymous app 730 (client + dedicated server merged); the server
// binary is game/bin/linuxsteamrt64/cs2.sh; config lives at game/csgo/cfg/gameserver.cfg; game port
// 27015 (+2 per instance); GSLT via +sv_setsteamaccount. (Verified against current CS2 docs 2026-07.)
public static class DefaultGames
{
    // Install dirs and units are namespaced per instance so two CS2 servers never collide.
    private const string Cs2InstallDir = "/opt/servercenter/cs2/{{instance.id}}";
    private const string Cs2ConfigPath = "{{instance.dir}}/game/csgo/cfg/gameserver.cfg";
    private const string Cs2Unit = "sc-cs2-{{instance.id}}.service";

    private const string Cs2ExecStart =
        "{{instance.dir}}/game/bin/linuxsteamrt64/cs2.sh -dedicated -usercon " +
        "+game_type {{game.type}} +game_mode {{game.mode}} +map {{map}} " +
        "+sv_setsteamaccount {{gslt}} +rcon_password {{rcon.password}} -port {{ports.game}}";

    private static readonly ConfigFileSpec Cs2GameServerCfg =
        new("cs2/gameserver.cfg", Cs2ConfigPath, ConfigFormat.Kv);

    private static GameDescriptor Cs2Descriptor => new()
    {
        Id = "cs2",
        Version = 1,
        SteamApp = new SteamAppSpec(730, Cs2InstallDir),
        Capabilities = new GameCapabilities
        {
            ConfigGen = new ConfigGenSpec("config-template", [Cs2GameServerCfg])
        }
    };

    private static BuildRecipe Cs2Recipe => new()
    {
        Id = "cs2",
        Version = 1,
        SteamApp = new SteamAppSpec(730, Cs2InstallDir),
        ConfigFiles = [Cs2GameServerCfg],
        ServiceDefinition = new ServiceDefinition(Cs2Unit, Cs2ExecStart),
        DescriptorRef = new DescriptorRef("cs2", 1)
    };

    public static async Task EnsureAsync(
        GameDescriptorRepository descriptors, BuildRecipeRepository recipes, TimeProvider clock, CancellationToken ct)
    {
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (await descriptors.GetLatestAsync(Cs2Descriptor.Id, ct) is null)
        {
            await descriptors.InsertAsync(Cs2Descriptor, now, ct);
        }

        if (await recipes.GetLatestAsync(Cs2Recipe.Id, ct) is null)
        {
            await recipes.InsertAsync(Cs2Recipe, now, ct);
        }
    }
}
