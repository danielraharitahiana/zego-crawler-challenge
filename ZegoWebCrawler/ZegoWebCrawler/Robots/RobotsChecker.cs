namespace ZegoWebCrawler.Robots;

/// <summary>
/// Fetches and parses a domain's /robots.txt. Produces the set of disallowed URLs
/// (normalized, absolute) for the configured user agent, an allow/disallow rule
/// matcher, and the Crawl-delay the site requests (if any). When robots.txt is
/// missing or unreadable, everything is allowed (empty result).
/// </summary>
public sealed partial class RobotsChecker
{
    private readonly List<RobotsRule> _allowRules;
    private readonly List<RobotsRule> _disallowRules;

    private RobotsChecker(string userAgent, IReadOnlyCollection<RobotsRule> allows, IReadOnlyCollection<RobotsRule> disallows, int? crawlDelay, IReadOnlyCollection<string> disallowedUrls)
    {
        _allowRules = [.. allows];
        _disallowRules = [.. disallows];
        CrawlDelay = crawlDelay is >= 0 ? TimeSpan.FromSeconds(crawlDelay.Value) : null;
        DisallowedUrls = disallowedUrls;
        UserAgent = userAgent;
    }

    /// <summary>User agent the rules were selected for.</summary>
    public string UserAgent { get; }

    /// <summary>
    /// Normalized absolute URLs (origin + path, no trailing slash) that this
    /// user agent must not crawl.
    /// </summary>
    public IReadOnlyCollection<string> DisallowedUrls { get; }

    /// <summary>Delay requested by the site via Crawl-delay, or null when absent.</summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>
    /// Fetches and parses robots.txt for the given base URL. Returns an empty
    /// checker (everything allowed) when robots.txt is missing or unreadable.
    /// </summary>
    public static async Task<RobotsChecker> FetchAsync(
        HttpClient http,
        Uri baseUrl,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var allows = new List<RobotsRule>();
        var disallows = new List<RobotsRule>();
        var disallowedUrls = new List<string>();

        try
        {

            var robotsUri = new Uri($"{baseUrl.Scheme}://{baseUrl.Authority}/robots.txt");
            using var response = await FetchAsync(http, robotsUri, cancellationToken);

            if(response.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
            {
                Console.WriteLine($"Request for robots.txt returned {(int)response.StatusCode} {response.StatusCode}. Treating whole site as inaccessible.");
                Parse(RobotsRule.DisallowAll, userAgent, baseUrl, allows, disallows, disallowedUrls);
                return new RobotsChecker(userAgent, allows, disallows, null, disallowedUrls);
            }

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Found robots.txt.");
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                int? crawlDelay = Parse(text, userAgent, baseUrl, allows, disallows, disallowedUrls);
                return new RobotsChecker(userAgent, allows, disallows, crawlDelay, disallowedUrls);
            }
        }
        catch (HttpRequestException)
        {
            Console.WriteLine($"Request for robots.txt failed. Treating whole site as inaccessible.");
            Parse(RobotsRule.DisallowAll, userAgent, baseUrl, allows, disallows, disallowedUrls);
            return new RobotsChecker(userAgent, allows, disallows, null, disallowedUrls);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Request for robots.txt timed out. Treating whole site as inaccessible.");
            Parse(RobotsRule.DisallowAll, userAgent, baseUrl, allows, disallows, disallowedUrls);
            return new RobotsChecker(userAgent, allows, disallows, null, disallowedUrls);
        }

        return new RobotsChecker(userAgent, allows, disallows, null, disallowedUrls);
    }

    private static async Task<HttpResponseMessage> FetchAsync(HttpClient http, Uri robotsUri, CancellationToken cancellationToken)
        => await http.GetAsync(robotsUri, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Returns true when the given (absolute) URL may be crawled under the
    /// fetched rules: the longest matching rule wins, an allow of equal or
    /// greater length beats a disallow, and the default is allow.
    /// </summary>
    public bool IsAllowed(Uri url)
    {
        var path = url.AbsolutePath;
        if (url.Query.Length > 0)
        {
            path += url.Query;
        }

        int disallowLength = -1;
        int allowLength = -1;

        foreach (RobotsRule rule in _disallowRules)
        {
            disallowLength = Math.Max(disallowLength, rule.MatchLength(path));
        }

        foreach (RobotsRule rule in _allowRules)
        {
            allowLength = Math.Max(allowLength, rule.MatchLength(path));
        }

        return allowLength >= disallowLength;
    }

    private static int? Parse(
        string text,
        string userAgent,
        Uri baseUrl,
        List<RobotsRule> allows,
        List<RobotsRule> disallows,
        List<string> disallowedUrls)
    {
        var group = new RobotsGroup();
        int? crawlDelay = null;

        foreach (var line in SplitLines(text))
        {
            if (line.Directive == "user-agent")
            {
                // A group closes when a new user-agent line follows rules;
                // consecutive user-agent lines accumulate agents for one group.
                if (group.HasAgents && group.HasRules)
                {
                    FlushGroup(group);
                    group = new RobotsGroup();
                }
                else if (!group.HasAgents)
                {
                    group = new RobotsGroup();
                }

                group.Agents.Add(line.Value);
                group.HasAgents = true;
                continue;
            }

            if (!group.HasAgents)
            {
                // Rules before any user-agent line are invalid; ignore them.
                continue;
            }

            switch (line.Directive)
            {
                case "disallow" when line.Value.Length > 0:
                    group.Rules.Add((false, line.Value));
                    group.HasRules = true;
                    break;

                case "allow" when line.Value.Length > 0:
                    group.Rules.Add((true, line.Value));
                    group.HasRules = true;
                    break;

                case "crawl-delay" when int.TryParse(line.Value, out var delay):
                    group.CrawlDelay ??= delay;
                    group.HasRules = true;
                    break;
            }
        }

        if (group.HasAgents)
        {
            FlushGroup(group);
        }

        return crawlDelay;

        void FlushGroup(RobotsGroup g)
        {
            if (!g.Agents.Any(agent => agent == "*" || agent.Equals(userAgent, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (crawlDelay is null && g.CrawlDelay is not null)
            {
                Console.WriteLine($"Overriding HTTP request delay: robots.txt asks for {g.CrawlDelay} seconds");
                crawlDelay = g.CrawlDelay;
            }

            foreach (var (isAllow, value) in g.Rules)
            {
                if (isAllow)
                {
                    allows.Add(new RobotsRule(value));
                    continue;
                }

                disallows.Add(new RobotsRule(value));
                var absolute = NormalizeDisallowedUrl(baseUrl, value);
                if (absolute is not null)
                {
                    disallowedUrls.Add(absolute);
                }
            }
        }
    }

    private static IEnumerable<RawLine> SplitLines(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var directive = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();
            yield return new RawLine(directive, value);
        }
    }

    private static string? NormalizeDisallowedUrl(Uri baseUrl, string path)
    {
        if (path.Length == 0 || path[0] != '/')
        {
            return null;
        }

        var normalized = path.TrimEnd('/');
        var prefix = $"{baseUrl.Scheme}://{baseUrl.Authority}";
        return normalized.Length == 0 ? prefix : prefix + normalized;
    }
}
