using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/showtimes")]
public sealed partial class ShowtimesController(IShowtimeService showtimes) : ControllerBase { }
