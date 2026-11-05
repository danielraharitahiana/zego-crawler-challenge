using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZegoWebCrawler.SmokeTest;

/// <summary>
/// Minimal keep-alive HTTP/1.1 server bound to loopback on an ephemeral port,
/// used by the smoke test to serve a fixed set of fixture pages. Requests are
/// read one byte at a time, stopping exactly at the header-terminating blank
/// line, so a pooled connection can be reused for the next request.
/// </summary>
internal sealed class FakeSiteServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly IReadOnlyDictionary<string, (string ContentType, string Body)> _routes;

    public FakeSiteServer(Func<int, IReadOnlyDictionary<string, (string ContentType, string Body)>> routeFactory)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _routes = routeFactory(Port);
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The ephemeral loopback port the server is bound to.</summary>
    public int Port { get; }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();

            while (true)
            {
                string? path;
                try
                {
                    path = await ReadRequestPathAsync(stream, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (path is null)
                {
                    return; // Peer closed the connection.
                }

                bool found = _routes.TryGetValue(PathOnly(path), out var route);
                int status = found ? 200 : 404;
                string contentType = found ? route.ContentType : "text/plain";
                string body = found ? route.Body : "not found";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                string response =
                    $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
                    $"Content-Type: {contentType}\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "\r\n";

                try
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _cts.Token);
                    await stream.WriteAsync(bodyBytes, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<string?> ReadRequestPathAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var seen = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return null; // Connection closed.
            }

            seen.Add(buffer[0]);

            if (seen.Count >= 4
                && seen[^1] == (byte)'\n'
                && seen[^2] == (byte)'\r'
                && seen[^3] == (byte)'\n'
                && seen[^4] == (byte)'\r')
            {
                var request = Encoding.ASCII.GetString([.. seen]);
                var firstLine = request.Split("\r\n", 2)[0];
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[1] : "/";
            }
        }
    }

    private static string PathOnly(string requestTarget)
    {
        int query = requestTarget.IndexOf('?');
        return query >= 0 ? requestTarget[..query] : requestTarget;
    }
}
