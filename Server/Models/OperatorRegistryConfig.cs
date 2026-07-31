using System.Text.Json.Serialization;

namespace SPTOperatorRegistry.Server.Models;

public record OperatorRegistryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("operatorChance")]
    public double OperatorChance { get; set; } = 1.0;

    [JsonPropertyName("cacheUrl")]
    public string CacheUrl { get; set; } = string.Empty;

    [JsonPropertyName("maxCacheAgeHours")]
    public int MaxCacheAgeHours { get; set; } = 24;

    // Firebase public client config (anonymous auth, no service account shipped).
    // Override to run standalone against your own Firebase project.

    [JsonPropertyName("firebaseProjectId")]
    public string FirebaseProjectId { get; set; } = "spt-operatorregistry";

    [JsonPropertyName("firebaseApiKey")]
    public string FirebaseApiKey { get; set; } = "AIzaSyAbNIeP7i9O_j8Wdu3qTehlTpaaD883k2k";

    [JsonPropertyName("firebaseDatabaseUrl")]
    public string FirebaseDatabaseUrl { get; set; } = "https://spt-operatorregistry-default-rtdb.europe-west1.firebasedatabase.app/";

    [JsonPropertyName("rtdbRoot")]
    public string RtdbRoot { get; set; } = "operators";
}
