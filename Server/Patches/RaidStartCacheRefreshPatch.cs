using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Services;

namespace SPTOperatorRegistry.Server.Patches;

[Injectable]
public class RaidStartCacheRefreshPatch : AbstractPatch
{
    private static OperatorCacheService? _cache;
    private static OperatorAssignmentService? _assignment;
    private static ISptLogger<RaidStartCacheRefreshPatch>? _logger;

    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);

    public RaidStartCacheRefreshPatch() : base("SPTOperatorRegistry.RaidStartCacheRefreshPatch") { }

    public static void SetDependencies(
        OperatorCacheService cache,
        OperatorAssignmentService assignment,
        ISptLogger<RaidStartCacheRefreshPatch> logger)
    {
        _cache = cache;
        _assignment = assignment;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotController), nameof(BotController.Generate));
    }

    [PatchPrefix]
    private static void Prefix()
    {
        if (_cache == null || _assignment == null)
        {
            return;
        }

        // Only refresh + reset pool on the first Generate call for this raid.
        if (!_assignment.RaidInitialised)
        {
            var ok = _cache.RefreshBlocking(RefreshTimeout);
            if (ok)
            {
                _logger?.Info($"[OperatorRegistry] Raid-start cache refresh complete ({_cache.Operators.Count} operators available).");
            }
            else
            {
                _logger?.Warning("[OperatorRegistry] Raid-start cache refresh skipped/failed; using existing local cache.");
            }

            _assignment.ResetRaidPool();
            var poolSize = _assignment.RaidPoolRemaining;
            _logger?.Info($"[OperatorRegistry] Raid operator pool ready ({poolSize} unique operators for this raid).");
        }
    }
}
