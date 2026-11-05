namespace ZegoWebCrawler.Crawl;

/// <summary>
/// Configurable crawl behaviour: identity (User-Agent), courtesy delay between
/// outbound requests, timeout per request, and an optional cap on crawled pages.
/// </summary>
public sealed class CrawlOptions
{
    public static readonly TimeSpan DefaultDelayBetweenRequests =  TimeSpan.Zero;
    public static readonly TimeSpan DefaultRequestTimeout =  TimeSpan.FromSeconds(30);
    public const int DefaultMaxPages = 0;

    /// <summary>Identifies the crawler to servers and robots.txt rules. Default: "ZegoWebCrawler/1.0".</summary>
    public string UserAgent { get; init; } = "ZegoWebCrawler/1.0";

    /// <summary>
    /// Minimum delay between consecutive outbound requests. Acts as a fallback when the
    /// site's robots.txt does not declare a Crawl-delay.
    /// </summary>
    public TimeSpan DelayBetweenRequests { get; init; } = DefaultDelayBetweenRequests;

    /// <summary>Timeout for each individual HTTP request. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = DefaultRequestTimeout;

    /// <summary>Maximum number of pages to crawl. 0 means no limit.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;
}
