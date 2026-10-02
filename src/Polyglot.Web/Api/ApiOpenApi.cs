using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// The OpenAPI document of the delivery API, which consumers generate their clients from. Everything
/// here exists to make those clients usable: stable operation and schema names, the API key as a
/// security scheme, typed errors, and the conditional-request headers.
/// </summary>
public static class ApiOpenApi
{
    public const string DocumentName = "v1";

    public const string SecuritySchemeName = "ApiKey";

    /// <summary>Generators name the client class after it, e.g. <c>DeliveryApi</c>.</summary>
    public const string Tag = "Delivery";

    public static IServiceCollection AddPolyglotOpenApi(this IServiceCollection services)
        => services.AddOpenApi(DocumentName, o =>
        {
            // 3.0 rather than ASP.NET Core 10's 3.1 default: generator support for 3.1 is still uneven.
            o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;

            // The delivery API only; the editor's own endpoints are not a public contract.
            o.ShouldInclude = d => d.RelativePath?.StartsWith(ApiExtensions.BasePath.TrimStart('/') + "/") == true;

            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Polyglot delivery API",
                    Version = DocumentName,
                    Description = "Read-only access to the translations of a Polyglot instance.",
                };

                // The committed copy must not depend on the host it was generated on; a client is
                // pointed at its own instance anyway.
                document.Servers = [];

                document.Tags = new HashSet<OpenApiTag>
                {
                    new() { Name = Tag, Description = "Cultures and translations for consuming apps." },
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyAuthenticationHandler.HeaderName,
                    Description = "An API key issued on the instance's /admin/api-keys page.",
                };

                return Task.CompletedTask;
            });

            // ASP.NET Core's web JSON defaults read numbers from strings too, so an int becomes
            // "integer or numeric string" — a union generators turn into an untyped field. Only
            // ProblemDetails.status is affected, and Polyglot only ever writes it as a number.
            o.AddSchemaTransformer((schema, context, _) =>
            {
                var type = context.JsonTypeInfo.Type;
                if (type == typeof(int) || type == typeof(int?))
                {
                    schema.AnyOf = null;
                    schema.Pattern = null;
                    schema.Type = type == typeof(int?) ? JsonSchemaType.Integer | JsonSchemaType.Null : JsonSchemaType.Integer;
                    schema.Format = "int32";
                }

                return Task.CompletedTask;
            });

            o.AddOperationTransformer((operation, context, _) =>
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SecuritySchemeName, context.Document)] = [],
                    },
                ];

                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "If-None-Match",
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = "The ETag of a response already held; a match is answered with an empty 304.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });

                if (operation.Responses?.TryGetValue("200", out var ok) == true && ok is OpenApiResponse okResponse)
                {
                    okResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
                    okResponse.Headers["ETag"] = new OpenApiHeader
                    {
                        Description = "The version of this response, to send back as If-None-Match.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    };
                }

                if (operation.Responses?.TryGetValue("304", out var notModified) == true
                    && notModified is OpenApiResponse notModifiedResponse)
                    notModifiedResponse.Description = "The response has not changed since the ETag in If-None-Match.";

                return Task.CompletedTask;
            });
        });
}
