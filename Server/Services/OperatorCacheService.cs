using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Models;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class OperatorCacheService(
    ISptLogger<OperatorCacheService> logger,
    ConfigService configService
)
{
    private readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private string _cachePath = string.Empty;
    private OperatorCache _cache = new();
    private DateTime _lastRefreshAttempt = DateTime.MinValue;
    private Timer? _refreshTimer;

    public IReadOnlyList<OperatorEntry> Operators => _cache.Operators;
    public long UpdatedAt => _cache.Updated;

    public void Initialise(string configPath)
    {
        _cachePath = Path.Combine(configPath, "operator_cache.json");
        LoadLocal();
    }

    public void Start()
    {
        _ = RefreshAsync();

        var intervalHours = Math.Max(1, configService.Config.MaxCacheAgeHours);
        var interval = TimeSpan.FromHours(intervalHours);
        _refreshTimer = new Timer(_ => _ = RefreshAsync(), null, interval, interval);
    }

    public void EnsureFresh()
    {
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UpdatedAt;
        var maxAgeSeconds = configService.Config.MaxCacheAgeHours * 3600L;
        if (age > maxAgeSeconds && (DateTime.UtcNow - _lastRefreshAttempt) > TimeSpan.FromMinutes(2))
        {
            _ = RefreshAsync();
        }
    }

    public bool RefreshBlocking(TimeSpan timeout)
    {
        try
        {
            return RefreshAsync().Wait((int)timeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Raid-start cache refresh failed: {ex.Message}");
            return false;
        }
    }

    public async Task RefreshAsync()
    {
        _lastRefreshAttempt = DateTime.UtcNow;

        if (!configService.Config.Enabled)
        {
            return;
        }

        var url = configService.Config.CacheUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("SPT-OperatorRegistry/1.0");
            using var response = await _httpClient.SendAsync(req, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.Warning($"[OperatorRegistry] Cache download failed: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var cache = JsonSerializer.Deserialize<OperatorCache>(json, JsonOptions);
            if (cache == null)
            {
                logger.Warning("[OperatorRegistry] Cache download returned empty/invalid JSON.");
                return;
            }

            cache.Operators = cache.Operators
                .Where(o => o != null)
                .Select(o => new OperatorEntry
                {
                    Nickname = OperatorRegistrationService.SanitizeNickname(o!.Nickname) ?? o.Nickname,
                    Level = OperatorRegistrationService.ClampLevel(o.Level) ?? o.Level,
                })
                .Where(o => !string.IsNullOrWhiteSpace(o.Nickname))
                .ToList();

            _cache = cache;
            SaveLocal(cache);
            logger.Info($"[OperatorRegistry] Cache refreshed: {cache.Operators.Count} operators (updated={cache.Updated}).");
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Cache refresh error: {ex.Message}");
        }
    }

    private void LoadLocal()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return;
            }

            var json = File.ReadAllText(_cachePath);
            var cache = JsonSerializer.Deserialize<OperatorCache>(json, JsonOptions);
            if (cache != null)
            {
                _cache = cache;
                logger.Info($"[OperatorRegistry] Loaded local cache: {cache.Operators.Count} operators.");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Could not load local cache: {ex.Message}");
        }
    }

    private void SaveLocal(OperatorCache cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tmp = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cache, JsonOptions));
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Could not save local cache: {ex.Message}");
        }
    }
}

public class OperatorCache
{
    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonPropertyName("operators")]
    public List<OperatorEntry> Operators { get; set; } = new();
}

public class OperatorEntry
{
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }
}
