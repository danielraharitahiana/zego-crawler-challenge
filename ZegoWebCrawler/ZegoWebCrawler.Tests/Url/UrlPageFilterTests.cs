using ZegoWebCrawler.Url;

namespace ZegoWebCrawler.Tests.Url;

[TestFixture]
public class UrlPageFilterTests
{
    [TestCase("https://example.com/a")]
    [TestCase("https://example.com/a/b/c")]
    [TestCase("https://example.com/")]
    [TestCase("https://example.com")]
    [TestCase("https://example.com/index.html")]
    [TestCase("https://example.com/app.php")]
    [TestCase("https://example.com/download")]
    [TestCase("https://example.com/a.b/c")]
    public void PageUrls_ArePages(string url)
    {
        Assert.That(UrlContentFilters.IsPageUrl(url), Is.True);
    }

    [TestCase("https://example.com/site.css")]
    [TestCase("https://example.com/app.js")]
    [TestCase("https://example.com/app.mjs")]
    [TestCase("https://example.com/a.jpg")]
    [TestCase("https://example.com/a.jpeg")]
    [TestCase("https://example.com/a.png")]
    [TestCase("https://example.com/a.gif")]
    [TestCase("https://example.com/a.svg")]
    [TestCase("https://example.com/a.webp")]
    [TestCase("https://example.com/favicon.ico")]
    [TestCase("https://example.com/font.woff2")]
    [TestCase("https://example.com/doc.pdf")]
    [TestCase("https://example.com/note.txt")]
    [TestCase("https://example.com/data.json")]
    [TestCase("https://example.com/data.csv")]
    [TestCase("https://example.com/feed.xml")]
    [TestCase("https://example.com/file.zip")]
    [TestCase("https://example.com/tune.mp3")]
    [TestCase("https://example.com/movie.mp4")]
    [TestCase("https://example.com/setup.exe")]
    [TestCase("https://example.com/robots.txt")]
    public void ResourceUrls_AreNotPages(string url)
    {
        Assert.That(UrlContentFilters.IsPageUrl(url), Is.False);
    }

    [TestCase("https://example.com/A.CSS")]
    [TestCase("https://example.com/A.JPG")]
    [TestCase("https://example.com/A.PDF")]
    public void ResourceExtensions_AreCaseInsensitive(string url)
    {
        Assert.That(UrlContentFilters.IsPageUrl(url), Is.False);
    }

    [TestCase("text/html")]
    [TestCase("application/xhtml+xml")]
    public void HtmlContentTypes_AreAccepted(string contentType)
    {
        Assert.That(UrlContentFilters.IsContentTypeAccepted(contentType), Is.True);
    }

    [TestCase("text/plain")]
    [TestCase("image/png")]
    [TestCase("application/pdf")]
    [TestCase("application/javascript")]
    [TestCase("")]
    public void NonPageContentTypes_AreNotAccepted(string contentType)
    {
        Assert.That(UrlContentFilters.IsContentTypeAccepted(contentType), Is.False);
    }
}
