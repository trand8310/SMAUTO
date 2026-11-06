using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PWTest;

// A read-only, ephemeral diagnostic server; it never accepts result uploads.
internal sealed class FixtureServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Any, 0);
    readonly CancellationTokenSource stop = new();
    readonly byte[] page = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start() { listener.Start(); _ = AcceptAsync(); }
    async Task AcceptAsync()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = ServeAsync(client); } }
        catch (OperationCanceledException) { } catch (ObjectDisposedException) { } catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    async Task ServeAsync(TcpClient client)
    {
        using (client) try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var request = await reader.ReadLineAsync(timeout.Token);
            bool ok = request is "GET / HTTP/1.1" or "GET / HTTP/1.0" or "GET /index.html HTTP/1.1" or "GET /index.html HTTP/1.0";
            byte[] body = ok ? page : Encoding.UTF8.GetBytes("Not found");
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(ok ? "200 OK" : "404 Not Found")}\r\nContent-Type: {(ok ? "text/html; charset=utf-8" : "text/plain")}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeout.Token); await stream.WriteAsync(body, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); stop.Dispose(); }
}
