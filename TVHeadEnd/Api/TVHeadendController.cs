using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TVHeadEnd.Api;

/// <summary>
/// Admin-only HTTP API backing the TVHeadend plugin settings page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
[Route("TVHeadend")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class TVHeadendController : ControllerBase
{
    private readonly ConnectionTester _connectionTester;

    /// <summary>
    /// Initializes a new instance of the <see cref="TVHeadendController"/> class.
    /// </summary>
    /// <param name="connectionTester">The connection tester.</param>
    public TVHeadendController(ConnectionTester connectionTester)
    {
        _connectionTester = connectionTester;
    }

    /// <summary>
    /// Tests whether the configured TVHeadend server is reachable.
    /// </summary>
    /// <remarks>
    /// The connection settings are read from the saved plugin configuration, so the settings page
    /// only asks for the check and never sends the credentials back to the server. Settings have
    /// to be saved before they can be tested. A failed check is reported in the result, not as an
    /// error status.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The test ran; see the result for its outcome.</response>
    /// <returns>The outcome of the connection test.</returns>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection(CancellationToken cancellationToken)
    {
        return await _connectionTester.TestConfiguredServerAsync(cancellationToken).ConfigureAwait(false);
    }
}
