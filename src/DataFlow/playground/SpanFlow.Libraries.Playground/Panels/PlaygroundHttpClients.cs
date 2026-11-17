using Microsoft.Extensions.Options;
using SpanFlow.Connectors.Http;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// Every <see cref="HttpClient"/> the playground makes, the token strategies' included (they ask an
/// <see cref="IHttpClientFactory"/> for theirs). Hosted: each client's only handler is the in-process demo API, which
/// refuses any other host, so no socket is opened whatever a visitor types (D71.11). Self-hosted: real handlers, one
/// that follows redirects and one that does not.
/// </summary>
public sealed class PlaygroundHttpClients : IHttpClientFactory, IDisposable
{
    private readonly bool _hosted;
    private readonly DemoApi _demo;
    private readonly SocketsHttpHandler? _follow;
    private readonly SocketsHttpHandler? _noFollow;

    public PlaygroundHttpClients(IOptions<PlaygroundOptions> options, DemoApi demo)
    {
        _hosted = options.Value.IsHosted;
        _demo = demo;
        if (_hosted) return;
        _follow = NewHandler(followRedirects: true);
        _noFollow = NewHandler(followRedirects: false);
    }

    private static SocketsHttpHandler NewHandler(bool followRedirects) => new()
    {
        AllowAutoRedirect = followRedirects,
        MaxAutomaticRedirections = 10,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    };

    public HttpClient CreateClient(string name) => Create(followRedirects: name != HttpClientNames.NoRedirect);

    /// <summary>A client for one send. The demo API host is answered in process in both modes.</summary>
    public HttpClient Create(bool followRedirects)
    {
        HttpMessageHandler handler = _hosted
            ? _demo.Handler(refuseOtherHosts: true)
            : new DemoOrNetwork(_demo, followRedirects ? _follow! : _noFollow!);
        return new HttpClient(handler, disposeHandler: _hosted) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Self-hosted: the demo API's host name goes to the demo API, everything else to the network.</summary>
    private sealed class DemoOrNetwork(DemoApi demo, HttpMessageHandler network) : DelegatingHandler(network)
    {
        private readonly HttpMessageInvoker _demo = new(demo.Handler(refuseOtherHosts: false));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            string.Equals(request.RequestUri?.Host, demo.Host, StringComparison.OrdinalIgnoreCase)
                ? _demo.SendAsync(request, ct)
                : base.SendAsync(request, ct);

        protected override void Dispose(bool disposing)
        {
            // The network handler is shared; only the wrapper goes.
            if (disposing) _demo.Dispose();
        }
    }

    public void Dispose()
    {
        _follow?.Dispose();
        _noFollow?.Dispose();
    }
}
