using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class OperatorAssignmentService(
    ISptLogger<OperatorAssignmentService> logger,
    ConfigService configService,
    OperatorCacheService operatorCacheService,
    OperatorRegistrationService operatorRegistrationService
)
{
    private readonly Lock _lock = new();
    private readonly Random _random = new();

    private List<OperatorEntry> _raidPool = new();
    private List<string> _raidAssignments = new();

    public void ResetRaidPool()
    {
        lock (_lock)
        {
            _raidPool = BuildAvailablePool();
            _raidAssignments.Clear();
        }
    }

    private List<OperatorEntry> BuildAvailablePool()
    {
        var pool = operatorCacheService.Operators.ToList();

        // Exclude our own operator so we don't appear in our own raids.
        var myNickname = operatorRegistrationService.LastRegisteredNickname;
        var myLevel = operatorRegistrationService.LastRegisteredLevel;
        if (!string.IsNullOrWhiteSpace(myNickname))
        {
            pool = pool
                .Where(o => !string.Equals(o.Nickname, myNickname, StringComparison.OrdinalIgnoreCase)
                            || o.Level != myLevel)
                .ToList();
        }

        return pool;
    }

    public bool ShouldAssign()
    {
        if (!configService.Config.Enabled)
        {
            return false;
        }

        var chance = configService.Config.OperatorChance;
        if (chance <= 0)
        {
            return false;
        }
        if (chance >= 1)
        {
            return true;
        }

        lock (_lock)
        {
            return _random.NextDouble() < chance;
        }
    }

    public OperatorEntry? PickOperator()
    {
        lock (_lock)
        {
            if (_raidPool.Count == 0)
            {
                return null;
            }

            var index = _random.Next(_raidPool.Count);
            var picked = _raidPool[index];
            _raidPool.RemoveAt(index);
            return picked;
        }
    }

    public OperatorEntry? PickIfAssigned()
    {
        if (!ShouldAssign())
        {
            return null;
        }

        var op = PickOperator();
        if (op == null)
        {
            return null;
        }
        return op;
    }

    public int RaidPoolRemaining
    {
        get
        {
            lock (_lock)
            {
                return _raidPool.Count;
            }
        }
    }

    public void RecordAssignment(string? originalName, int? originalLevel, OperatorEntry op)
    {
        lock (_lock)
        {
            _raidAssignments.Add($"'{originalName}' L{originalLevel} -> '{op.Nickname}' L{op.Level}");
        }
    }

    public int RaidAssignmentCount
    {
        get
        {
            lock (_lock)
            {
                return _raidAssignments.Count;
            }
        }
    }

    public string GetRaidSummary()
    {
        lock (_lock)
        {
            return string.Join(", ", _raidAssignments);
        }
    }
}
