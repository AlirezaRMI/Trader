using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controller;
[AllowAnonymous]
[ApiController]
public class ApiBaseController : ControllerBase
{
    
}