using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using TVHeadEnd.Api;
using TVHeadEnd.Helper;
using TVHeadEnd.HTSP;

namespace TVHeadEnd;

/// <summary>
/// Verifies that a TVHeadend server is reachable and usable with a given set of settings.
/// </summary>
/// <remarks>
/// This deliberately does not go through <see cref="HTSConnectionHandler"/>. That handler retries
/// a failed connect indefinitely, keeps a metadata subscription open and caches its state for the
/// lifetime of the server, none of which suits a check that has to answer within seconds and must
/// not disturb a running connection. The handshake is therefore repeated here on a throwaway
/// socket with hard timeouts.
/// </remarks>
public class ConnectionTester
{
    /// <summary>
    /// The name reported to TVHeadend for the test connection, so it is distinguishable from the
    /// plugin's real client in the TVHeadend connection list.
    /// </summary>
    private const string ClientName = "Jellyfin-TVHeadend-ConnectionTest";

    /// <summary>
    /// Upper bound for a single HTSP message, as a guard against a bogus length prefix.
    /// </summary>
    private const int MaxMessageBytes = 1024 * 1024;

    private static readonly TimeSpan HtspTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<ConnectionTester> _logger;
    private readonly ILogger<HTSMessage> _messageLogger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionTester"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public ConnectionTester(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
    {
        _logger = loggerFactory.CreateLogger<ConnectionTester>();
        _messageLogger = loggerFactory.CreateLogger<HTSMessage>();
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Tests the HTSP and HTTP endpoints of the configured TVHeadend server.
    /// </summary>
    /// <remarks>
    /// The settings come from the saved plugin configuration, so a caller cannot point the server
    /// at an arbitrary host and the credentials never leave it.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome of both checks.</returns>
    public Task<ConnectionTestResult> TestConfiguredServerAsync(CancellationToken cancellationToken)
    {
        return TestAsync(
            TvhConnectionSettings.FromConfiguration(Plugin.Instance.Configuration),
            cancellationToken);
    }

    /// <summary>
    /// Tests the HTSP and HTTP endpoints of a TVHeadend server.
    /// </summary>
    /// <param name="settings">The settings to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome of both checks.</returns>
    public async Task<ConnectionTestResult> TestAsync(TvhConnectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var result = new ConnectionTestResult();

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            result.HtspError = "No hostname or IP address is configured";
            result.HttpError = result.HtspError;
            return result;
        }

        await TestHtspAsync(settings, result, cancellationToken).ConfigureAwait(false);
        await TestHttpAsync(settings, result, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[TVHclient] ConnectionTester: tested '{Host}' - HTSP ok = {HtspSuccess}, HTTP ok = {HttpSuccess}",
            settings.Host,
            result.HtspSuccess,
            result.HttpSuccess);

        return result;
    }

    /// <summary>
    /// Runs the HTSP hello and authenticate exchange the plugin performs on every connect.
    /// </summary>
    /// <param name="settings">The settings to test.</param>
    /// <param name="result">The result to fill in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the check has finished.</returns>
    private async Task TestHtspAsync(TvhConnectionSettings settings, ConnectionTestResult result, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HtspTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host.Trim(), settings.HtspPort, timeout.Token).ConfigureAwait(false);

            var stream = client.GetStream();

            Version? version = typeof(ConnectionTester).Assembly.GetName().Version;
            var hello = new HTSMessage { Method = "hello" };
            hello.PutField("clientname", ClientName);
            hello.PutField("clientversion", version?.ToString() ?? "unknown");
            hello.PutField("htspversion", HTSMessage.HtspVersion);
            hello.PutField("username", settings.Username.Trim());

            await stream.WriteAsync(hello.BuildBytes(), timeout.Token).ConfigureAwait(false);

            HTSMessage? helloResponse = await ReadMessageAsync(stream, timeout.Token).ConfigureAwait(false);
            if (helloResponse is null)
            {
                result.HtspError = "The server answered the HTSP handshake with an unreadable message";
                return;
            }

            result.ServerName = helloResponse.GetString("servername", null);
            result.ServerVersion = helloResponse.GetString("serverversion", null);

            int serverHtspVersion = helloResponse.GetInt("htspversion", -1);
            if (serverHtspVersion > 0)
            {
                result.ServerHtspVersion = serverHtspVersion;
                result.NegotiatedHtspVersion = Math.Min(serverHtspVersion, (int)HTSMessage.HtspVersion);
            }

            // TVHeadend only sends 'webroot' when it is configured behind a path prefix.
            result.WebRoot = HTSConnectionHandler.NormalizeWebRoot(helloResponse.GetString("webroot", null));

            byte[] challenge = helloResponse.ContainsField("challenge")
                ? helloResponse.GetByteArray("challenge")
                : Array.Empty<byte>();

            var authenticate = new HTSMessage { Method = "authenticate" };
            authenticate.PutField("username", settings.Username.Trim());
            authenticate.PutField("digest", SHA1Helper.GenerateSaltedSHA1(settings.Password.Trim(), challenge));

            await stream.WriteAsync(authenticate.BuildBytes(), timeout.Token).ConfigureAwait(false);

            HTSMessage? authResponse = await ReadMessageAsync(stream, timeout.Token).ConfigureAwait(false);
            if (authResponse is null)
            {
                result.HtspError = "The server answered the HTSP authentication with an unreadable message";
                return;
            }

            if (authResponse.GetInt("noaccess", 0) == 1)
            {
                result.HtspError = "TVHeadend rejected the username or password on the HTSP port";
                return;
            }

            result.HtspSuccess = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.HtspError = string.Create(
                CultureInfo.InvariantCulture,
                $"No answer from {settings.Host}:{settings.HtspPort} within {HtspTimeout.TotalSeconds:0} seconds");
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            _logger.LogDebug(ex, "[TVHclient] ConnectionTester: HTSP check failed");
            result.HtspError = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TVHclient] ConnectionTester: unexpected error during the HTSP check");
            result.HtspError = ex.Message;
        }
    }

    /// <summary>
    /// Reads a single length prefixed HTSP message off the wire.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed message, or <c>null</c> when it could not be parsed.</returns>
    private async Task<HTSMessage?> ReadMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        long length = HTSMessage.UIntToLong(header[0], header[1], header[2], header[3]);
        if (length < 0 || length > MaxMessageBytes)
        {
            _logger.LogWarning("[TVHclient] ConnectionTester: implausible HTSP message length {Length}", length);
            return null;
        }

        byte[] frame = new byte[length + 4];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(4, (int)length), cancellationToken).ConfigureAwait(false);

        return HTSMessage.Parse(frame, _messageLogger);
    }

    /// <summary>
    /// Checks the HTTP endpoint the plugin streams from, and how many channels it offers.
    /// </summary>
    /// <remarks>
    /// The channel playlist is requested rather than one of the <c>/api</c> endpoints: it needs
    /// only the streaming rights the plugin itself needs, so a non-admin TVHeadend user does not
    /// fail the check, and its entries are exactly the channels that user is allowed to watch. The
    /// web root discovered by the HTSP check is used, because that is the one the plugin builds
    /// its URLs from.
    /// </remarks>
    /// <param name="settings">The settings to test.</param>
    /// <param name="result">The result to fill in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the check has finished.</returns>
    private async Task TestHttpAsync(TvhConnectionSettings settings, ConnectionTestResult result, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HttpTimeout);

        string baseUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"http://{settings.Host.Trim()}:{settings.HttpPort}{result.WebRoot}");

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

            using var response = await GetAuthenticatedAsync(
                httpClient,
                baseUrl + "/playlist/channels",
                settings,
                result,
                timeout.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                result.HttpError = "TVHeadend rejected the credentials on the HTTP port, or this user may not stream";
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.HttpError = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{baseUrl} answered with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return;
            }

            result.HttpSuccess = true;

            string playlist = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            result.ChannelCount = CountPlaylistEntries(playlist);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.HttpError = string.Create(
                CultureInfo.InvariantCulture,
                $"No answer from {baseUrl} within {HttpTimeout.TotalSeconds:0} seconds");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogDebug(ex, "[TVHclient] ConnectionTester: HTTP check failed");
            result.HttpError = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TVHclient] ConnectionTester: unexpected error during the HTTP check");
            result.HttpError = ex.Message;
        }
    }

    /// <summary>
    /// Counts the entries of an M3U playlist.
    /// </summary>
    /// <param name="playlist">The playlist body.</param>
    /// <returns>The number of entries.</returns>
    private static int CountPlaylistEntries(string playlist)
    {
        int count = 0;

        foreach (var line in playlist.AsSpan().EnumerateLines())
        {
            if (line.TrimStart().StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Performs a GET request, answering whichever authentication challenge TVHeadend sends.
    /// </summary>
    /// <remarks>
    /// The scheme is not known up front: TVHeadend can be configured for basic ('plain') or
    /// digest authentication, or for none at all. The request therefore goes out unauthenticated
    /// first and is repeated with credentials if the server asks for them, the way any HTTP client
    /// does. The scheme that was offered is recorded in the result, because it tells an admin
    /// which TVHeadend authentication type is in effect.
    /// </remarks>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="url">The URL to request.</param>
    /// <param name="settings">The settings being tested.</param>
    /// <param name="result">The result to record the offered scheme in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response to the authenticated request, or to the first one when no
    /// authentication was requested.</returns>
    private async Task<HttpResponseMessage> GetAuthenticatedAsync(
        HttpClient httpClient,
        string url,
        TvhConnectionSettings settings,
        ConnectionTestResult result,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        result.HttpAuthScheme = string.Join(", ", response.Headers.WwwAuthenticate.Select(header => header.Scheme));

        AuthenticationHeaderValue? challenge = response.Headers.WwwAuthenticate
            .FirstOrDefault(header => header.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase))
            ?? response.Headers.WwwAuthenticate.FirstOrDefault();

        string username = settings.Username.Trim();
        string password = settings.Password.Trim();
        string? authorization = null;

        if (challenge is not null && challenge.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase))
        {
            string? digest = HttpDigestHelper.BuildAuthorization(
                challenge,
                HttpMethod.Get.Method,
                new Uri(url),
                username,
                password);

            if (digest is not null)
            {
                authorization = "Digest " + digest;
            }
        }
        else if (challenge is not null && challenge.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        }

        if (authorization is null)
        {
            _logger.LogWarning(
                "[TVHclient] ConnectionTester: cannot answer the '{Scheme}' authentication challenge",
                result.HttpAuthScheme);
            return response;
        }

        response.Dispose();

        using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, url);
        authenticatedRequest.Headers.TryAddWithoutValidation("Authorization", authorization);

        return await httpClient.SendAsync(authenticatedRequest, cancellationToken).ConfigureAwait(false);
    }
}
