using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class ApiBaseController : ControllerBase
{
    
}