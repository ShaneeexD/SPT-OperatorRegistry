using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTOperatorRegistry.Server.Services;

namespace SPTOperatorRegistry.Server.Patches;

[Injectable]
public class BotGenerateOperatorPatch : AbstractPatch
{
    private static OperatorAssignmentService? _assignment;
    private static OperatorCacheService? _cache;

    private static readonly HashSet<string> _pmcRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        Sides.Usec, Sides.Bear, Sides.PmcUsec, Sides.PmcBear,
    };

    public BotGenerateOperatorPatch() : base("SPTOperatorRegistry.BotGenerateOperatorPatch") { }

    public static void SetDependencies(
        OperatorAssignmentService assignment,
        OperatorCacheService cache)
    {
        _assignment = assignment;
        _cache = cache;
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

    // EFT cumulative XP per level (index 0 = level 1).
    private static readonly int[] CumulativeXpPerLevel =
    [
        0, 1000, 4017, 8432, 14256, 21477, 30023, 39936, 51204, 63723,
        77563, 93279, 115302, 143253, 177337, 217885, 264432, 316851, 374400, 437465,
        505161, 577978, 656347, 741150, 836066, 944133, 1066259, 1199423, 1343743, 1499338,
        1666320, 1846664, 2043349, 2258436, 2492126, 2750217, 3032022, 3337766, 3663831, 4010401,
        4377662, 4765799, 5182399, 5627732, 6102063, 6630287, 7189442, 7779792, 8401607, 9055144,
        9740666, 10458431, 11219666, 12024744, 12874041, 13767918, 14706741, 15690872, 16720667, 17816442,
        19041492, 20360945, 21792266, 23350443, 25098462, 27100775, 29581231, 33028574, 37953544, 44260543,
        51901513, 60887711, 71228846, 82933459, 96009180, 110462910, 126300949, 144924572, 172016256
    ];

    private static int GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        if (level - 1 < CumulativeXpPerLevel.Length)
            return CumulativeXpPerLevel[level - 1];
        return CumulativeXpPerLevel[^1];
    }
}
