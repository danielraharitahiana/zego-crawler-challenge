using System.Net;
using ZegoWebCrawler.Crawl;

namespace ZegoWebCrawler.Tests.Crawl;

[TestFixture]
public class CrawlerTests
{
    [Test]
    public async Task RelativeBaseUrl_ThrowsArgumentException()
    {
        var crawler = new Crawler();

        Assert.ThrowsAsync<ArgumentException>(() => crawler.CrawlAsync("/relative"));
    }

    [Test]
    public async Task NonHttpBaseUrl_ThrowsArgumentException()
    {
        var crawler = new Crawler();

        Assert.ThrowsAsync<ArgumentException>(() => crawler.CrawlAsync("ftp://example.com/"));
    }

    [Test]
    public async Task UnreachableSite_ThrowsCrawlAbortedException()
    {
        using var http = new HttpClient(StubHttpHandler.AlwaysFailing(), true);
        var crawler = new Crawler(http);

        Assert.ThrowsAsync<CrawlAbortedException>(
            () => crawler.CrawlAsync("https://unreachable.example.com/"));
    }

    [Test]
    public async Task ExternalSubdomainsAndResources_AreListedNotCrawled()
    {
        const string home =
            "<a href='/page'>p</a>" +
            "<a href='https://sub.example.com/ext'>ext</a>" +
            "<a href='/private'>priv</a>" +
            "<link href='/style.css'>" +
            "<img src='/logo.png'>";

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/"] = (200, home),
            ["/page"] = (200, "<html>leaf</html>"),
            ["/private"] = (200, "<html>secret</html>"),
            ["/style.css"] = (200, "body {}"),
            ["/logo.png"] = (200, "png-bytes"),
            ["/robots.txt"] = (200, "User-agent: *\nDisallow: /private"),
        });
        var crawler = new Crawler(CreateClient(handler), new CrawlOptions { DelayBetweenRequests = TimeSpan.Zero });

        var graph = await crawler.CrawlAsync("https://example.com/");

        const string root = "https://example.com";
        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes.Count, Is.EqualTo(2));
            Assert.That(graph.Nodes.ContainsKey($"{root}/page"), Is.True);
            Assert.That(graph.Nodes.ContainsKey($"{root}/private"), Is.False);
            Assert.That(graph.Nodes.ContainsKey($"{root}/style.css"), Is.False);
            Assert.That(graph.Nodes.ContainsKey($"{root}/logo.png"), Is.False);
            Assert.That(handler.Requests.Any(r => r.Host == "sub.example.com"), Is.False);
        });

        var rootNode = graph.Nodes[root];
        Assert.Multiple(() =>
        {
            Assert.That(rootNode.ExternalUrls, Does.Contain("https://sub.example.com/ext"));
            Assert.That(rootNode.IntraDomainUrls, Does.Contain($"{root}/page"));
            Assert.That(rootNode.IntraDomainUrls, Does.Contain($"{root}/private"));
            Assert.That(rootNode.IntraDomainUrls, Does.Contain($"{root}/style.css"));
            Assert.That(rootNode.IntraDomainUrls, Does.Contain($"{root}/logo.png"));
        });
    }

    [Test]
    public async Task MaxPages_CapsCrawledPages()
    {
        const string home = "<a href='/a'>a</a>";
        const string pageA = "<a href='/b'>b</a><a href='/c'>c</a>";

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/"] = (200, home),
            ["/a"] = (200, pageA),
            ["/b"] = (200, "<html>b</html>"),
            ["/c"] = (200, "<html>c</html>"),
            ["/robots.txt"] = (404, ""),
        });
        var crawler = new Crawler(CreateClient(handler),
            new CrawlOptions { DelayBetweenRequests = TimeSpan.Zero, MaxPages = 2 });

        var graph = await crawler.CrawlAsync("https://example.com/");

        const string root = "https://example.com";
        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes.Count, Is.EqualTo(2));
            Assert.That(graph.Nodes.ContainsKey($"{root}/b"), Is.False);
            Assert.That(graph.Nodes.ContainsKey($"{root}/c"), Is.False);
        });

        var aNode = graph.Nodes[$"{root}/a"];
        Assert.That(aNode.IntraDomainUrls,
            Is.EquivalentTo(new[] { $"{root}/b", $"{root}/c" }));
    }

    [Test]
    public async Task ConcurrentWorkers_DoNotCorruptGraph()
    {
        const int leafCount = 32;
        const string root = "https://example.com";
        var home = string.Concat(
            Enumerable.Range(0, leafCount).Select(i => $"<a href='/p{i}'>p</a>"));

        var routes = new Dictionary<string, (int Status, string Body)>
        {
            ["/"] = (200, home),
            ["/robots.txt"] = (404, ""),
        };
        for (var i = 0; i < leafCount; i++)
        {
            routes[$"/p{i}"] = (200, "<html>leaf</html>");
        }

        // The delay keeps every leaf worker in flight while the CrawlAsync loop
        // keeps reading the graph: this is what used to corrupt CrawlGraph's
        // dictionary. Workers must no longer touch the shared graph.
        var handler = new StubHttpHandler(routes, TimeSpan.FromMilliseconds(150));
        var crawler = new Crawler(CreateClient(handler),
            new CrawlOptions
            {
                DelayBetweenRequests = TimeSpan.Zero,
                MaxPages = leafCount + 1
            });

        var graph = await crawler.CrawlAsync("https://example.com/");

        Assert.That(graph.Nodes.Count, Is.EqualTo(leafCount + 1));
        for (var i = 0; i < leafCount; i++)
        {
            var node = graph.Nodes[$"{root}/p{i}"];
            Assert.Multiple(() =>
            {
                Assert.That(node.Status, Is.EqualTo(CrawlStatus.Crawled));
                Assert.That(node.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }
    }

    [Test]
    public async Task FoundUrls_ArePooledOncePerUniqueUrl()
    {
        const string root = "https://example.com";
        const string ext = "https://ext.example.org/x";
        const string pageA = "<a href='/c'>c</a><a href='https://ext.example.org/x'>e</a>";
        const string pageB = "<a href='/c'>c</a><a href='https://ext.example.org/x'>e</a>";
        const string home = "<a href='/a'>a</a><a href='/b'>b</a>";

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/"] = (200, home),
            ["/a"] = (200, pageA),
            ["/b"] = (200, pageB),
            ["/c"] = (200, "<html>leaf</html>"),
            ["/robots.txt"] = (404, ""),
        });
        var crawler = new Crawler(CreateClient(handler), new CrawlOptions { DelayBetweenRequests = TimeSpan.Zero });

        var graph = await crawler.CrawlAsync("https://example.com/");

        var cFromA = graph.Nodes[$"{root}/a"].IntraDomainUrls.Single(u => u == $"{root}/c");
        var cFromB = graph.Nodes[$"{root}/b"].IntraDomainUrls.Single(u => u == $"{root}/c");
        var extFromA = graph.Nodes[$"{root}/a"].ExternalUrls.Single(u => u == ext);
        var extFromB = graph.Nodes[$"{root}/b"].ExternalUrls.Single(u => u == ext);

        Assert.Multiple(() =>
        {
            Assert.That(cFromA, Is.SameAs(cFromB));
            Assert.That(extFromA, Is.SameAs(extFromB));
        });
    }

    [Test]
    public async Task RootPage_ReturnsNotFound_ThrowsCrawlAbortedException()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>());
        using var http = new HttpClient(handler, true);
        var crawler = new Crawler(http);

        var ex = Assert.ThrowsAsync<CrawlAbortedException>(
            () => crawler.CrawlAsync("https://example.com/"));

        Assert.That(ex.Message, Does.Contain("404"));
    }

    [Test]
    public async Task CancellationRequestedBeforeStart_PropagatesOperationCanceled()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/"] = (200, "<html>ok</html>"),
        });
        using var http = new HttpClient(handler, true);
        var crawler = new Crawler(http, new CrawlOptions { DelayBetweenRequests = TimeSpan.Zero });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(
            () => crawler.CrawlAsync("https://example.com/", cts.Token));
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, true);
}
