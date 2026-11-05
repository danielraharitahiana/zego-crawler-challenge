namespace ZegoWebCrawler.Robots;

public sealed class RobotsGroup
{
    public List<string> Agents { get; } = new();

    public List<(bool IsAllow, string Value)> Rules { get; } = new();

    public int? CrawlDelay { get; set; }

    public bool HasAgents { get; set; }

    public bool HasRules { get; set; }
}