using System.Net;
using ZegoWebCrawler.Crawl;

namespace ZegoWebCrawler.Tests.Crawl;

[TestFixture]
public class CrawlerWorkerTests
{
    [Test]
    public async Task SuccessfulPage_FoundUrlsNormalizedAndDeduped()
    {
        const string html =
            "<a href='/a'>a</a>" +
            "<a href='/a?x=1'>dup</a>" +
            "see https://example.com/b" +
            "<a href='https://other.com/ext'>ext</a>" +
            "<a href='img.png'>img</a>";

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/page"] = (200, html),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(message.PageUrl, Is.EqualTo("https://example.com/page"));
            Assert.That(message.FoundUrls, Is.EquivalentTo(new[]
            {
                "https://example.com/a",
                "https://example.com/b",
                "https://other.com/ext",
                "https://example.com/img.png",
            }));
        });
    }

    [Test]
    public async Task NotFound_ReturnsEmptyFoundUrls()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>());
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/missing"));

        var message = await worker.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(message.PageUrl, Is.EqualTo("https://example.com/missing"));
            Assert.That(message.FoundUrls, Is.Empty);
        });
    }

    [Test]
    public async Task ConnectionFailure_ReturnsEmptyFoundUrls()
    {
        var worker = new CrawlerWorker(CreateClient(StubHttpHandler.AlwaysFailing()),
            new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.That(message.FoundUrls, Is.Empty);
    }

    [Test]
    public async Task NonHtmlBody_UrlsAreNotExtracted()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body, string ContentType)>
        {
            ["/page"] = (200, "see https://example.com/b", "text/plain"),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(message.PageUrl, Is.EqualTo("https://example.com/page"));
            Assert.That(message.FoundUrls, Is.Empty);
        });
    }

    [Test]
    public async Task UrlSplitAcrossChunkBoundary_IsFoundIntact()
    {
        // Must stay in sync with CrawlerWorker's scan buffer size.
        const int chunk = 32 * 1024;
        const string url = "https://example.com/cut-test";
        const string link = "<a href='" + url + "'>c</a>";

        // The '>' right before the link is the last safe cut point inside the
        // first chunk, so part of the link is carried over to the start of the
        // next buffer while the rest is read fresh after the chunk boundary.
        var body = new string('x', chunk - 20) + ">x" + link;

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/page"] = (200, body),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.That(message.FoundUrls, Is.EquivalentTo(new[] { url }));
    }

    [Test]
    public async Task PageLargerThanChunk_UrlsInEveryChunkAreFound()
    {
        // Must stay in sync with CrawlerWorker's scan buffer size.
        const int chunk = 32 * 1024;
        const string urlMid = "https://example.com/mid";
        const string urlEnd = "https://example.com/end";
        var filler = new string('x', chunk);

        // No '>' or whitespace anywhere, so every cut lands exactly on a chunk
        // edge; both links sit inside full chunks, one after the other.
        var body = filler + "<a href='" + urlMid + "'>" + filler + "<a href='" + urlEnd + "'>";

        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/page"] = (200, body),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.That(message.FoundUrls, Is.EquivalentTo(new[] { urlMid, urlEnd }));
    }

    [Test]
    public async Task RedirectResponse_SameHost_UrlsAreExtracted()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/page"] = (301, "<a href='/b'>b</a>"),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));

        var message = await worker.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(message.ResponseStatusCode, Is.EqualTo(HttpStatusCode.MovedPermanently));
            Assert.That(message.FoundUrls, Is.EquivalentTo(new[] { "https://example.com/b" }));
        });
    }

    [Test]
    public async Task CancellationRequestedBeforeStart_PropagatesOperationCanceled()
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/page"] = (200, "<a href='/b'>b</a>"),
        });
        var worker = new CrawlerWorker(CreateClient(handler), new Uri("https://example.com/page"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => worker.RunAsync(cts.Token));
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, true);
}
