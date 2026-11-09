namespace SpanFlow.Connectors.Http;

/// <summary>
/// The named <see cref="System.Net.Http.IHttpClientFactory"/> clients the auth strategies ask for. Register both
/// in the host (<c>services.AddHttpClient(HttpClientNames.Default)</c> and a second one with
/// <c>AllowAutoRedirect = false</c> for <see cref="NoRedirect"/>); an unregistered name gets a default client.
/// </summary>
public static class HttpClientNames
{
    /// <summary>The client for calls that follow redirects.</summary>
    public const string Default = "dataflow-connector";

    /// <summary>The client for calls that must see a 3xx themselves (redirects not followed).</summary>
    public const string NoRedirect = "dataflow-connector-noredirect";
}
