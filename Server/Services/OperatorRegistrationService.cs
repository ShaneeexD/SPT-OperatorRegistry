using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Models;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class OperatorRegistrationService(
    ISptLogger<OperatorRegistrationService> logger,
    ConfigService configService,
    InstallationIdService installationIdService,
    FirebaseAuthService firebaseAuthService
)
{
    private readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // EFT nickname constraints (must match client-side validation).
    public const int NicknameMinLength = 3;
    public const int NicknameMaxLength = 32;
    public const int LevelMin = 1;
    public const int LevelMax = 79;

    private long _lastRegisteredAt;
    private string? _lastRegisteredNickname;
    private int? _lastRegisteredLevel;
    private static readonly TimeSpan MinReRegisterInterval = TimeSpan.FromMinutes(5);

    public string? LastRegisteredNickname => _lastRegisteredNickname;
    public int? LastRegisteredLevel => _lastRegisteredLevel;

    public async Task RegisterAsync(string? nickname, int? level, string sptVersion, string modVersion)
    {
        if (!configService.Config.Enabled)
        {
            return;
        }

        var installationId = installationIdService.InstallationId;
        if (string.IsNullOrWhiteSpace(installationId))
        {
            logger.Warning("[OperatorRegistry] Cannot register: no installation UUID.");
            return;
        }

        var cleanNickname = SanitizeNickname(nickname);
        var safeLevel = ClampLevel(level);

        if (cleanNickname == null || safeLevel == null)
        {
            logger.Warning($"[OperatorRegistry] Registration rejected (invalid data: nickname='{nickname}', level={level}).");
            return;
        }

        // Throttle unless nickname/level changed (level-up/rename forces immediate re-register).
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var firstEver = _lastRegisteredAt == 0;
        var hasChanged = _lastRegisteredNickname != cleanNickname || _lastRegisteredLevel != safeLevel;
        if (!firstEver && !hasChanged && (now - _lastRegisteredAt) < MinReRegisterInterval.TotalSeconds)
        {
            return;
        }

        var baseUrl = ResolveDatabaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.Warning("[OperatorRegistry] Cannot register: database URL not configured.");
            return;
        }

        var root = configService.Config.RtdbRoot;
        var nodePath = $"{root}/{installationId}";

        try
        {
            // RTDB rules deny reads (.read: false), so we can't fetch existing firstSeen.
            // Persist it locally via InstallationIdService so it survives restarts.
            var firstSeen = installationIdService.FirstSeen ?? now;
            var record = new OperatorRecord
            {
                Nickname = cleanNickname,
                Level = safeLevel.Value,
                SptVersion = sptVersion,
                ModVersion = modVersion,
                FirstSeen = firstSeen,
                LastSeen = now,
            };

            // Get fresh token for PUT.
            var token = await firebaseAuthService.GetIdTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.Warning("[OperatorRegistry] Cannot register: no Firebase id token.");
                return;
            }

            var url = $"{baseUrl}{nodePath}.json?auth={Uri.EscapeDataString(token)}";
            var (ok, statusCode) = await PutJsonAsync(url, record);

            if (!ok && (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
            {
                logger.Info("[OperatorRegistry] RTDB write auth-expired, forcing token refresh and retrying.");
                token = await firebaseAuthService.ForceRefreshAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    url = $"{baseUrl}{nodePath}.json?auth={Uri.EscapeDataString(token)}";
                    (ok, statusCode) = await PutJsonAsync(url, record);
                }
            }

            if (ok)
            {
                _lastRegisteredAt = now;
                _lastRegisteredNickname = cleanNickname;
                _lastRegisteredLevel = safeLevel;
                installationIdService.SetFirstSeen(firstSeen);
                logger.Info($"[OperatorRegistry] Registered operator '{cleanNickname}' (L{safeLevel}) to RTDB.");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Registration upload failed: {ex.Message}");
        }
    }

    public async Task SendHeartbeatAsync()
    {
        if (!configService.Config.Enabled || !configService.Config.OnlineOnly)
        {
            return;
        }

        var installationId = installationIdService.InstallationId;
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return;
        }

        var baseUrl = ResolveDatabaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

        try
        {
            var token = await firebaseAuthService.GetIdTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var url = $"{baseUrl}presence/{installationId}.json?auth={Uri.EscapeDataString(token)}";
            var payload = new Dictionary<string, object>
            {
                ["lastSeen"] = new Dictionary<string, string> { [".sv"] = "timestamp" },
            };
            var (ok, statusCode) = await PutJsonAsync(url, payload);

            if (!ok && (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
            {
                token = await firebaseAuthService.ForceRefreshAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    url = $"{baseUrl}presence/{installationId}.json?auth={Uri.EscapeDataString(token)}";
                    (ok, statusCode) = await PutJsonAsync(url, payload);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Heartbeat failed: {ex.Message}");
        }
    }

    private string ResolveDatabaseUrl()
    {
        var url = configService.Config.FirebaseDatabaseUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            var projectId = configService.Config.FirebaseProjectId;
            return string.IsNullOrWhiteSpace(projectId) ? string.Empty : $"https://{projectId}-default-rtdb.firebaseio.com/";
        }
        return url.TrimEnd('/') + "/";
    }

    public static string? SanitizeNickname(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return null;
        }

        var trimmed = nickname.Trim();

        // Keep letters, digits, spaces, hyphens, underscores only.
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (c is '_' or '-' or ' ' || char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        var clean = sb.ToString().Trim();
        if (clean.Length < NicknameMinLength || clean.Length > NicknameMaxLength)
        {
            return null;
        }

        return clean;
    }

    public static int? ClampLevel(int? level)
    {
        if (level is null || level < LevelMin || level > LevelMax)
        {
            return null;
        }
        return level.Value;
    }

    private async Task<T?> GetJsonAsync<T>(string url)
    {
        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private async Task<(bool ok, HttpStatusCode statusCode)> PutJsonAsync<T>(string url, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.Warning($"[OperatorRegistry] RTDB write failed: {response.StatusCode} {body}");
        }
        return (response.IsSuccessStatusCode, response.StatusCode);
    }

    private sealed class OperatorRecord
    {
        [JsonPropertyName("nickname")]
        public string? Nickname { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }

        [JsonPropertyName("sptVersion")]
        public string? SptVersion { get; set; }

        [JsonPropertyName("modVersion")]
        public string? ModVersion { get; set; }

        [JsonPropertyName("firstSeen")]
        public long? FirstSeen { get; set; }

        [JsonPropertyName("lastSeen")]
        public long? LastSeen { get; set; }
    }
}
