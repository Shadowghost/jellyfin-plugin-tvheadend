using System;
using TVHeadEnd.Configuration;

namespace TVHeadEnd;

/// <summary>
/// The settings needed to reach a TVHeadend server.
/// </summary>
/// <remarks>
/// A narrow view of <see cref="PluginConfiguration"/>, so the connection test does not have to
/// carry along the recording and channel settings it has no use for.
/// </remarks>
public class TvhConnectionSettings
{
    /// <summary>
    /// Gets or sets the TVHeadend hostname or IP address.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port TVHeadend serves HTTP on.
    /// </summary>
    public int HttpPort { get; set; } = 9981;

    /// <summary>
    /// Gets or sets the port TVHeadend serves HTSP on.
    /// </summary>
    public int HtspPort { get; set; } = 9982;

    /// <summary>
    /// Gets or sets the TVHeadend username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVHeadend password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Reads the connection settings out of a plugin configuration.
    /// </summary>
    /// <param name="configuration">The saved plugin configuration.</param>
    /// <returns>The connection settings.</returns>
    public static TvhConnectionSettings FromConfiguration(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new TvhConnectionSettings
        {
            Host = configuration.TVH_ServerName,
            HttpPort = configuration.HTTP_Port,
            HtspPort = configuration.HTSP_Port,
            Username = configuration.Username,
            Password = configuration.Password
        };
    }
}
