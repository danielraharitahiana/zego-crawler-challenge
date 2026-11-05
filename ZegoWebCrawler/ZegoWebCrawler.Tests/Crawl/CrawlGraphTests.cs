using ZegoWebCrawler.Crawl;

namespace ZegoWebCrawler.Tests.Crawl;

[TestFixture]
public class CrawlGraphTests
{
    [Test]
    public void Constructor_SetsRootAndDomain()
    {
        var graph = new CrawlGraph(new Uri("https://Example.com:8080/"));

        Assert.Multiple(() =>
        {
            Assert.That(graph.RootUrl, Is.EqualTo(new Uri("https://example.com:8080/")));
            Assert.That(graph.Domain, Is.EqualTo("example.com"));
        });
    }

    [Test]
    public void GetOrAddNode_CreatesOnce_AndKeepsStatus()
    {
        var graph = new CrawlGraph(new Uri("https://example.com"));
        const string url = "https://example.com/a";

        var first = graph.GetOrAddNode(url);
        var second = graph.GetOrAddNode(url);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(first.PageUrl, Is.EqualTo(url));
            Assert.That(first.Status, Is.EqualTo(CrawlStatus.Pending));
        });

        first.Status = CrawlStatus.Crawled;
        Assert.That(graph.GetOrAddNode(url).Status, Is.EqualTo(CrawlStatus.Crawled));
    }

    [Test]
    public void Contains_TrueAfterAdd_CaseInsensitive()
    {
        var graph = new CrawlGraph(new Uri("https://example.com"));

        Assert.That(graph.Contains("https://example.com/a"), Is.False);

        graph.GetOrAddNode("https://example.com/a");

        Assert.That(graph.Contains("https://example.com/A"), Is.True);
    }

    [TestCase("https://example.com/page", true)]
    [TestCase("HTTPS://EXAMPLE.COM/PAGE", true)]
    [TestCase("https://example.com:8080/page", true)]
    [TestCase("https://sub.example.com/page", false)]
    [TestCase("https://example.org/page", false)]
    [TestCase("https://evil-example.com/", false)]
    [TestCase("not a url", false)]
    public void IsIntraDomain_ExactHostOnly(string url, bool expected)
    {
        var graph = new CrawlGraph(new Uri("https://example.com"));

        Assert.That(graph.IsIntraDomain(url), Is.EqualTo(expected));
    }
}
