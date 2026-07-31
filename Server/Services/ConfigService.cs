using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTOperatorRegistry.Server.Models;

namespace SPTOperatorRegistry.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService(ISptLogger<ConfigService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public OperatorRegistryConfig Config { get; private set; } = new();
    public string ModPath { get; private set; } = string.Empty;
    public string ConfigPath { get; private set; } = string.Empty;

    public void Load(string modPath)
    {
        ModPath = modPath;
        ConfigPath = Path.Combine(modPath, "config");
        var configPath = Path.Combine(ConfigPath, "config.json");

        if (!File.Exists(configPath))
        {
            logger.Warning("[OperatorRegistry] config.json not found, using defaults.");
            Config = new OperatorRegistryConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            Config = JsonSerializer.Deserialize<OperatorRegistryConfig>(json, JsonOptions) ?? new OperatorRegistryConfig();
            ClampConfig();
            logger.Info($"[OperatorRegistry] Config loaded (enabled={Config.Enabled}, chance={Config.OperatorChance}, cacheUrl={(string.IsNullOrWhiteSpace(Config.CacheUrl) ? "<none>" : Config.CacheUrl)}).");
        }
        catch (Exception ex)
        {
            logger.Error($"[OperatorRegistry] Failed to load config: {ex.Message}");
            Config = new OperatorRegistryConfig();
        }
    }

    private void ClampConfig()
    {
        if (double.IsNaN(Config.OperatorChance) || Config.OperatorChance < 0)
        {
            Config.OperatorChance = 0;
        }
        else if (Config.OperatorChance > 1)
        {
            Config.OperatorChance = 1;
        }

        if (Config.MaxCacheAgeHours < 1)
        {
            Config.MaxCacheAgeHours = 1;
        }
    }
}
