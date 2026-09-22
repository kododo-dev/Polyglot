namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// The cultures this instance serves. A named record rather than an anonymous object, so that the
/// OpenAPI document carries a named schema and a generated client gets a named model.
/// </summary>
/// <param name="Version">Content version of this response, the same value as its ETag.</param>
/// <param name="DefaultCulture">The culture a consuming app should fall back to.</param>
/// <param name="Cultures">Every culture the instance serves, sorted, including the default one.</param>
public sealed record CulturesResponse(string Version, string DefaultCulture, string[] Cultures);
