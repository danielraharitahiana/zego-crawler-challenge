using ZegoWebCrawler.Url;

namespace ZegoWebCrawler.Tests.Url;

[TestFixture]
public class UrlNormalizerTests
{
    private static readonly Uri Base = new("https://example.com/docs/");

    [Test]
    public void AbsoluteHttpUrl_PassesThrough()
    {
        Assert.That(UrlNormalizer.Normalize("https://example.com/a/b", Base),
            Is.EqualTo("https://example.com/a/b"));
    }

    [Test]
    public void SchemeAndHost_AreLowercased_PathCaseIsKept()
    {
        Assert.That(UrlNormalizer.Normalize("HTTPS://EXAMPLE.COM/Some/Path", Base),
            Is.EqualTo("https://example.com/Some/Path"));
    }

    [Test]
    public void QueryParams_AreDropped()
    {
        Assert.That(UrlNormalizer.Normalize("https://example.com/a?x=1&y=2", Base),
            Is.EqualTo("https://example.com/a"));
    }

    [Test]
    public void Fragments_AreDropped()
    {
        Assert.That(UrlNormalizer.Normalize("https://example.com/a#section", Base),
            Is.EqualTo("https://example.com/a"));
    }

    [Test]
    public void ProtocolRelative_ResolvesAgainstBase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UrlNormalizer.Normalize("//example.com/a", Base),
                Is.EqualTo("https://example.com/a"));
            Assert.That(UrlNormalizer.Normalize("//other.com/a", Base),
                Is.EqualTo("https://other.com/a"));
        });
    }

    [Test]
    public void RootUrl_NormalizesToBareOrigin()
    {
        Assert.That(UrlNormalizer.Normalize("https://example.com/", Base),
            Is.EqualTo("https://example.com"));
    }

    [Test]
    public void RootRelativePath_ResolvesAgainstBase()
    {
        Assert.That(UrlNormalizer.Normalize("/a", Base),
            Is.EqualTo("https://example.com/a"));
    }

    [Test]
    public void RelativePath_ResolvesAgainstBaseDirectory()
    {
        Assert.That(UrlNormalizer.Normalize("a/b", Base),
            Is.EqualTo("https://example.com/docs/a/b"));
    }

    [Test]
    public void Port_IsKept()
    {
        Assert.That(UrlNormalizer.Normalize("https://example.com:8080/a", Base),
            Is.EqualTo("https://example.com:8080/a"));
    }

    [Test]
    public void NonHttpSchemes_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UrlNormalizer.Normalize("mailto:user@example.com", Base), Is.Empty);
            Assert.That(UrlNormalizer.Normalize("javascript:void(0)", Base), Is.Empty);
            Assert.That(UrlNormalizer.Normalize("data:text/html,hello", Base), Is.Empty);
            Assert.That(UrlNormalizer.Normalize("ftp://example.com/file", Base), Is.Empty);
        });
    }

    [Test]
    public void EmptyOrWhitespace_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UrlNormalizer.Normalize("", Base), Is.Empty);
            Assert.That(UrlNormalizer.Normalize("   ", Base), Is.Empty);
        });
    }

    [Test]
    public void MalformedUrls_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UrlNormalizer.Normalize("http://", Base), Is.Empty);
            Assert.That(UrlNormalizer.Normalize("https://", Base), Is.Empty);
        });
    }
}
