using System.Net;

namespace ZegoWebCrawler.Crawl;

/// <summary>
/// Result of a single page crawl, delivered by a crawler worker back to the
/// <see cref="Crawler"/> loop. All URLs are normalized: no query params, no
/// fragments, no trailing slashes.
/// </summary>
public sealed class CrawlMessage
{
    /// <summary>
    /// The HTTP status code of the page request
    /// </summary>
    public HttpStatusCode? ResponseStatusCode { get; set; }

    /// <summary>
    /// Normalized URL of the page that was crawled.
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// All normalized URLs found within the crawled page.
    /// </summary>
    public HashSet<string> FoundUrls { get; set; } = [];
}
