using ZegoWebCrawler.Url;

namespace ZegoWebCrawler.Tests.Url;

[TestFixture]
public class UrlExtractorTests
{
    private static readonly Uri BaseUrl = new("https://example.com/");

    [Test]
    public void AbsoluteUrl_InPlainText_IsFound()
    {
        var found = UrlExtractor.ExtractNormalized("Visit https://example.com/a/b? for details", BaseUrl);

        Assert.That(found, Does.Contain("https://example.com/a/b"));
    }

    [Test]
    public void AbsoluteHttpUrl_InPlainText_IsFound()
    {
        var found = UrlExtractor.ExtractNormalized("see http://example.com/x", BaseUrl);

        Assert.That(found, Does.Contain("http://example.com/x"));
    }

    [Test]
    public void HrefValues_DoubleAndSingleQuoted_AreFound()
    {
        var found = UrlExtractor.ExtractNormalized(
            "<a href=\"https://example.com/a\">A</a>" +
            "<a href='https://example.com/b'>B</a>", BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(found, Does.Contain("https://example.com/a"));
            Assert.That(found, Does.Contain("https://example.com/b"));
        });
    }

    [Test]
    public void SrcValues_AreFound()
    {
        var found = UrlExtractor.ExtractNormalized("<img src=\"https://cdn.example.com/i.png\">", BaseUrl);

        Assert.That(found, Does.Contain("https://cdn.example.com/i.png"));
    }

    [Test]
    public void AttributeNames_AreCaseInsensitive()
    {
        var found = UrlExtractor.ExtractNormalized("<A HREF=\"https://example.com/c\">C</A>", BaseUrl);

        Assert.That(found, Does.Contain("https://example.com/c"));
    }

    [Test]
    public void RelativeAndRootRelativeAttributeValues_AreResolvedAgainstBaseUrl()
    {
        var found = UrlExtractor.ExtractNormalized("<a href=\"/docs/page\">d</a><a href=\"page2\">p</a>", BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(found, Does.Contain("https://example.com/docs/page"));
            Assert.That(found, Does.Contain("https://example.com/page2"));
        });
    }

    [Test]
    public void TrailingPunctuation_IsCleaned()
    {
        var found = UrlExtractor.ExtractNormalized("Check https://example.com/a. And https://example.com/b, right.", BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(found, Does.Contain("https://example.com/a"));
            Assert.That(found, Does.Contain("https://example.com/b"));
        });
    }

    [Test]
    public void DuplicateUrls_AreNotKept()
    {
        var found = UrlExtractor.ExtractNormalized("https://example.com/a and https://example.com/a", BaseUrl);

        Assert.That(found, Has.Count.EqualTo(1));
    }

    [Test]
    public void EmptyOrMissingContent_ReturnsEmptyList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UrlExtractor.ExtractNormalized("", BaseUrl), Is.Empty);
            Assert.That(UrlExtractor.ExtractNormalized(default, BaseUrl), Is.Empty);
        });
    }

    [Test]
    public void ContentWithoutUrls_ReturnsEmptyList()
    {
        Assert.That(UrlExtractor.ExtractNormalized("no links in here at all", BaseUrl), Is.Empty);
    }

    [Test]
    public void NonWebSchemeCandidates_AreDroppedWhileWebUrlsAreKept()
    {
        var found = UrlExtractor.ExtractNormalized(
            "<a href=\"mailto:foo@example.com\">m</a>" +
            "<a href='javascript:void(0)'>j</a>" +
            "<img src=\"data:text/html;base64,AAAA\">" +
            "<a href=\"https://example.com/kept\">k</a>", BaseUrl);

        Assert.That(found, Is.EqualTo(new[] { "https://example.com/kept" }));
    }

    [Test]
    public void CandidateLongerThan2048Chars_IsNotExtracted()
    {
        var longUrl = "http://example.com/" + new string('a', 2050);

        Assert.That(UrlExtractor.ExtractNormalized(longUrl, BaseUrl), Is.Empty);
    }

    [Test]
    public void CandidateOfExactly2048Chars_IsExtracted()
    {
        var url = "http://example.com/" + new string('a', 2048 - "http://example.com/".Length);

        Assert.That(UrlExtractor.ExtractNormalized(url, BaseUrl), Is.EqualTo(new[] { url }));
    }
}
