using System.ComponentModel.DataAnnotations;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.VisularrDtos;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// The hub (linkarr) that manages visularr's users. Written by admins or an api key (Jellyfin gives api keys the admin role).
/// </summary>
[Route("Visularr/Managed")]
[Authorize]
public class VisularrManagedController : BaseJellyfinApiController
{
    private readonly ManagedHub _hub;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisularrManagedController"/> class.
    /// </summary>
    /// <param name="hub">Instance of the <see cref="ManagedHub"/>.</param>
    public VisularrManagedController(ManagedHub hub)
    {
        _hub = hub;
    }

    /// <summary>
    /// Gets the managing hub.
    /// </summary>
    /// <response code="200">Hub returned, empty when the users are managed here.</response>
    /// <returns>The <see cref="ManagedDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ManagedDto> Get() => _hub.Get();

    /// <summary>
    /// Stores the managing hub.
    /// </summary>
    /// <param name="body">The hub's name and url.</param>
    /// <response code="204">Hub stored.</response>
    /// <response code="400">The name is blank or the url is not an absolute http(s) url.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpPost]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Set([FromBody, Required] ManagedDto body)
        => _hub.TrySave(body.Name, body.Url) ? NoContent() : BadRequest("name must be 1 to 100 characters and url an absolute http(s) url");

    /// <summary>
    /// Forgets the managing hub.
    /// </summary>
    /// <response code="204">Hub forgotten.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpDelete]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Clear()
    {
        _hub.Clear();
        return NoContent();
    }
}
