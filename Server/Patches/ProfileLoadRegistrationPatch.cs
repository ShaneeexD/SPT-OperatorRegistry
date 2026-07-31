using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Services;

namespace SPTOperatorRegistry.Server.Patches;

[Injectable]
public class ProfileLoadRegistrationPatch : AbstractPatch
{
    private static OperatorRegistrationService? _registration;
    private static ISptLogger<ProfileLoadRegistrationPatch>? _logger;
    private static string _modVersion = "1.0.0";
    private static string _sptVersion = "4.0.13";

    public ProfileLoadRegistrationPatch() : base("SPTOperatorRegistry.ProfileLoadRegistrationPatch") { }

    public static void SetDependencies(
        OperatorRegistrationService registration,
        ISptLogger<ProfileLoadRegistrationPatch> logger,
        string modVersion,
        string sptVersion)
    {
        _registration = registration;
        _logger = logger;
        _modVersion = modVersion;
        _sptVersion = sptVersion;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProfileController), nameof(ProfileController.GetCompleteProfile));
    }

    [PatchPostfix]
    private static void Postfix(ref List<PmcData>? __result, MongoId sessionId)
    {
        if (__result is null || __result.Count == 0 || _registration == null)
        {
            return;
        }

        // PmcData is index 0, scav is index 1.
        var pmc = __result[0];
        var nickname = pmc?.Info?.Nickname;
        var level = pmc?.Info?.Level;

        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        // Fire-and-forget: must never block profile loading.
        _ = Task.Run(async () =>
        {
            try
            {
                await _registration!.RegisterAsync(nickname, level, _sptVersion, _modVersion);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[OperatorRegistry] Background registration failed: {ex.Message}");
            }
        });
    }
}
