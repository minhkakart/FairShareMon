using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FairShareMonApi.Swagger;

/// <summary>
/// Adds the Bearer padlock ONLY to operations that actually require authentication: everything is
/// guarded by the FallbackPolicy unless the action or its controller carries
/// <c>[AllowAnonymous]</c>. Replaces the former document-wide security requirement, which put the
/// padlock on anonymous endpoints too.
/// </summary>
public sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var allowAnonymous =
            context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true;
        if (allowAnonymous)
            return;

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            }
        ];
    }
}
