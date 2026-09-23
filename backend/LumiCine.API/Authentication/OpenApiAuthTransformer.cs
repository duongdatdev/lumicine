using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LumiCine.API.Authentication;

public sealed class OpenApiAuthTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var components = document.Components ?? new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token"
        };
        document.Components = components;
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var action = context.Description.ActionDescriptor;
        var metadata = action.EndpointMetadata;
        if (!metadata.OfType<IAllowAnonymous>().Any() && metadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });
        }

        var hasCsrfFilter = action is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor controllerAction &&
            controllerAction.FilterDescriptors.Any(descriptor =>
                descriptor.Filter is ServiceFilterAttribute { ServiceType: var serviceType } &&
                serviceType == typeof(CsrfValidationFilter));
        if (hasCsrfFilter)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-CSRF-TOKEN",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Request token obtained from GET /api/auth/csrf",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        return Task.CompletedTask;
    }
}
