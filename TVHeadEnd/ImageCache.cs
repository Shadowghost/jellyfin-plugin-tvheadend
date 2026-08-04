using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;
using TVHeadEnd.Helper;

namespace TVHeadEnd;

/// <summary>
/// Fetches images from TVHeadend and keeps them on disk for Jellyfin to pick up.
/// </summary>
/// <remarks>
/// Jellyfin downloads channel and programme images itself, with an <see cref="HttpClient"/> that
/// drops the userinfo of a <c>http://user:pass@host/...</c> URL without ever sending it. Every
/// image on an authenticated TVHeadend therefore came back as 401 and surfaced as "Unable to
/// convert any images to local". They are fetched here instead, where the basic or digest challenge
/// can be answered, and handed to Jellyfin as a local path, which it prefers over the URL.
/// </remarks>
public class ImageCache
{
    /// <summary>
    /// How long a cached file is used before it is fetched again.
    /// </summary>
    /// <remarks>
    /// TVHeadend hands out a new imagecache id when an image changes, so a stale file is normally
    /// replaced by a different URL rather than by a refetch. This is only a backstop for picons
    /// that are updated in place.
    /// </remarks>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly ILogger<ImageCache> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCache"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    public ImageCache(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);

        _logger = loggerFactory.CreateLogger<ImageCache>();
        _httpClientFactory = httpClientFactory;
        _directory = Path.Combine(applicationPaths.CachePath, "tvheadend-images");
    }

    /// <summary>
    /// Returns a local file holding the given TVHeadend image.
    /// </summary>
    /// <remarks>
    /// Only images served by the configured TVHeadend are cached. An EPG provider's own image URL
    /// needs no credentials, so Jellyfin is left to fetch it directly.
    /// </remarks>
    /// <param name="imageUrl">The absolute image URL, as built by the connection handler.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The path of the cached file, or <c>null</c> when the image is not ours to fetch or
    /// could not be fetched.</returns>
    public async Task<string?> GetLocalPathAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        var configuration = Plugin.Instance.Configuration;

        if (!uri.Host.Equals(configuration.TVH_ServerName.Trim(), StringComparison.OrdinalIgnoreCase)
            || uri.Port != configuration.HTTP_Port)
        {
            return null;
        }

        string key = CacheKey(imageUrl);
        string? cached = FindCached(key);
        if (cached is not null && File.GetLastWriteTimeUtc(cached) > DateTime.UtcNow - MaxAge)
        {
            return cached;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

            var (response, _) = await HttpAuthHelper.GetAsync(
                httpClient,
                imageUrl,
                configuration.Username.Trim(),
                configuration.Password.Trim(),
                _logger,
                cancellationToken).ConfigureAwait(false);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "[TVHclient] ImageCache: TVHeadend answered HTTP {StatusCode} for '{Path}'",
                        (int)response.StatusCode,
                        uri.PathAndQuery);

                    // A stale file still beats no image at all.
                    return cached;
                }

                Directory.CreateDirectory(_directory);

                string target = Path.Combine(_directory, key + Extension(response, uri));
                string temporary = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{target}.{Guid.NewGuid():N}.tmp");

                var file = File.Create(temporary);
                await using (file.ConfigureAwait(false))
                {
                    await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                // Renamed into place so a reader never sees a half written image.
                File.Move(temporary, target, true);

                return target;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "[TVHclient] ImageCache: could not fetch '{Path}'", uri.PathAndQuery);
            return cached;
        }
    }

    /// <summary>
    /// Derives the cache file name of an image URL.
    /// </summary>
    /// <param name="imageUrl">The image URL.</param>
    /// <returns>The file name without an extension.</returns>
    private static string CacheKey(string imageUrl)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)))[..32];
    }

    /// <summary>
    /// Picks the file extension to store an image under.
    /// </summary>
    /// <remarks>
    /// Jellyfin decides how to handle an image file by its extension, and TVHeadend serves whatever
    /// the provider delivered, so the content type is asked first and the URL is only a fallback.
    /// </remarks>
    /// <param name="response">The response carrying the image.</param>
    /// <param name="uri">The image URL.</param>
    /// <returns>The extension, including the leading dot.</returns>
    private static string Extension(HttpResponseMessage response, Uri uri)
    {
        string? fromContentType = MimeTypes.ToExtension(response.Content.Headers.ContentType?.MediaType ?? string.Empty);
        if (!string.IsNullOrEmpty(fromContentType))
        {
            return fromContentType.StartsWith('.') ? fromContentType : "." + fromContentType;
        }

        string fromUrl = Path.GetExtension(uri.AbsolutePath);

        return string.IsNullOrEmpty(fromUrl) ? ".png" : fromUrl;
    }

    /// <summary>
    /// Looks for an already cached file, whatever extension it was stored under.
    /// </summary>
    /// <param name="key">The cache key of the image.</param>
    /// <returns>The path of the cached file, or <c>null</c> when there is none.</returns>
    private string? FindCached(string key)
    {
        if (!Directory.Exists(_directory))
        {
            return null;
        }

        foreach (string path in Directory.EnumerateFiles(_directory, key + ".*"))
        {
            if (!path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }
}
