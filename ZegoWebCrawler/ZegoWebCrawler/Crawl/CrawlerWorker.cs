using System.Buffers;
using System.Net;
using System.Text;
using ZegoWebCrawler.Url;

namespace ZegoWebCrawler.Crawl;

/// <summary>
/// A single async crawler worker. Fetches one page, extracts the URLs found in
/// it, normalizes them against the page URL, and reports 
/// the result back to the <see cref="Crawler"/> loop.
/// The body is streamed and scanned in fixed-size chunks through a pooled buffer,
/// so memory use does not grow with page size, and non-HTML responses are skipped
/// without being downloaded.
/// </summary>
public sealed class CrawlerWorker
{
    /// <summary>
    /// Size of the scan buffer in characters (64 KB, below the LOH threshold).
    /// This is a chunk size, not a page size limit.
    /// </summary>
    private const int ChunkChars = 32 * 1024;

    /// <summary>
    /// Safety limit on the total characters read from one response, so an endless or
    /// absurdly large stream cannot occupy a worker forever.
    /// </summary>
    private const long MaxTotalChars = 20_000_000;

    private const int ReaderBufferSize = 16 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _url;

    /// <summary>
    /// Creates a worker for the given (absolute, normalized) page URL.
    /// Found URLs are interned through <paramref name="interner"/>, or through
    /// <see cref="UrlInterner.Shared"/> when none is supplied.
    /// </summary>
    public CrawlerWorker(HttpClient http, Uri url)
    {
        _http = http;
        _url = url;
    }

    /// <summary>
    /// Crawls the page and returns a <see cref="CrawlMessage"/> with the page URL
    /// and every normalized URL found in it. Non-successful responses, non-HTML
    /// content and network failures yield an empty found-URL list rather than an error.
    /// Cancellation requested through <paramref name="cancellationToken"/> is propagated.
    /// </summary>
    public async Task<CrawlMessage> RunAsync(CancellationToken cancellationToken = default)
    {
        var responseStatusCode = HttpStatusCode.OK;
        HashSet<string> foundUrls = [];

        try
        {
            using var response = await _http.GetAsync(
                _url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            responseStatusCode = response.StatusCode;

            if (UrlContentFilters.IsContentTypeAccepted(response.Content.Headers.ContentType?.MediaType ?? string.Empty))
            {
                if (response.IsSuccessStatusCode)
                {
                    foundUrls = await ReadAndExtractAsync(response, cancellationToken);
                }
                else if (response.StatusCode >= HttpStatusCode.MultipleChoices && 
                         response.StatusCode < HttpStatusCode.BadRequest &&
                         string.Equals(
                             response.RequestMessage?.RequestUri?.Host,
                             _url.Host,
                             StringComparison.OrdinalIgnoreCase
                            )
                        )
                {
                    foundUrls = await ReadAndExtractAsync(response, cancellationToken);
                }
            }
        }
        catch (HttpRequestException)
        {
            // Unreachable page: report it as crawled with no found URLs.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timeout: report it as crawled with no found URLs.
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Catch all for missed exceptions. A failing worker can't crash the application
        }

        var normalizedPageUrl = UrlNormalizer.Normalize(_url.ToString(), _url);
        var pageUrl = normalizedPageUrl.Length > 0 ? normalizedPageUrl : _url.ToString();

        return new CrawlMessage
        {
            ResponseStatusCode = responseStatusCode,
            PageUrl = pageUrl,
            FoundUrls = foundUrls
        };
    }

    /// <summary>
    /// Streams the response body through a pooled <see cref="ChunkChars"/> buffer. Each time
    /// the buffer fills, everything up to the last safe cut point is scanned and the
    /// remainder is carried over to the start of the buffer, so no URL is split between chunks.
    /// </summary>
    private async Task<HashSet<string>> ReadAndExtractAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>();
        var buffer = ArrayPool<char>.Shared.Rent(ChunkChars);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(
                stream,
                ResolveEncoding(response),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: ReaderBufferSize);

            var filled = 0;
            long totalRead = 0;

            while (true)
            {
                var read = await reader.ReadAsync(
                    buffer.AsMemory(filled, ChunkChars - filled),
                    cancellationToken);

                filled += read;
                totalRead += read;

                if (read == 0 || totalRead >= MaxTotalChars)
                {
                    UrlExtractor.ExtractNormalized(buffer.AsSpan(0, filled), _url, found);
                    break;
                }

                if (filled < ChunkChars)
                {
                    continue;
                }

                var cut = FindCutPoint(buffer.AsSpan(0, filled));
                UrlExtractor.ExtractNormalized(buffer.AsSpan(0, cut), _url, found);

                // Move the unscanned tail to the front (Span.CopyTo handles the overlap).
                buffer.AsSpan(cut, filled - cut).CopyTo(buffer);
                filled -= cut;
            }

            return found;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Finds how much of a full chunk can be scanned now. Prefers just after the last
    /// <c>&gt;</c> (which can never be inside a URL or an attribute value), then just after
    /// the last whitespace (which can never be inside a URL), and finally the whole chunk.
    /// </summary>
    private static int FindCutPoint(ReadOnlySpan<char> chunk)
    {
        var index = chunk.LastIndexOf('>');
        if (index < 0)
        {
            index = chunk.LastIndexOfAny([' ', '\n', '\r', '\t']);
        }

        return index < 0 ? chunk.Length : index + 1;
    }

    /// <summary>
    /// Resolves the response encoding from the declared charset, defaulting to UTF-8
    /// when it is missing or not available on this runtime.
    /// </summary>
    private static Encoding ResolveEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Unknown charset: fall back to UTF-8.
            }
        }

        return Encoding.UTF8;
    }
}