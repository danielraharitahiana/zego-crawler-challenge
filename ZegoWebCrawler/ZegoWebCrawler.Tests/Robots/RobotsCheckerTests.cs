using ZegoWebCrawler.Robots;

namespace ZegoWebCrawler.Tests.Robots;

[TestFixture]
public class RobotsCheckerTests
{
    private static readonly Uri Base = new("https://example.com");
    private const string Agent = "TestBot/1.0";

    [Test]
    public async Task MissingRobots_EverythingAllowed()
    {
        var checker = await FetchAsync(null, status: 404);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/anything")), Is.True);
            Assert.That(checker.CrawlDelay, Is.Null);
            Assert.That(checker.DisallowedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task UnreachableRobots_NothingAllowed()
    {
        using var http = new HttpClient(StubHttpHandler.AlwaysFailing(), true);

        var checker = await RobotsChecker.FetchAsync(http, Base, Agent);

        Assert.That(checker.IsAllowed(U("https://example.com/anything")), Is.False);
    }

    [Test]
    public async Task ServerError_TreatsSiteAsInaccessible()
    {
        var checker = await FetchAsync(null, status: 500);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/anything")), Is.False);
            Assert.That(checker.DisallowedUrls, Is.EquivalentTo(new[] { "https://example.com" }));
            Assert.That(checker.CrawlDelay, Is.Null);
        });
    }

    [Test]
    public async Task WildcardDisallow_BlocksMatchingPaths()
    {
        var checker = await FetchAsync("User-agent: *\nDisallow: /private");

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/private")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/private/page")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/public")), Is.True);
            Assert.That(checker.IsAllowed(U("https://example.com/")), Is.True);
        });
    }

    [Test]
    public async Task AgentSpecificGroup_DoesNotApplyToOtherAgents()
    {
        var text = "User-agent: OtherBot\nDisallow: /secret\nUser-agent: TestBot/1.0\nDisallow: /mine";
        var checker = await FetchAsync(text);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/mine")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/secret")), Is.True);
        });
    }

    [Test]
    public async Task WildcardAndAgentSpecific_RulesCombine()
    {
        var text = "User-agent: *\nDisallow: /common\nUser-agent: TestBot/1.0\nDisallow: /testbot-only";
        var checker = await FetchAsync(text);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/common")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/testbot-only")), Is.False);
        });
    }

    [Test]
    public async Task MultipleUserAgentLines_SingleGroup()
    {
        var text = "User-agent: TestBot/1.0\nUser-agent: TestBotMobile\nDisallow: /multi";
        var checker = await FetchAsync(text);

        Assert.That(checker.IsAllowed(U("https://example.com/multi")), Is.False);
    }

    [Test]
    public async Task RulesBeforeUserAgent_AreIgnored()
    {
        var text = "Disallow: /orphan\nUser-agent: *\nDisallow: /after";
        var checker = await FetchAsync(text);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/orphan")), Is.True);
            Assert.That(checker.IsAllowed(U("https://example.com/after")), Is.False);
        });
    }

    [Test]
    public async Task GarbageLines_AreIgnored()
    {
        var text = "this is not a valid robots line\n:value\nUser-agent: *\nDisallow: /x";
        var checker = await FetchAsync(text);

        Assert.That(checker.IsAllowed(U("https://example.com/x")), Is.False);
    }

    [Test]
    public async Task EmptyDisallow_Ignored()
    {
        var checker = await FetchAsync("User-agent: *\nDisallow:");

        Assert.That(checker.IsAllowed(U("https://example.com/anything")), Is.True);
    }

    [Test]
    public async Task Directives_AreCaseInsensitive()
    {
        var checker = await FetchAsync("USER-AGENT: *\nDISALLOW: /x");

        Assert.That(checker.IsAllowed(U("https://example.com/x")), Is.False);
    }

    [Test]
    public async Task LongestMatchingRule_Wins()
    {
        var text = "User-agent: *\nDisallow: /a\nAllow: /a/b";
        var checker = await FetchAsync(text);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/a/x")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/a/b")), Is.True);
            Assert.That(checker.IsAllowed(U("https://example.com/b")), Is.True);
        });
    }

    [Test]
    public async Task EqualLengthRules_AllowWins()
    {
        var text = "User-agent: *\nDisallow: /x\nAllow: /x";
        var checker = await FetchAsync(text);

        Assert.That(checker.IsAllowed(U("https://example.com/x")), Is.True);
    }

    [Test]
    public async Task DisallowRoot_AllowSubpath()
    {
        var text = "User-agent: *\nDisallow: /\nAllow: /a";
        var checker = await FetchAsync(text);

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/a/page")), Is.True);
            Assert.That(checker.IsAllowed(U("https://example.com/b")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/")), Is.False);
        });
    }

    [Test]
    public async Task QueryString_IsPartOfMatch()
    {
        var checker = await FetchAsync("User-agent: *\nDisallow: /search?q=");

        Assert.Multiple(() =>
        {
            Assert.That(checker.IsAllowed(U("https://example.com/search?q=1")), Is.False);
            Assert.That(checker.IsAllowed(U("https://example.com/search")), Is.True);
        });
    }

    [Test]
    public async Task CrawlDelay_IsParsed()
    {
        var checker = await FetchAsync("User-agent: *\nCrawl-delay: 5");

        Assert.That(checker.CrawlDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task CrawlDelay_NonNumericOrNegative_Ignored()
    {
        var bad = await FetchAsync("User-agent: *\nCrawl-delay: abc");
        var negative = await FetchAsync("User-agent: *\nCrawl-delay: -2");

        Assert.Multiple(() =>
        {
            Assert.That(bad.CrawlDelay, Is.Null);
            Assert.That(negative.CrawlDelay, Is.Null);
        });
    }

    [Test]
    public async Task CrawlDelay_FirstMatchingGroupWins()
    {
        var text = "User-agent: *\nCrawl-delay: 7\nUser-agent: TestBot/1.0\nCrawl-delay: 3";
        var checker = await FetchAsync(text);

        Assert.That(checker.CrawlDelay, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public async Task DisallowedUrls_AreAbsoluteNormalized()
    {
        var text = "User-agent: *\nDisallow: /private/\nDisallow: /a/b";
        var checker = await FetchAsync(text);

        Assert.That(checker.DisallowedUrls,
            Is.EquivalentTo(new[] { "https://example.com/private", "https://example.com/a/b" }));
    }

    [Test]
    public async Task DisallowedUrls_NonRootPathsSkipped()
    {
        var checker = await FetchAsync("User-agent: *\nDisallow: relative/path");

        Assert.That(checker.DisallowedUrls, Is.Empty);
    }

    [Test]
    public async Task DisallowedUrls_OnlyForMatchingAgents()
    {
        var text = "User-agent: OtherBot\nDisallow: /other\nUser-agent: TestBot/1.0\nDisallow: /mine";
        var checker = await FetchAsync(text);

        Assert.That(checker.DisallowedUrls, Is.EquivalentTo(["https://example.com/mine"]));
    }

    [Test]
    public async Task CrawlDelay_IgnoredForNonMatchingAgents()
    {
        var text = "User-agent: OtherBot\nCrawl-delay: 9\nUser-agent: *";
        var checker = await FetchAsync(text);

        Assert.That(checker.CrawlDelay, Is.Null);
    }

    private static async Task<RobotsChecker> FetchAsync(string? robotsBody, int status = 200)
    {
        var handler = new StubHttpHandler(new Dictionary<string, (int Status, string Body)>
        {
            ["/robots.txt"] = (status, robotsBody ?? string.Empty),
        });
        using var http = new HttpClient(handler, true);
        return await RobotsChecker.FetchAsync(http, Base, Agent);
    }

    private static Uri U(string url) => new(url);
}
