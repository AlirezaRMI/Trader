using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Api.Securities;

public class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemes = await schemeProvider.GetAllSchemesAsync();
        if (schemes.Any(s => s.Name == "Bearer"))
        {
            var bearerScheme = new OpenApiSecurityScheme
            {
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes["Bearer"] = bearerScheme;
            
            
            var requirement = new OpenApiSecurityRequirement
            {
                [ new OpenApiSecurityScheme { Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }
                ] = Array.Empty<string>()
            };
            foreach (var path in document.Paths.Values)
            foreach (var op   in path.Operations.Values)
                op.Security.Add(requirement);
        }
    }
}
