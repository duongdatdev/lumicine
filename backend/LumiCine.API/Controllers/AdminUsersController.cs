using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin)]
[Route("api/admin/users")]
public sealed class AdminUsersController(IAdminUserService adminUserService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminUserPage>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserPage>> GetUsers(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await adminUserService.GetUsersAsync(page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminUserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserSummary>> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await adminUserService.GetUserAsync(id, cancellationToken);
        return user is null ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found") : Ok(user);
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetAdminUserStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await adminUserService.SetActiveAsync(id, request.IsActive!.Value, cancellationToken);
        return result switch
        {
            AdminUserStatusResult.Success => NoContent(),
            AdminUserStatusResult.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found"),
            AdminUserStatusResult.ProtectedAdmin or AdminUserStatusResult.Conflict =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict"),
            _ => throw new InvalidOperationException("Unknown admin user status result.")
        };
    }

    public sealed record SetAdminUserStatusRequest
    {
        [Required]
        public bool? IsActive { get; init; }
    }
}
