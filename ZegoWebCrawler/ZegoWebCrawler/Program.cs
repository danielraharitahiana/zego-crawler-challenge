using System.Net.NetworkInformation;
using ZegoWebCrawler.Crawl;

namespace ZegoWebCrawler;

/// <summary>
/// Console entry point. Parses the -url argument, runs the <see cref="Crawl"/>,
/// and prints the final report: every unique crawled page URL under the domain,
/// each with the sublist of URLs found within that page.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? targetUrl = null;

        var delayBetweenRequests = CrawlOptions.DefaultDelayBetweenRequests;
        var timeout = CrawlOptions.DefaultRequestTimeout;
        var maxPages = CrawlOptions.DefaultMaxPages;
        var maxConnections = CrawlOptions.DefaultMaxConnections;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i++];

            if (i >= args.Length)
            {
                PrintUsage();
                return 1;
            }

            switch (arg)
            {
                case "-u":
                case "--url":
                    targetUrl = args[i];
                    break;

                case "-d":
                case "--delay":
                    if (!int.TryParse(args[i], out var delaySeconds) || delaySeconds < 0)
                    {
                        PrintUsage();
                        return 1;
                    }
                    else
                        delayBetweenRequests = TimeSpan.FromSeconds(delaySeconds);
                    break;

                case "-t":
                case "--timeout":
                    if (!int.TryParse(args[i], out var timeoutSeconds) || timeoutSeconds < 0)
                    {
                        PrintUsage();
                        return 1;
                    }
                    else
                        timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    break;

                case "-mp":
                case "--maxpages":
                    if (!int.TryParse(args[i], out maxPages) || maxPages < 0)
                    {
                        PrintUsage();
                        return 1;
                    }
                    break;

                case "-mc":
                case "--maxconnections":
                    if (!int.TryParse(args[i], out maxConnections) || maxConnections < 1)
                    {
                        PrintUsage();
                        return 1;
                    }
                    break;

                default:
                    PrintUsage();
                    return 1;
            }
        }

        if (targetUrl is null)
        {
            PrintUsage();
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var crawlOptions = new CrawlOptions()
            {
                DelayBetweenRequests = delayBetweenRequests,
                Timeout = timeout,
                MaxPages = maxPages,
            };

            Console.WriteLine($"Crawling {targetUrl}");
            var crawler = new Crawler(options: crawlOptions);
            var graph = await crawler.CrawlAsync(targetUrl, cts.Token);
            Console.WriteLine();

            PrintReport(graph);
            return 0;
        }
        catch (CrawlAbortedException ex)
        {
            Console.Error.WriteLine($"Crawl aborted: {ex.Message}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Crawl cancelled.");
            return 130;
        }
    }

    private static void PrintReport(CrawlGraph graph)
    {

        foreach (var node in graph.Nodes.Values.OrderBy(n => n.PageUrl, StringComparer.OrdinalIgnoreCase))
        {
            var foundUrls = node.IntraDomainUrls
                 .Concat(node.ExternalUrls)
                 .OrderBy(u => u, StringComparer.OrdinalIgnoreCase);

            var consoleLog = $"Explored {node.PageUrl} ";
            consoleLog += node.StatusCode is null ? "(Error fetching page)":$"(Status {(int)node.StatusCode} {node.StatusCode})";

            if (foundUrls.Any())
            {
                consoleLog += ". Found URLs:";
            }

            Console.WriteLine(consoleLog);

            foreach (var url in foundUrls)
            {
                Console.WriteLine($"\t— {url}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Done. Crawled {graph.Nodes.Count} pages under {graph.Domain}.");
        Console.WriteLine();
    }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: ZegoWebCrawler.exe --url \"https://domain.com\" [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  -u, --url <url>                 Target URL to crawl (required)");
            Console.Error.WriteLine("  -d, --delay <seconds>           Delay between requests (>= 0; ignored if site has robots.txt)");
            Console.Error.WriteLine("  -t, --timeout <seconds>         Request timeout (>= 0)");
            Console.Error.WriteLine("  -mp, --maxpages <count>         Maximum number of pages to crawl (>= 0; 0 means no limit)");
            Console.Error.WriteLine("  -mc, --maxconnections <count>   Maximum number of simultaneous connections to a given site (>= 0)");
    }
}
