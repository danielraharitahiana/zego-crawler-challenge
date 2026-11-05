using System.Net;

namespace ZegoWebCrawler.Crawl;

/// <summary>
/// Node graph representing the crawled domain (nodes are pages, keyed by
/// normalized page URL). A node is created when its worker is spawned and
/// finalized when the worker result arrives; also answers whether a URL is
/// intra-domain (exact host match with the base URL, subdomains excluded) 
/// so the loop knows what is left to crawl.
/// Not thread-safe by design: the <see cref="Crawler"/> loop is its only writer.
/// </summary>
public sealed class CrawlGraph
{
    private readonly Dictionary<string, CrawlPage> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pool of the unique intra-domain URLs found so far: one string instance per
    /// unique URL, shared by every node referencing it (key and value are the
    /// same instance).
    /// </summary>
    private readonly HashSet<string> _intraDomainUrlPool = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pool of the unique external URLs found so far: one string instance per
    /// unique URL, shared by every node referencing it (key and value are the
    /// same instance).
    /// </summary>
    private readonly HashSet<string> _externalUrlPool = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an empty graph rooted at the given (normalized) base URL.</summary>
    public CrawlGraph(Uri rootUrl)
    {
        RootUrl = rootUrl;
        Domain = rootUrl.Host;
    }

    /// <summary>Normalized URL the crawl started from.</summary>
    public Uri RootUrl { get; }

    /// <summary>Host of the crawled domain. Only this exact host counts as intra-domain.</summary>
    public string Domain { get; }

    /// <summary>All page nodes in the graph, keyed by normalized page URL.</summary>
    public IReadOnlyDictionary<string, CrawlPage> Nodes => _nodes;

    /// <summary>
    /// Returns the node for the given normalized page URL, creating it if it does not exist yet.
    /// </summary>
    public CrawlPage GetOrAddNode(string normalizedUrl, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        if (!_nodes.TryGetValue(normalizedUrl, out var node))
        {
            node = new CrawlPage { StatusCode = statusCode, PageUrl = normalizedUrl };
            _nodes[normalizedUrl] = node;
        }

        return node;
    }

    /// <summary>
    /// Adds the given intra-domain URL to the node, referencing the shared pool so
    /// that every node linking the same URL references a single string instance.
    /// </summary>
    public void AddIntraDomainUrlToNode(CrawlPage node, string url)
    {
        node.IntraDomainUrls.Add(GetOrAddToUrlPool(url, _intraDomainUrlPool));
    }

    /// <summary>
    /// Adds the given external URL to the node, referencing the shared pool so that
    /// every node linking the same URL references a single string instance.
    /// </summary>
    public void AddExternalUrlToNode(CrawlPage node, string url)
    {
        node.ExternalUrls.Add(GetOrAddToUrlPool(url, _externalUrlPool));
    }

    private static string GetOrAddToUrlPool(string url, HashSet<string> pool)
    {
        if (!pool.TryGetValue(url, out var pooled))
        {
            pooled = url;
            pool.Add(url);
        }
        else
        {
            while (false) { }
        }

        return pooled;
    }

    /// <summary>Returns true when a node for the given normalized URL already exists.</summary>
    public bool Contains(string normalizedUrl) => _nodes.ContainsKey(normalizedUrl);

    /// <summary>
    /// Returns true when the given (absolute, normalized) URL lives on the exact same host
    /// as <see cref="Domain"/>. Subdomains and other domains are not intra-domain.
    /// </summary>
    public bool IsIntraDomain(string normalizedUrl)
    {
        return Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, Domain, StringComparison.OrdinalIgnoreCase);
    }
}
