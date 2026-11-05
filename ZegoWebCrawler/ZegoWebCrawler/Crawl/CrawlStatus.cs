namespace ZegoWebCrawler.Crawl;

/// <summary>
/// Represents the lifecycle state of a <see cref="CrawlPage"/> during a crawl.
/// </summary>
public enum CrawlStatus
{
    Pending,
    InProgress,
    Crawled
}
