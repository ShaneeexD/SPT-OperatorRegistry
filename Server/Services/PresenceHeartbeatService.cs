using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class PresenceHeartbeatService(
    ISptLogger<PresenceHeartbeatService> logger,
    ConfigService configService,
    OperatorRegistrationService operatorRegistrationService
)
{
    private Timer? _timer;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        if (!configService.Config.OnlineOnly)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(5);
        logger.Info("[OperatorRegistry] Starting presence heartbeat (interval: 5 min).");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task TickAsync()
    {
        try
        {
            await operatorRegistrationService.SendHeartbeatAsync();
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Presence heartbeat tick failed: {ex.Message}");
        }
    }
}
