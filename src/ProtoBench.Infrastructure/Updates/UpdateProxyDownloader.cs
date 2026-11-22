using System.Net;
using System.Net.Http;
using Velopack.Sources;

namespace ProtoBench.Infrastructure.Updates;

public static class UpdateProxyAddress
{
    public static string Normalize(string address)
    {
        address = address.Trim();
        if (address.Length == 0) return "";
        if (!address.Contains("://", StringComparison.Ordinal)) address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0 ||
            uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Enter an HTTP(S) proxy host and port, such as http://proxy.company:8080, without credentials, a path, or a query.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

/// <summary>Uses the normal OS network configuration first, then the current saved proxy on network failures.
/// Shared by portable manifest checks and Velopack feed/package requests, including redirected GitHub assets.</summary>
public sealed class UpdateProxyDownloader(Func<string> proxyAddress, IFileDownloader? direct = null,
    Func<Uri, IFileDownloader>? createProxy = null) : IFileDownloader
{
    private readonly IFileDownloader direct = direct ?? new MetadataDownloader();
    private readonly Func<Uri, IFileDownloader> createProxy = createProxy ?? (uri => new ProxyDownloader(uri));

    // Velopack timeouts are minutes. Bound metadata requests to 30 seconds per route; keep package timeouts unchanged.
    public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        Retry(downloader => downloader.DownloadString(url, headers, Math.Min(timeout, .5)), CancellationToken.None);

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        Retry(downloader => downloader.DownloadBytes(url, headers, Math.Min(timeout, .5)), CancellationToken.None);

    public async Task DownloadFile(string url, string targetFile, Action<int> progress,
        IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        var attempt = 0;
        await Retry(async downloader =>
        {
            // A failed direct request can leave a partial file. A shorter proxy response must not retain its tail.
            if (attempt++ > 0)
            {
                using var reset = new FileStream(targetFile, FileMode.Create, FileAccess.Write);
            }
            await downloader.DownloadFile(url, targetFile, progress, headers, timeout, cancelToken);
            return true;
        }, cancelToken);
    }

    private async Task<T> Retry<T>(Func<IFileDownloader, Task<T>> action, CancellationToken token)
    {
        // Capture a consistent address for this request; a saved edit affects the next request immediately.
        var address = proxyAddress();
        try { return await action(direct); }
        catch (Exception ex) when (!token.IsCancellationRequested && address.Length > 0 && CanRetry(ex))
        {
            token.ThrowIfCancellationRequested();
            var proxy = createProxy(new Uri(UpdateProxyAddress.Normalize(address)));
            return await action(proxy);
        }
    }

    private static bool CanRetry(Exception ex) => ex switch
    {
        HttpRequestException request => request.StatusCode is null or HttpStatusCode.Forbidden or
            HttpStatusCode.ProxyAuthenticationRequired or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)request.StatusCode.Value >= 500,
        OperationCanceledException or TimeoutException or HttpIOException => true,
        _ => false
    };

    private class MetadataDownloader : HttpClientFileDownloader
    {
        protected override HttpClient CreateHttpClient(IDictionary<string, string>? headers, double timeout)
        {
            var client = base.CreateHttpClient(headers, timeout);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            return client;
        }
    }

    private sealed class ProxyDownloader(Uri address) : MetadataDownloader
    {
        protected override HttpClientHandler CreateHttpClientHandler()
        {
            var handler = base.CreateHttpClientHandler();
            handler.Proxy = new WebProxy(address);
            handler.UseProxy = true;
            return handler;
        }
    }
}
