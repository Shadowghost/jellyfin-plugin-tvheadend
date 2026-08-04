namespace TVHeadEnd.Api;

/// <summary>
/// The outcome of a TVHeadend connection test.
/// </summary>
/// <remarks>
/// The HTSP and HTTP endpoints are reported separately because the plugin needs both and they
/// fail independently: HTSP carries channels, EPG and recordings, HTTP carries the streams.
/// </remarks>
public class ConnectionTestResult
{
    /// <summary>
    /// Gets a value indicating whether both the HTSP and the HTTP endpoint answered successfully.
    /// </summary>
    public bool Success => HtspSuccess && HttpSuccess;

    /// <summary>
    /// Gets or sets a value indicating whether the HTSP handshake and authentication succeeded.
    /// </summary>
    public bool HtspSuccess { get; set; }

    /// <summary>
    /// Gets or sets the reason the HTSP check failed, if it did.
    /// </summary>
    public string? HtspError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the HTTP endpoint answered and accepted the credentials.
    /// </summary>
    public bool HttpSuccess { get; set; }

    /// <summary>
    /// Gets or sets the reason the HTTP check failed, if it did.
    /// </summary>
    public string? HttpError { get; set; }

    /// <summary>
    /// Gets or sets the authentication scheme the HTTP endpoint asked for.
    /// </summary>
    public string? HttpAuthScheme { get; set; }

    /// <summary>
    /// Gets or sets the server name TVHeadend reported during the handshake.
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// Gets or sets the TVHeadend version reported during the handshake.
    /// </summary>
    public string? ServerVersion { get; set; }

    /// <summary>
    /// Gets or sets the highest HTSP version the server supports.
    /// </summary>
    public int? ServerHtspVersion { get; set; }

    /// <summary>
    /// Gets or sets the HTSP version this plugin would use with that server.
    /// </summary>
    public int? NegotiatedHtspVersion { get; set; }

    /// <summary>
    /// Gets or sets the web root the server reported, or an empty string when it serves from the root.
    /// </summary>
    public string? WebRoot { get; set; }

    /// <summary>
    /// Gets or sets the number of channels visible to the configured user.
    /// </summary>
    public int? ChannelCount { get; set; }
}
