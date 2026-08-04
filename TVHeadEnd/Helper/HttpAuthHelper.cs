using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TVHeadEnd.Helper
{
    /// <summary>
    /// Performs HTTP requests against TVHeadend, answering whichever authentication it asks for.
    /// </summary>
    /// <remarks>
    /// The scheme is not known up front: TVHeadend can be configured for basic ('plain') or digest
    /// authentication, or for none at all. The request therefore goes out unauthenticated first and
    /// is repeated with credentials when the server asks for them, the way any HTTP client does.
    /// </remarks>
    public static class HttpAuthHelper
    {
        /// <summary>
        /// Performs a GET request, answering a basic or digest challenge with the given credentials.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use.</param>
        /// <param name="url">The URL to request.</param>
        /// <param name="username">The TVHeadend username, empty for anonymous access.</param>
        /// <param name="password">The TVHeadend password, empty for anonymous access.</param>
        /// <param name="logger">The logger to report an unanswerable challenge to.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response, plus the scheme the server offered when it asked for one. The
        /// caller owns the response and has to dispose it.</returns>
        public static async Task<(HttpResponseMessage Response, string? OfferedScheme)> GetAsync(
            HttpClient httpClient,
            string url,
            string username,
            string password,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpClient);

            var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return (response, null);
            }

            string offered = string.Join(", ", response.Headers.WwwAuthenticate.Select(header => header.Scheme));

            AuthenticationHeaderValue? challenge = response.Headers.WwwAuthenticate
                .FirstOrDefault(header => header.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase))
                ?? response.Headers.WwwAuthenticate.FirstOrDefault();

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
                logger.LogWarning("[TVHclient] HttpAuthHelper: cannot answer the '{Scheme}' authentication challenge", offered);
                return (response, offered);
            }

            response.Dispose();

            using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, url);
            authenticatedRequest.Headers.TryAddWithoutValidation("Authorization", authorization);

            return (await httpClient.SendAsync(authenticatedRequest, cancellationToken).ConfigureAwait(false), offered);
        }
    }
}
