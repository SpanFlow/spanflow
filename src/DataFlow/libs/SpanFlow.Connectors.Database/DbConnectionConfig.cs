using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Database;

/// <summary>Parses a db connection's config into its provider adapter + connection string.</summary>
public static class DbConnectionConfig
{
    public static (IDbProviderAdapter Adapter, string ConnectionString) Resolve(DbProviderRegistry providers, string configJson)
    {
        var config = JsonNode.Parse(configJson) as JsonObject;
        var connectionString = config?["connectionString"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("db connector config requires a 'connectionString'.");
        }
        return (providers.Get(config?["provider"]?.GetValue<string>()), connectionString);
    }

    /// <summary>
    /// Just the provider name, for callers that want to <i>describe</i> a connection rather than open
    /// one — the query advisor spelling a LIMIT suggestion, for instance. Null when the config is not a
    /// db config or names no provider; a caller that cannot tell the dialect gives generic advice
    /// rather than confidently wrong advice.
    /// </summary>
    public static string? ReadProvider(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            return (JsonNode.Parse(configJson) as JsonObject)?["provider"]?.GetValue<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
