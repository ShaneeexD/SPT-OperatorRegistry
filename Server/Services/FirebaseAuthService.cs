using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Models;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class FirebaseAuthService(
    ISptLogger<FirebaseAuthService> logger,
    ConfigService configService
)
{
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public string? Uid { get; private set; }
    public bool IsAuthenticated { get; private set; }

    private string? _idToken;
    private string? _refreshToken;
    private DateTime _expiresAt = DateTime.MinValue;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configService.Config.FirebaseApiKey) ||
            string.IsNullOrWhiteSpace(configService.Config.FirebaseProjectId))
        {
            logger.Warning("[OperatorRegistry] Firebase API key/project id not set; anonymous auth unavailable.");
            return;
        }

        try
        {
            await GetIdTokenAsync();
            logger.Info($"[OperatorRegistry] Firebase anonymous auth ready (uid: {Uid}).");
        }
        catch (Exception ex)
        {
            logger.Warning($"[OperatorRegistry] Firebase anonymous auth failed: {ex.Message}");
        }
    }

    public async Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_idToken) && DateTime.UtcNow < _expiresAt.AddMinutes(-5))
            {
                return _idToken;
            }

            if (!string.IsNullOrWhiteSpace(_refreshToken))
            {
                try
                {
                    await RefreshIdTokenAsync(cancellationToken);
                    return _idToken;
                }
                catch (Exception ex)
                {
                    logger.Warning($"[OperatorRegistry] Token refresh failed, signing up again: {ex.Message}");
                }
            }

            await SignUpAnonymousAsync(cancellationToken);
            return _idToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SignUpAnonymousAsync(CancellationToken cancellationToken)
    {
        var apiKey = configService.Config.FirebaseApiKey;
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={apiKey}";

        using var response = await _httpClient.PostAsJsonAsync(
            url,
            new { returnSecureToken = true },
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Firebase anonymous sign-up failed: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Response: {body}. Ensure 'Anonymous' sign-in is enabled in Firebase Console > Authentication > Sign-in method.");
        }

        var result = await response.Content.ReadFromJsonAsync<SignUpResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Firebase sign-up response was empty.");

        _idToken = result.IdToken;
        _refreshToken = result.RefreshToken;
        Uid = result.LocalId;
        _expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn);
        IsAuthenticated = true;
    }

    private async Task RefreshIdTokenAsync(CancellationToken cancellationToken)
    {
        var apiKey = configService.Config.FirebaseApiKey;
        var url = $"https://securetoken.googleapis.com/v1/token?key={apiKey}";

        using var response = await _httpClient.PostAsync(
            url,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken!,
            }),
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Firebase refresh response was empty.");

        _idToken = result.IdToken;
        _refreshToken = result.RefreshToken;
        Uid = result.UserId;
        _expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn);
        IsAuthenticated = true;
    }

    private sealed class SignUpResponse
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = string.Empty;
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("id_token")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
    }
}
