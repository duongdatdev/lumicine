using Microsoft.AspNetCore.Mvc;

namespace LumiCine.IntegrationTests;

[ApiController]
[Route("__test/validation-probe")]
public sealed class ValidationProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post(ValidationProbeRequest request) =>
        Ok(new { name = request.Name, quantity = request.Quantity });
}