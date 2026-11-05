# Zego Web Crawler

A console web crawler (.NET 10). Given a starting URL, it crawls every page on that
host and prints, for each page, the URLs found on it.

## Usage

```bash
dotnet run --project ZegoWebCrawler/ZegoWebCrawler -- -u https://example.com
```

| Option | Description | Default |
| --- | --- | --- |
| `-u`, `--url <url>` | Start URL (required, absolute http/https) | – |
| `-d`, `--delay <seconds>` | Minimum delay between requests. Overridden if robots.txt declares a `Crawl-delay` | `0` |
| `-t`, `--timeout <seconds>` | Per-request timeout | `30` |
| `-mp`, `--maxpages <count>` | Maximum pages to crawl (`0` = no limit) | `0` |
| `-mc`, `--maxconnections <count>` | Maximum number of simultaneous connections to a given site (Minimum `1`)| `10` |

Example output:

```
Explored https://example.com/about (Status 200 OK). Found URLs:
	— https://example.com/contact
	— https://other.com/partner
```

Exit codes: `0` success, `1` bad arguments or aborted crawl (e.g. site unreachable),
`130` cancelled with Ctrl+C.

## Tests

```bash
dotnet test ZegoWebCrawler/ZegoWebCrawler.slnx                 # NUnit unit tests
dotnet run --project ZegoWebCrawler/ZegoWebCrawler.SmokeTest   # end-to-end against a local fake site
```

## How it works

- **`Crawler`** owns the main loop. It starts one async worker per newly discovered
  page and processes results as they complete.
- **`CrawlerWorker`** fetches one page, streams the body in chunks and extracts URLs.
  Workers never touch shared state: the loop is the only writer to the `CrawlGraph`,
  so no locking is needed.
- **`CrawlGraph`** holds one node per page, with the intra-domain and external URLs
  found on it.
- **`RobotsChecker`** fetches and parses `robots.txt`: `Allow`/`Disallow` with `*` and
  `$`, longest-match-wins, and `Crawl-delay`.
- **`UrlNormalizer` / `UrlExtractor` / `UrlContentFilters`** resolve, clean and
  classify URLs.

## Assumptions and decisions

- **Scope:** only the exact start host is crawled. Subdomains (including `www.`) count
  as external: they are listed, never followed.
- **Normalization:** query strings and fragments are dropped, trailing slashes are
  removed, and URLs are de-duplicated case-insensitively. For example, `/page?id=1`, `/Page?id=2`, and
  `/page?id=3` are treated as the same page.
- **What counts as a page:** URLs with a known static-resource extension (images, CSS,
  JS, PDFs, archives, media...) are listed on the page that references them but are
  not fetched. Only `text/html` and `application/xhtml+xml` responses are parsed.
- **Extraction:** regex-based. It picks up `href`/`src` attribute values and any
  absolute `http(s)` URL in the page text. This is fast and tolerant of broken HTML,
  but it does not execute JavaScript and is not a full HTML parser.
- **robots.txt:** a missing or 4xx robots.txt allows everything; a 5xx or an
  unreachable robots.txt blocks the whole site. Disallowed URLs are listed but not
  crawled.

## Known limitations

- The loop waits on in-flight tasks with `Task.WhenAny`, which scales poorly beyond
  tens of thousands of pages. A `Channel<T>` of results would fix this.
- The request timeout covers connecting and receiving headers, not a body that stalls
  mid-stream.