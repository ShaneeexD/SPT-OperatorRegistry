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
        if (_cache == null)
        {
            return;
        }

        var ok = _cache.RefreshBlocking(RefreshTimeout);
        if (ok)
        {
            _logger?.Info($"[OperatorRegistry] Raid-start cache refresh complete ({_cache.Operators.Count} operators available).");
        }
        else
        {
            _logger?.Warning("[OperatorRegistry] Raid-start cache refresh skipped/failed; using existing local cache.");
        }

        _assignment?.ResetRaidPool();
        var poolSize = _assignment?.RaidPoolRemaining ?? 0;
        _logger?.Info($"[OperatorRegistry] Raid operator pool ready ({poolSize} unique operators for this raid).");
    }

    [PatchPostfix]
    private static void Postfix()
    {
        if (_assignment == null || _logger == null)
        {
            return;
        }

        var count = _assignment.RaidAssignmentCount;
        if (count > 0)
        {
            _logger.Info($"[OperatorRegistry] Raid summary — {count} PMC bot(s) renamed: {_assignment.GetRaidSummary()}");
        }
        else
        {
            _logger.Info("[OperatorRegistry] Raid summary — no community operators assigned this raid.");
        }
    }
}
