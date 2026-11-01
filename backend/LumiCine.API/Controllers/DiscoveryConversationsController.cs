using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/discovery/conversations")]
[Authorize]
[EnableRateLimiting("DiscoveryPerUser")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DiscoveryConversationsController(IDiscoveryConversationService conversations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DiscoveryConversationDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var details = await conversations.CreateAsync(UserId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = details.Id }, details);
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<DiscoveryConversationSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<PagedResult<DiscoveryConversationSummary>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        conversations.ListAsync(UserId, new PageQuery(page, pageSize), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DiscoveryConversationDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<DiscoveryConversationDetails> Get(Guid id, CancellationToken cancellationToken) =>
        await conversations.GetAsync(UserId, id, cancellationToken)
            ?? throw new ApiException(404, "Conversation not found.");

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType<DiscoveryChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<DiscoveryChatResponse> SendMessage(Guid id, SendDiscoveryMessageRequest request,
        CancellationToken cancellationToken) =>
        conversations.SendMessageAsync(UserId, id, request, cancellationToken);

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
