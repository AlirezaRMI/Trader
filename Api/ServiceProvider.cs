using System.Security.Claims;
using Api.Securities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Api;

/// <summary>
/// Extension methods to register application services.
/// </summary>
public static class ServiceProvider
{
    /// <summary>
    /// Registers the DbContext with the correct connection string
    /// based on the hosting environment.
    /// </summary>
    public static IServiceCollection ApiServiceProvider(this IServiceCollection services, IConfiguration configuration,
        IWebHostEnvironment env)
    {
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value is { Errors.Count: > 0 })
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(err => err.ErrorMessage).ToArray()
                        );
                    var problemDetails = new ValidationProblemDetails(errors!)
                    {
                        Title = "Validation Error",
                        Status = StatusCodes.Status400BadRequest,
                    };
                    return new BadRequestObjectResult(problemDetails);
                };
            });

        services.AddHttpContextAccessor();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var secret = configuration["JwtSettings:Secret"]!;
                var key = new SymmetricSecurityKey(Convert.FromBase64String(secret));

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidAudience = configuration["JwtSettings:Audience"],
                    RoleClaimType = ClaimTypes.Role,
                };
            });
        services.AddOpenApi("docs", options => { options.AddDocumentTransformer<BearerSecuritySchemeTransformer>(); });
        services.AddSpaStaticFiles(options => options.RootPath = Path.Combine("EasyUi", "dist"));

        // config CORS
        services.AddCors(options =>
        {
            options.AddPolicy(name: "ShopApiCors", policy =>
            {
                policy.AllowAnyHeader();
                policy.AllowAnyMethod();
                policy.AllowAnyOrigin();
            });
        });

        services.AddHttpClient();

        return services;
    }
}