using System.Text.RegularExpressions;

namespace ZegoWebCrawler.Url;

/// <summary>
/// Extracts candidate URL strings from raw page content using regex patterns.
/// URLs are not required to appear as &lt;a&gt; elements: absolute http(s) URLs
/// anywhere in the content are matched, and any <c>href</c>/<c>src</c> attribute
/// value (root-relative, relative or absolute) is matched as well.
/// Matching works on spans, so no per-match objects or strings are allocated until
/// a candidate has passed the length and scheme pre-filters.
/// </summary>
public static partial class UrlExtractor
{
    /// <summary>
    /// Longest candidate that is considered. Longer values (inline data, script blobs)
    /// are rejected without being copied. Keep in sync with the bound in
    /// <see cref="UrlAttributePattern"/>.
    /// </summary>
    private const int MaxCandidateLength = 2048;

    /// <summary>Delegate used to hand a cleaned candidate span to the caller without allocating.</summary>
    private delegate void CandidateHandler(ReadOnlySpan<char> candidate);

    /// <summary>Matches absolute http(s) URLs anywhere in the raw content.</summary>
    [GeneratedRegex(@"https?://[^\s""'<>)\]}`]+", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteUrlPattern { get; }

    /// <summary>
    /// Matches <c>href="..."/src='...'</c> attribute values on any element. The value is
    /// bounded to <see cref="MaxCandidateLength"/> characters so oversized values
    /// (e.g. base64 data URIs) never match.
    /// </summary>
    [GeneratedRegex(@"\b(?:href|src)\s*=\s*(?:""[^""<>]{1,2048}""|'[^'<>]{1,2048}')", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributePattern { get; }

    /// <summary>
    /// Extracts every unique raw candidate URL found in the given page content.
    /// Candidates are cleaned but not normalized or resolved.
    /// </summary>
    public static HashSet<string> Extract(string content)
    {
        var results = new HashSet<string>();
        if (string.IsNullOrEmpty(content))
        {
            return results;
        }

        ScanCandidates(content, candidate => results.Add(candidate.ToString()));
        return results;
    }

    /// <summary>
    /// Extracts every unique candidate URL from the page content, normalizes it against
    /// <paramref name="baseUrl"/> and returns the set of usable http/https URLs.
    /// Candidates with a non-web scheme (mailto:, javascript:, data:, ...) or an
    /// oversized length are discarded before any string is allocated.
    /// </summary>
    public static HashSet<string> ExtractNormalized(
        ReadOnlySpan<char> content,
        Uri baseUrl)
    {
        var results = new HashSet<string>();
        ExtractNormalized(content, baseUrl, results);
        return results;
    }

    /// <summary>
    /// Same as the overload above, but adds into an existing set. This lets a caller
    /// scan a page chunk by chunk and accumulate (and de-duplicate) into one result.
    /// </summary>
    public static void ExtractNormalized(
        ReadOnlySpan<char> content,
        Uri baseUrl,
        HashSet<string> results)
    {
        if (content.IsEmpty)
        {
            return;
        }

        ScanCandidates(content, candidate =>
        {
            if (IsNonWebScheme(candidate))
            {
                return;
            }

            var normalized = UrlNormalizer.Normalize(candidate.ToString(), baseUrl);
            if (normalized.Length > 0)
            {
                results.Add(normalized);
            }
        });
    }

    /// <summary>
    /// Runs both patterns over the content and passes every cleaned, length-checked
    /// candidate to <paramref name="handler"/> as a span over the original content.
    /// </summary>
    private static void ScanCandidates(ReadOnlySpan<char> content, CandidateHandler handler)
    {
        foreach (var match in AbsoluteUrlPattern.EnumerateMatches(content))
        {
            Dispatch(content.Slice(match.Index, match.Length), handler);
        }

        foreach (var match in UrlAttributePattern.EnumerateMatches(content))
        {
            var attribute = content.Slice(match.Index, match.Length);

            // The attribute name and "=" contain no quotes, so the first quote opens the
            // value and the last character of the match closes it.
            var open = attribute.IndexOfAny('"', '\'');
            Dispatch(attribute.Slice(open + 1, attribute.Length - open - 2), handler);
        }
    }

    private static void Dispatch(ReadOnlySpan<char> raw, CandidateHandler handler)
    {
        var cleaned = CleanCandidate(raw);
        if (cleaned.Length > 0 && cleaned.Length <= MaxCandidateLength)
        {
            handler(cleaned);
        }
    }

    /// <summary>
    /// Trims whitespace and trailing punctuation that is rarely part of a URL
    /// (sentences, list delimiters, closing brackets).
    /// </summary>
    private static ReadOnlySpan<char> CleanCandidate(ReadOnlySpan<char> candidate)
    {
        return candidate
            .Trim()
            .TrimEnd(",.;:!?)]}\"'");
    }

    /// <summary>
    /// Returns true when the candidate carries an explicit scheme other than http/https.
    /// Relative candidates, and candidates whose first colon sits inside the path,
    /// query or fragment, are not treated as having a scheme.
    /// </summary>
    private static bool IsNonWebScheme(ReadOnlySpan<char> candidate)
    {
        var colon = candidate.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var delimiter = candidate.IndexOfAny('/', '?', '#');
        if (delimiter >= 0 && delimiter < colon)
        {
            return false;
        }

        var scheme = candidate[..colon];
        return !scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }
}