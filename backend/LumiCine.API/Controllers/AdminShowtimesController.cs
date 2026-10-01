using LumiCine.Application.Accounts;
using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
[Route("api/admin/showtimes")]
public sealed partial class AdminShowtimesController(IShowtimeService showtimes) : ControllerBase { }
