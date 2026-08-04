using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TVHeadEnd.Helper
{
    /// <summary>
    /// Answers an HTTP digest access authentication challenge.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Net.Http.HttpClientHandler"/> cannot be used for this: TVHeadend sends
    /// its challenge with an unquoted <c>qop=auth</c>, which the handler refuses to parse, so it
    /// never retries the request. Digest is TVHeadend's default HTTP authentication type, so the
    /// exchange is done here instead.
    /// </remarks>
    public static partial class HttpDigestHelper
    {
        /// <summary>
        /// Builds the Authorization header value answering a digest challenge.
        /// </summary>
        /// <param name="challenge">The WWW-Authenticate header sent by the server.</param>
        /// <param name="method">The HTTP method of the request being authenticated.</param>
        /// <param name="requestUri">The URI of the request being authenticated.</param>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <returns>The header value, or <c>null</c> when the challenge cannot be answered.</returns>
        [SuppressMessage(
            "Security",
            "CA5351:Do Not Use Broken Cryptographic Algorithms",
            Justification = "HTTP digest access authentication as implemented by TVHeadend specifies an MD5 digest. The algorithm is dictated by the server and cannot be changed client-side.")]
        public static string? BuildAuthorization(
            AuthenticationHeaderValue challenge,
            string method,
            Uri requestUri,
            string username,
            string password)
        {
            ArgumentNullException.ThrowIfNull(challenge);
            ArgumentNullException.ThrowIfNull(requestUri);

            if (challenge.Parameter is null)
            {
                return null;
            }

            string? realm = null;
            string? nonce = null;
            string? qop = null;
            string? opaque = null;

            foreach (Match match in ChallengeParameterRegex().Matches(challenge.Parameter))
            {
                string value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["bare"].Value;

                switch (match.Groups["name"].Value.ToLowerInvariant())
                {
                    case "realm":
                        realm = value;
                        break;
                    case "nonce":
                        nonce = value;
                        break;
                    case "qop":
                        qop = value;
                        break;
                    case "opaque":
                        opaque = value;
                        break;
                    default:
                        break;
                }
            }

            if (realm is null || nonce is null)
            {
                return null;
            }

            // Only the plain 'auth' quality of protection is answered; 'auth-int' would require
            // digesting the body, which TVHeadend never asks for.
            bool useQop = qop is not null && qop.Contains("auth", StringComparison.OrdinalIgnoreCase);

            string uri = requestUri.PathAndQuery;
            string ha1 = Md5Hex($"{username}:{realm}:{password}");
            string ha2 = Md5Hex($"{method}:{uri}");

            const string NonceCount = "00000001";
            string clientNonce = Guid.NewGuid().ToString("N");

            string response = useQop
                ? Md5Hex($"{ha1}:{nonce}:{NonceCount}:{clientNonce}:auth:{ha2}")
                : Md5Hex($"{ha1}:{nonce}:{ha2}");

            var header = new StringBuilder();
            header.Append(CultureInfo.InvariantCulture, $"username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", algorithm=MD5, response=\"{response}\"");

            if (useQop)
            {
                header.Append(CultureInfo.InvariantCulture, $", qop=auth, nc={NonceCount}, cnonce=\"{clientNonce}\"");
            }

            if (opaque is not null)
            {
                header.Append(CultureInfo.InvariantCulture, $", opaque=\"{opaque}\"");
            }

            return header.ToString();
        }

        /// <summary>
        /// Matches the comma separated parameters of an authentication challenge, whether or not
        /// their values are quoted.
        /// </summary>
        /// <returns>The parameter regex.</returns>
        [GeneratedRegex("(?<name>[A-Za-z0-9_-]+)\\s*=\\s*(?:\"(?<quoted>[^\"]*)\"|(?<bare>[^,\\s]+))")]
        private static partial Regex ChallengeParameterRegex();

        /// <summary>
        /// Hashes a string with MD5 and formats it as lower case hex, as digest requires.
        /// </summary>
        /// <param name="value">The value to hash.</param>
        /// <returns>The hex encoded hash.</returns>
        [SuppressMessage(
            "Security",
            "CA5351:Do Not Use Broken Cryptographic Algorithms",
            Justification = "HTTP digest access authentication as implemented by TVHeadend specifies an MD5 digest. The algorithm is dictated by the server and cannot be changed client-side.")]
        private static string Md5Hex(string value)
        {
            return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}
