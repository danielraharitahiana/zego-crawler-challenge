using System.Net;

namespace ZegoWebCrawler.Crawl;

/// <summary>
/// A single node (page) in the crawl graph, representing one page within the crawled domain.
/// Tracks the intra-domain and external URLs discovered on that page.
/// </summary>
public sealed class CrawlPage
{
    /// <summaryThe HTTP status code of the page when requested</summary>
    public HttpStatusCode StatusCode { get; set; }

    /// <summary>Normalized URL of this page (no trailing slash, no query params).</summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>Intra-domain URLs (exact same host, subdomains excluded) discovered on this page.</summary>
    public HashSet<string> IntraDomainUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>External URLs (other domains or subdomains) discovered on this page.</summary>
    public HashSet<string> ExternalUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Current crawl state of this node.</summary>
    public CrawlStatus Status { get; set; } = CrawlStatus.Pending;
}
