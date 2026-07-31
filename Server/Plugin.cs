using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Reflection.Patching;
using SPTOperatorRegistry.Server.Patches;
using SPTOperatorRegistry.Server.Services;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace SPTOperatorRegistry.Server;

public record OperatorRegistryMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.shaneeexd.operatorregistry";
    public override string Name { get; init; } = "SPT-OperatorRegistry";
    public override string Author { get; init; } = "ShaneeexD";
    public override List<string>? Contributors { get; init; } = null;
    public override Version Version { get; init; } = new Version("1.0.0");
    public override Range SptVersion { get; init; } = new Range("~4.0.13");
    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 1)]
public class OperatorRegistryPlugin(
    ISptLogger<OperatorRegistryPlugin> logger,
    ModHelper modHelper,
    ConfigService configService,
    InstallationIdService installationIdService,
    FirebaseAuthService firebaseAuthService,
    OperatorRegistrationService operatorRegistrationService,
    OperatorCacheService operatorCacheService,
    OperatorAssignmentService operatorAssignmentService,
    ISptLogger<ProfileLoadRegistrationPatch> profilePatchLogger,
    ISptLogger<RaidStartCacheRefreshPatch> raidRefreshPatchLogger,
    BotGenerateOperatorPatch botGenerateOperatorPatch,
    ProfileLoadRegistrationPatch profileLoadRegistrationPatch,
    RaidStartCacheRefreshPatch raidStartCacheRefreshPatch
) : IOnLoad
{
    private const string ModVersion = "1.0.0";

    public async Task OnLoad()
    {
        try
        {
            var modPath = modHelper.GetAbsolutePathToModFolder(typeof(OperatorRegistryPlugin).Assembly);
            logger.Info("[OperatorRegistry] Initialising...");

            configService.Load(modPath);

            if (!configService.Config.Enabled)
            {
                logger.Warning("[OperatorRegistry] Mod is disabled in config.json. Bot replacement and registration are off.");
                return;
            }

            installationIdService.Initialise(configService.ConfigPath);
            await firebaseAuthService.InitialiseAsync();
            operatorCacheService.Initialise(configService.ConfigPath);
            operatorCacheService.Start();

            var sptVersion = ProgramStatics.SPT_VERSION()?.ToString() ?? "4.0.13";

            ProfileLoadRegistrationPatch.SetDependencies(
                operatorRegistrationService,
                profilePatchLogger,
                ModVersion,
                sptVersion
            );
            BotGenerateOperatorPatch.SetDependencies(
                operatorAssignmentService,
                operatorCacheService
            );
            RaidStartCacheRefreshPatch.SetDependencies(
                operatorCacheService,
                operatorAssignmentService,
                raidRefreshPatchLogger
            );

            profileLoadRegistrationPatch.Enable();
            botGenerateOperatorPatch.Enable();
            raidStartCacheRefreshPatch.Enable();

            logger.Info("[OperatorRegistry] Loaded successfully. Community operators will appear on PMC bots in raids.");
        }
        catch (Exception ex)
        {
            logger.Error($"[OperatorRegistry] Failed to load: {ex}");
            throw;
        }
    }
}
