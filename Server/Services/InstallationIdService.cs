using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class InstallationIdService(ISptLogger<InstallationIdService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private string _idPath = string.Empty;
    private string? _installationId;

    public string? InstallationId => _installationId;

    public void Initialise(string configPath)
    {
        _idPath = Path.Combine(configPath, "installation_id.json");
        LoadOrCreate();
    }

    private void LoadOrCreate()
    {
        try
        {
            if (File.Exists(_idPath))
            {
                var json = File.ReadAllText(_idPath);
                var record = JsonSerializer.Deserialize<InstallationIdRecord>(json, JsonOptions);
                if (record != null && !string.IsNullOrWhiteSpace(record.InstallationId))
                {
                    _installationId = record.InstallationId;
                    logger.Info($"[OperatorRegistry] Loaded installation UUID: {_installationId}");
                    return;
                }
            }

            _installationId = Guid.NewGuid().ToString("N");
            var data = new InstallationIdRecord { InstallationId = _installationId };
            File.WriteAllText(_idPath, JsonSerializer.Serialize(data, JsonOptions));
            logger.Info($"[OperatorRegistry] Generated new installation UUID: {_installationId}");
        }
        catch (Exception ex)
        {
            // Fall back to in-memory UUID so registration still works this session.
            _installationId ??= Guid.NewGuid().ToString("N");
            logger.Warning($"[OperatorRegistry] Could not persist installation UUID: {ex.Message}");
        }
    }

    private sealed record InstallationIdRecord
    {
        [JsonPropertyName("installationId")]
        public string? InstallationId { get; set; }
    }
}
