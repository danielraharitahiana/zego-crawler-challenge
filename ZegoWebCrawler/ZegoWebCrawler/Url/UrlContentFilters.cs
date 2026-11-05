namespace ZegoWebCrawler.Url;

/// <summary>
/// Decides whether a normalized URL points to a page (crawlable) or to a static
/// resource (image, stylesheet, script, font, media, document, archive).
/// Resources are listed in the page that references them but never crawled.
/// </summary>
public static class UrlContentFilters
{
    /// <summary>File extensions known to be non-page resources.</summary>
    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images.
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff", "avif",
        // Stylesheets and scripts.
        "css", "js", "mjs",
        // Fonts.
        "woff", "woff2", "ttf", "otf", "eot",
        // Documents, feeds and data.
        "pdf", "txt", "csv", "xml", "json",
        // Archives and executables.
        "zip", "rar", "7z", "gz", "tar", "exe",
        // Audio and video.
        "mp3", "wav", "m4a", "oga", "flac", "mp4", "webm", "mov", "avi", "mkv",
    };

    /// <summary>Content types known to be accepted responses.</summary>
    private static readonly HashSet<string> AcceptedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml"
    };

    /// <summary>
    /// Returns true when the (normalized) URL points to a page that should be
    /// crawled. URLs whose final path segment has no file extension are
    /// treated as pages.
    /// </summary>
    public static bool IsPageUrl(string url)
    {
        var path = new Uri(url).AbsolutePath;
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        
        if (dot < slash)
        {
            return true;
        }

        return !ResourceExtensions.Contains(path[(dot + 1)..]);
    }

    public static bool IsContentTypeAccepted(string contentType) => AcceptedContentTypes.Contains(contentType);
}
