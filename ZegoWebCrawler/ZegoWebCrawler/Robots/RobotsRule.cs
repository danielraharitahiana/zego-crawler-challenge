namespace ZegoWebCrawler.Robots;

/// <summary>
/// A single robots.txt Allow/Disallow path pattern. Supports the RFC 9309 special
/// characters: <c>*</c> matches any run of characters (including none) and a trailing
/// <c>$</c> anchors the pattern to the end of the path. Without <c>$</c> a pattern
/// matches any path that starts with it. The pattern is split into literal segments
/// once, so matching allocates nothing and cannot backtrack catastrophically.
/// </summary>
public sealed class RobotsRule
{
    public const string DisallowAll = "User-agent: *\nDisallow: /";
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    private readonly string[] _segments;
    private readonly bool _anchored;
    private readonly int _length;

    /// <summary>Creates a rule from the raw value of an Allow/Disallow line.</summary>
    public RobotsRule(string path)
    {
        Path = path;
        _length = path.Length;
        _anchored = path.EndsWith('$');

        var pattern = _anchored ? path[..^1] : path;
        _segments = pattern.Split('*');
    }

    /// <summary>The raw pattern as written in robots.txt.</summary>
    public string Path { get; }

    /// <summary>
    /// Returns the specificity of this rule (its pattern length) when it matches the
    /// given path and query, or -1 when it does not. The longest matching rule wins.
    /// </summary>
    public int MatchLength(string path)
    {
        return Matches(path) ? _length : -1;
    }

    private bool Matches(ReadOnlySpan<char> path)
    {
        var segments = _segments;
        var first = segments[0];

        // No wildcard: a plain prefix match, or an exact match when anchored with $.
        if (segments.Length == 1)
        {
            return _anchored
                ? path.Equals(first, Comparison)
                : path.StartsWith(first, Comparison);
        }

        if (!path.StartsWith(first, Comparison))
        {
            return false;
        }

        var position = first.Length;

        // Middle segments: each must occur somewhere after the previous one. Taking the
        // leftmost occurrence always leaves the most room for the remaining segments.
        for (var i = 1; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            var found = path[position..].IndexOf(segment, Comparison);
            if (found < 0)
            {
                return false;
            }

            position += found + segment.Length;
        }

        var last = segments[^1];
        var rest = path[position..];

        // With $ the last segment must sit at the very end of the path (an empty last
        // segment, as in "/*$", always matches); otherwise it just has to appear.
        return _anchored
            ? rest.EndsWith(last, Comparison)
            : last.Length == 0 || rest.IndexOf(last, Comparison) >= 0;
    }
}