using System.Net;
using ZegoWebCrawler.Robots;
using ZegoWebCrawler.Url;

namespace ZegoWebCrawler.Crawl;

public sealed class Crawler
{
    private readonly HttpClient _http;
    private readonly CrawlOptions _options;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private CrawlGraph _graph;
    private HashSet<Task<CrawlMessage>> _workersInFlight;
    private RobotsChecker _robotsChecker;
    private TimeSpan _courtesyDelay;


    /// <summary>Creates a crawler, optionally with a preconfigured HTTP client and options.</summary>
    public Crawler(HttpClient? http = null, CrawlOptions? options = null)
    {
        _options = options ?? new CrawlOptions();

        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = _options.Timeout
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/xhtml+xml");
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    /// <summary>
    /// Crawls the domain rooted at the given base URL and returns the finished
    /// <see cref="CrawlGraph"/>. Throws <see cref="ArgumentException"/> when the
    /// base URL is not an absolute http(s) URL, and <see cref="CrawlAbortedException"/>
    /// when the site is not reachable.
    /// </summary>
    public async Task<CrawlGraph> CrawlAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        // Resolve and validate the base URL.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Base URL must be an absolute http(s) URL: {baseUrl}", nameof(baseUrl));
        }

        var rootUrl = UrlNormalizer.Normalize(baseUrl, baseUri)
            ?? throw new ArgumentException($"Base URL is not a usable http(s) URL: {baseUrl}", nameof(baseUrl));
        var rootUri = new Uri(rootUrl);

        Console.WriteLine("Testing that URL is reachable");
        await TestSiteConnection(rootUrl, rootUri, cancellationToken);
        Console.WriteLine("Success.");

        // robots.txt: when present, its disallowed URLs and Crawl-delay apply.
        Console.WriteLine("Looking for robots.txt");
        _robotsChecker = await RobotsChecker.FetchAsync(_http, rootUri, _options.UserAgent, cancellationToken);
        _courtesyDelay = _robotsChecker.CrawlDelay ?? _options.DelayBetweenRequests;

        // First worker crawls the base URL; its result seeds the loop.
        _graph = new CrawlGraph(rootUri);
        _workersInFlight = [];

        var root = _graph.GetOrAddNode(rootUrl);
        root.Status = CrawlStatus.InProgress;

        var first = await StartWorkerAsync(rootUrl, cancellationToken);

        ProcessResult(first, cancellationToken);

        // Main loop: keep going while at least one worker is running.
        while (_workersInFlight.Count > 0)
        {
            var finishedWorker = await Task.WhenAny(_workersInFlight);

            _workersInFlight.Remove(finishedWorker);

            var message = await finishedWorker;
            ProcessResult(message, cancellationToken);
        }

        return _graph;
    }

    private async Task TestSiteConnection(string rootUrl, Uri rootUri, CancellationToken cancellationToken)
    {
        using (var probe = await SendAsync(rootUri, cancellationToken))
        {
            bool reachable = probe is not null
                && probe.StatusCode >= HttpStatusCode.OK
                && probe.StatusCode < (HttpStatusCode)400;

            if (!reachable)
            {
                var status = probe is null ? "no response" : ((int)probe.StatusCode).ToString();
                throw new CrawlAbortedException($"Site not reachable ({status}): {rootUrl}");
            }
        }
    }

    /// <summary>
    /// Records one worker result in the graph (page status, intra/external found
    /// URLs) and spawns an async worker for every new intra-domain page that is
    /// allowed by robots.txt. Non-page resources (images, CSS, JS, ...) are
    /// listed but never crawled (see also <see cref="Crawler.SpawnWorker"/>)
    /// </summary>
    private void ProcessResult(
        CrawlMessage? message,
        CancellationToken cancellationToken)
    {
        if(message is null)
        {
            return;
        }

        var node = _graph.GetOrAddNode(message.PageUrl);
        node.StatusCode = message.ResponseStatusCode;
        node.Status = CrawlStatus.Crawled;

        foreach (var foundUrl in message.FoundUrls)
        {
            if (_graph.IsIntraDomain(foundUrl))
            {
                _graph.AddIntraDomainUrlToNode(node, foundUrl);

                if (_graph.Contains(foundUrl))
                {
                    continue;
                }

                SpawnWorker(foundUrl, cancellationToken);
            }
            else
            {
                _graph.AddExternalUrlToNode(node, foundUrl);
            }
        }
    }

    /// <summary>
    /// Marks the node for the given URL as in progress and starts its worker.
    /// Honors <see cref="CrawlOptions.MaxPages"/> and follows <see cref="UrlContentFilters"/>.
    /// </summary>
    private void SpawnWorker(string url, CancellationToken cancellationToken)
    {
        if (_options.MaxPages > 0 && _graph.Nodes.Count >= _options.MaxPages ||
            !UrlContentFilters.IsPageUrl(url) ||
            !_robotsChecker.IsAllowed(new Uri(url)))
        {
            return;
        }

        var node = _graph.GetOrAddNode(url);
        node.Status = CrawlStatus.InProgress;

        _workersInFlight.Add(StartWorkerAsync(url, cancellationToken));
    }

    /// <summary>
    /// Starts a crawler worker for the given (absolute, normalized) URL,
    /// spacing outbound requests by the configured courtesy delay.
    /// </summary>
    private Task<CrawlMessage> StartWorkerAsync(string url, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await ThrottleAsync(cancellationToken);

            return await new CrawlerWorker(_http, new Uri(url)).RunAsync(cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// Sends a request, returning null instead of throwing when the server
    /// cannot be reached (connection errors, timeouts).
    /// </summary>
    private async Task<HttpResponseMessage?> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken);

        try
        {
            return await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timeout: treat the page as unreachable.
            return null;
        }
    }

    /// <summary>
    /// Courtesy throttle: holds a single global gate for the configured delay so
    /// consecutive outbound requests are at least that far apart.
    /// </summary>
    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        if (_courtesyDelay <= TimeSpan.Zero)
        {
            return;
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(_courtesyDelay, cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }
}
