using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using SPTOperatorRegistry.Server.Services;

namespace SPTOperatorRegistry.Server.Patches;

[Injectable]
public class BotGenerateOperatorPatch : AbstractPatch
{
    private static OperatorAssignmentService? _assignment;
    private static OperatorCacheService? _cache;
    private static DatabaseService? _database;

    private static readonly HashSet<string> _pmcRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        Sides.Usec, Sides.Bear, Sides.PmcUsec, Sides.PmcBear,
    };

    public BotGenerateOperatorPatch() : base("SPTOperatorRegistry.BotGenerateOperatorPatch") { }

    public static void SetDependencies(
        OperatorAssignmentService assignment,
        OperatorCacheService cache,
        DatabaseService database)
    {
        _assignment = assignment;
        _cache = cache;
        _database = database;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));
    }

    [PatchPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref BotBase? __result)
    {
        var bot = __result;
        if (bot?.Info == null || _assignment == null)
        {
            return;
        }

        var role = bot.Info.Settings?.Role;
        if (string.IsNullOrWhiteSpace(role) || !_pmcRoles.Contains(role))
        {
            return;
        }

        _cache?.EnsureFresh();

        var originalName = bot.Info.Nickname;
        var originalLevel = bot.Info.Level;

        // Roll chance before picking to avoid wasting pool slots.
        if (!_assignment.ShouldAssign())
        {
            return;
        }

        var op = _assignment.PickOperator();
        if (op == null || string.IsNullOrWhiteSpace(op.Nickname))
        {
            return;
        }

        bot.Info.Nickname = op.Nickname;
        bot.Info.LowerNickname = op.Nickname!.ToLowerInvariant();
        if (op.Level is int lvl and >= 1)
        {
            bot.Info.Level = lvl;
            bot.Info.Experience = GetExperienceForLevel(lvl);
        }

        _assignment.RecordAssignment(originalName, originalLevel, op);
    }

    private static int GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        if (_database == null) return 0;

        var expTable = _database.GetGlobals().Configuration.Exp.Level.ExperienceTable;
        if (expTable == null || expTable.Length == 0) return 0;

        // Sum XP for all full levels before the desired level (matches BotLevelGenerator logic).
        var clampedLevel = Math.Clamp(level, 0, expTable.Length);
        return expTable.Take(clampedLevel).Sum(entry => entry.Experience);
    }
}
