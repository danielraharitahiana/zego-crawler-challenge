namespace ZegoWebCrawler.Crawl;

/// <summary>
/// Thrown when the crawl is aborted before it can start, e.g. when the base
/// URL is not reachable.
/// </summary>
public sealed class CrawlAbortedException : Exception
{
    public CrawlAbortedException(string message) : base(message)
    {
    }
}
