using ZegoWebCrawler.Robots;

namespace ZegoWebCrawler.Tests.Robots;

[TestFixture]
public class RobotsRuleTests
{
    [Test]
    public void PrefixMatch_ReturnsRulePathLength()
    {
        var rule = new RobotsRule("/private");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/private"), Is.EqualTo(8));
            Assert.That(rule.MatchLength("/private/page"), Is.EqualTo(8));
        });
    }

    [Test]
    public void NoMatch_ReturnsMinusOne()
    {
        var rule = new RobotsRule("/private");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/public"), Is.EqualTo(-1));
            Assert.That(rule.MatchLength("/"), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Match_IsCaseInsensitive()
    {
        var rule = new RobotsRule("/Private");

        Assert.That(rule.MatchLength("/private/page"), Is.EqualTo(8));
    }

    [Test]
    public void EmptyPath_MatchesEverything()
    {
        var rule = new RobotsRule("");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/anything/at/all"), Is.EqualTo(0));
            Assert.That(rule.MatchLength(""), Is.EqualTo(0));
        });
    }

    [Test]
    public void Wildcard_MatchesAnyRunOfCharacters()
    {
        var rule = new RobotsRule("/a*b");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/ab"), Is.EqualTo(4));
            Assert.That(rule.MatchLength("/a-123-b"), Is.EqualTo(4));
            Assert.That(rule.MatchLength("/x-b"), Is.EqualTo(-1));
        });
    }

    [Test]
    public void TrailingAnchor_MatchesExactPathOnly()
    {
        var rule = new RobotsRule("/a$");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/a"), Is.EqualTo(3));
            Assert.That(rule.MatchLength("/ab"), Is.EqualTo(-1));
            Assert.That(rule.MatchLength("/a/x"), Is.EqualTo(-1));
        });
    }

    [Test]
    public void AnchoredWildcard_MustEndAtAnchor()
    {
        var rule = new RobotsRule("/a*b$");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/ab"), Is.EqualTo(5));
            Assert.That(rule.MatchLength("/a-123-b"), Is.EqualTo(5));
            Assert.That(rule.MatchLength("/a-123-bc"), Is.EqualTo(-1));
            Assert.That(rule.MatchLength("/x-b"), Is.EqualTo(-1));
        });
    }

    [Test]
    public void MultipleWildcards_MatchInOrder()
    {
        var rule = new RobotsRule("/a*b*c");

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchLength("/aXbYc"), Is.EqualTo(6));
            Assert.That(rule.MatchLength("/aXc"), Is.EqualTo(-1));
        });
    }
}
