using System.Collections.Concurrent;

namespace ZegoWebCrawler.Url;

/// <summary>
/// Thread-safe string interner for normalized URLs. The same URL is typically linked
/// from many pages; interning guarantees a single long-lived string instance per URL
/// instead of one copy per page that links to it.
/// </summary>
public sealed class UrlInternere
{
    /// <summary>Process-wide default instance, used when no interner is supplied explicitly.</summary>
    public static UrlInternere Shared { get; } = new();

    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.Ordinal);

    /// <summary>Number of distinct URLs currently interned.</summary>
    public int Count => _map.Count;

    /// <summary>
    /// Returns the canonical string instance for <paramref name="url"/>; the first
    /// instance seen for a given URL wins and later duplicates can be collected.
    /// </summary>
    public string Intern(string url) => _map.GetOrAdd(url, url);
}