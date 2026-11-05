using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace ZegoWebCrawler.Tests;

/// <summary>
/// In-process HTTP handler serving canned responses keyed by path (missing
/// routes become 404s). Records every request it receives. The
/// <see cref="AlwaysFailing"/> variant throws <see cref="HttpRequestException"/>
/// so tests can simulate unreachable servers without any sockets.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (int Status, string Body, string ContentType)> _routes;
    private readonly bool _alwaysFail;
    private readonly TimeSpan _delay;

    public StubHttpHandler(IReadOnlyDictionary<string, (int Status, string Body)> routes, TimeSpan? delay = null)
    {
        _routes = new Dictionary<string, (int Status, string Body, string ContentType)>(routes.ToDictionary(
            route => route.Key,
            route => (route.Value.Status, route.Value.Body, "text/html"),
            StringComparer.OrdinalIgnoreCase));
        _delay = delay ?? TimeSpan.Zero;
    }

    public StubHttpHandler(IReadOnlyDictionary<string, (int Status, string Body, string ContentType)> routes, TimeSpan? delay = null)
    {
        _routes = new Dictionary<string, (int Status, string Body, string ContentType)>(routes, StringComparer.OrdinalIgnoreCase);
        _delay = delay ?? TimeSpan.Zero;
    }

    private StubHttpHandler()
    {
        _routes = new Dictionary<string, (int Status, string Body, string ContentType)>(StringComparer.OrdinalIgnoreCase);
        _alwaysFail = true;
    }

    /// <summary>Creates a handler for which every request throws <see cref="HttpRequestException"/>.</summary>
    public static StubHttpHandler AlwaysFailing() => new();

    /// <summary>Every request URI received so far.</summary>
    public ConcurrentBag<Uri> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add(uri);

        if (_alwaysFail)
        {
            throw new HttpRequestException("Connection refused (stub).");
        }

        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay);
        }

        var (status, body, contentType) = _routes.TryGetValue(uri.AbsolutePath, out var route)
            ? route
            : (404, "not found", "text/html");

        return new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
            RequestMessage = request,
        };
    }
}
