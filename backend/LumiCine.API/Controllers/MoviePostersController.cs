using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
[Route("api/admin/movies/{id:guid}/poster")]
public sealed class MoviePostersController(IPosterService posters) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [RejectOversizedPosterRequest(6 * 1024 * 1024)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<PosterUrlResponse>> Upload(Guid id, [FromForm] PosterUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (Request.Form.Files.Count != 1) throw new LumiCine.Application.Common.ApiException(400, "Upload exactly one poster file.");
        var file = request.File!;
        using var content = file.OpenReadStream();
        var url = await posters.UploadAsync(id, content, file.ContentType, file.Length, cancellationToken);
        return Ok(new PosterUrlResponse(url));
    }

    [HttpDelete]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await posters.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

    public sealed record PosterUploadRequest
    {
        [Required] public IFormFile? File { get; init; }
    }

    public sealed record PosterUrlResponse(string PosterUrl);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RejectOversizedPosterRequestAttribute(long maximumRequestBytes) : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength > maximumRequestBytes)
            throw new LumiCine.Application.Common.ApiException(StatusCodes.Status413PayloadTooLarge, "Poster requests must be 6 MiB or smaller.");

        if (request.HasFormContentType)
        {
            try
            {
                await request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new LumiCine.Application.Common.ApiException(StatusCodes.Status413PayloadTooLarge, "Poster requests must be 6 MiB or smaller.");
            }
            catch (InvalidDataException exception) when (exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
            {
                throw new LumiCine.Application.Common.ApiException(StatusCodes.Status413PayloadTooLarge, "Poster requests must be 6 MiB or smaller.");
            }
            catch (InvalidDataException)
            {
                throw new LumiCine.Application.Common.ApiException(StatusCodes.Status400BadRequest, "The poster form is invalid.");
            }
        }
        await next();
    }
}
