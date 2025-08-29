using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Securities;

public class SuperAdminAuthorizeAttribute : Attribute,IAsyncAuthorizationFilter
{

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return  Task.CompletedTask; 
        }
        
        var isSuperAdminClaim = context.HttpContext.User.Claims
            .FirstOrDefault(c => c.Type.Equals("isSuperAdmin", StringComparison.OrdinalIgnoreCase));
        
        if (isSuperAdminClaim == null || !bool.TryParse(isSuperAdminClaim.Value, out var isSuperAdmin) || !isSuperAdmin)
        {
            context.Result = new ForbidResult();
        }
        
        return Task.CompletedTask; 
    }
}