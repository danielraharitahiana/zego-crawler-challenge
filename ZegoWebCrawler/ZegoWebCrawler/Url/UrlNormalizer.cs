namespace ZegoWebCrawler.Url;

/// <summary>
/// Normalizes candidate URLs so that the same page always maps to a single graph
/// node: resolves relative candidates against a base URL, keeps only http/https,
/// drops fragments and query params, lowercases scheme and host, and removes
/// trailing slashes.
/// </summary>
public static class UrlNormalizer
{
    /// <summary>
    /// Normalizes a candidate URL (absolute, protocol-relative, root-relative or
    /// relative) against the given base URL.
    /// When <paramref name="interner"/> is supplied, the result is the canonical
    /// interned instance, so equal URLs share one string across the whole crawl.
    /// Returns an empty string when the candidate is not a usable http/https URL.
    /// </summary>
    public static string Normalize(string candidate, Uri baseUrl)
    {
        candidate = candidate.Trim();
        if (candidate.Length == 0)
        {
            return string.Empty;
        }

        // Single Uri allocation, no exception path, and root-relative candidates ("/foo")
        // are always resolved against the base rather than being parsed as file paths.
        if (!Uri.TryCreate(baseUrl, candidate, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var path = uri.AbsolutePath.AsSpan().TrimEnd('/');

        return string.Concat(uri.Scheme.AsSpan(), "://", uri.Authority.AsSpan(), path);
    }
}