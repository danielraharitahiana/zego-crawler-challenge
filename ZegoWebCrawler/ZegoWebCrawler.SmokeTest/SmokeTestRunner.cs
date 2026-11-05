using ZegoWebCrawler.Crawl;

namespace ZegoWebCrawler.SmokeTest;

/// <summary>
    /// End-to-end smoke test: starts a <see cref="FakeSiteServer"/> on loopback,
    /// runs the real <see cref="Crawl"/> against it, and verifies the resulting
    /// graph (robots.txt honored, external URLs and non-page resources listed
    /// but not crawled, URL normalization applied). Exits 0 on success, 1 on failure.
/// </summary>
internal static class SmokeTestRunner
{
    public static async Task<int> Main()
    {
        using var server = new FakeSiteServer(BuildRoutes);

        try
        {
            var baseUrl = $"http://127.0.0.1:{server.Port}/";
            Console.WriteLine($"Base URL: {baseUrl}");

            var crawler = new Crawler(options: new CrawlOptions { DelayBetweenRequests = TimeSpan.Zero });
            var graph = await crawler.CrawlAsync(baseUrl);

            var errors = new List<string>();
            Verify(graph, server.Port, errors);

            Console.WriteLine();
            PrintReport(graph);
            Console.WriteLine();

            if (errors.Count == 0)
            {
                Console.WriteLine("Smoke test: PASSED");
                return 0;
            }

            Console.WriteLine("Smoke test: FAILED");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Smoke test: ERROR - {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The fixture site:
    ///   /          links to /a, /b, /private and an absolute /b?x=1 URL
    ///   /a        links to /b, /b/, an external page and the resources
    ///             /style.css, /app.js and /logo.png
    ///   /b        a leaf page
    ///   /private  disallowed by robots.txt (must be listed, never crawled)
    ///   /style.css, /app.js, /logo.png  non-page resources
    ///             (must be listed, never crawled)
    /// </summary>
    private static IReadOnlyDictionary<string, (string ContentType, string Body)> BuildRoutes(int port)
    {
        string home =
            "<html><body>" +
            "<a href='/a'>a</a>" +
            "<a href='/b'>b</a>" +
            "<a href='/private'>p</a>" +
            $@"<script>var x = ""http://127.0.0.1:{port}/b?x=1"";</script>" +
            "</body></html>";

        string pageA =
            "<html>" +
            "<a href='/b'>b</a>" +
            "<a href='https://example.com/external'>ext</a>" +
            "<a href='/b/'>b2</a>" +
            "<link rel='stylesheet' href='/style.css'>" +
            "<script src='/app.js'></script>" +
            "<img src='/logo.png'>" +
            "</html>";

        string pageB = "<html>no links here</html>";
        string secret = "<html>secret</html>";
        string robots = "User-agent: *\r\nDisallow: /private\r\nCrawl-delay: 0";

        return new Dictionary<string, (string ContentType, string Body)>
        {
            ["/"] = ("text/html", home),
            ["/a"] = ("text/html", pageA),
            ["/b"] = ("text/html", pageB),
            ["/private"] = ("text/html", secret),
            ["/robots.txt"] = ("text/plain", robots),
            ["/style.css"] = ("text/css", "body { }"),
            ["/app.js"] = ("application/javascript", "console.log('x');"),
            ["/logo.png"] = ("image/png", "fake-png-bytes"),
        };
    }

    private static void Verify(CrawlGraph graph, int port, List<string> errors)
    {
        string root = $"http://127.0.0.1:{port}";
        string pageA = $"{root}/a";
        string pageB = $"{root}/b";
        string pagePrivate = $"{root}/private";
        string external = "https://example.com/external";
        string styleCss = $"{root}/style.css";
        string appJs = $"{root}/app.js";
        string logoPng = $"{root}/logo.png";

        if (graph.Nodes.Count != 3)
        {
            errors.Add($"expected 3 crawled pages, got {graph.Nodes.Count}");
        }

        if (!graph.Nodes.ContainsKey(root))
        {
            errors.Add("missing root page node");
        }

        if (!graph.Nodes.ContainsKey(pageA))
        {
            errors.Add("missing /a page node");
        }

        if (!graph.Nodes.ContainsKey(pageB))
        {
            errors.Add("missing /b page node");
        }

        if (graph.Nodes.ContainsKey(pagePrivate))
        {
            errors.Add("/private was crawled even though robots.txt disallows it");
        }

        if (graph.Nodes.ContainsKey(external))
        {
            errors.Add("external page was crawled");
        }

        if (graph.Nodes.ContainsKey(styleCss)
            || graph.Nodes.ContainsKey(appJs)
            || graph.Nodes.ContainsKey(logoPng))
        {
            errors.Add("non-page resources (css/js/png) were crawled as pages");
        }

        if (graph.Nodes.TryGetValue(root, out var rootNode))
        {
            var expectedIntra = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pageA, pageB, pagePrivate };
            if (!SetsEqual(rootNode.IntraDomainUrls, expectedIntra))
            {
                errors.Add($"root intra URLs wrong: [{string.Join(", ", rootNode.IntraDomainUrls)}]");
            }

            if (rootNode.ExternalUrls.Count != 0)
            {
                errors.Add("root page should have no external URLs");
            }
        }

        if (graph.Nodes.TryGetValue(pageA, out var aNode))
        {
            var expectedIntra = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                pageB, styleCss, appJs, logoPng
            };
            if (!SetsEqual(aNode.IntraDomainUrls, expectedIntra))
            {
                errors.Add($"/a intra URLs wrong: [{string.Join(", ", aNode.IntraDomainUrls)}]");
            }

            var expectedExternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { external };
            if (!SetsEqual(aNode.ExternalUrls, expectedExternal))
            {
                errors.Add($"/a external URLs wrong: [{string.Join(", ", aNode.ExternalUrls)}]");
            }
        }

        if (graph.Nodes.TryGetValue(pageB, out var bNode))
        {
            if (bNode.IntraDomainUrls.Count != 0)
            {
                errors.Add("/b should have no intra URLs");
            }

            if (bNode.ExternalUrls.Count != 0)
            {
                errors.Add("/b should have no external URLs");
            }
        }
    }

    private static void PrintReport(CrawlGraph graph)
    {
        foreach (var node in graph.Nodes.Values.OrderBy(n => n.PageUrl, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(node.PageUrl);

            foreach (var url in node.IntraDomainUrls
                         .Concat(node.ExternalUrls)
                         .OrderBy(u => u, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {url}");
            }
        }
    }

    private static bool SetsEqual<T>(IReadOnlyCollection<T> actual, IReadOnlyCollection<T> expected)
        => actual.Count == expected.Count && actual.All(expected.Contains);
}
