using Kododo.Polyglot.Web.Demo;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

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

    /// <summary>Where the interactive reference (Scalar) is served.</summary>
    public const string ReferencePath = "/api-reference";

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
            // "integer or numeric string", a union that generators turn into an untyped field. Only
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

    /// <summary>
    /// Serves an interactive reference of the document, where a key can be pasted in and requests
    /// sent from the browser. Public like the document itself: the requests still need a key.
    /// </summary>
    public static void MapPolyglotApiReference(this WebApplication app, DemoOptions demo)
        => app.MapScalarApiReference(ReferencePath, (options, http) =>
            {
                // The document has no servers on purpose (see AddPolyglotOpenApi), so the reference is
                // told where this instance is, path base included.
                var pathBase = http.Request.PathBase.Value ?? "";
                options
                    .WithTitle("Polyglot delivery API")
                    .AddDocument(DocumentName)
                    .AddServer(pathBase.Length > 0 ? pathBase : "/")
                    .WithFavicon($"{pathBase}/favicon.svg")
                    .AddPreferredSecuritySchemes(SecuritySchemeName)
                    // A self-hosted instance should not call out to anyone: no telemetry, AI chat,
                    // MCP, fonts from a CDN, or the toolbar that shares and deploys to Scalar's
                    // cloud. The reference's own scripts ship in the package.
                    .HideDeveloperTools()
                    .DisableTelemetry()
                    .DisableAgent()
                    .DisableMcp()
                    .DisableDefaultFonts();

                // The demo's key is public, so visitors can send requests right away.
                if (demo.Enabled)
                    options.AddApiKeyAuthentication(SecuritySchemeName, key => key.Value = demo.ApiKey);
            })
            .AllowAnonymous();
}
