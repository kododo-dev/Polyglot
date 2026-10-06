# Delivery API contract

Design for roadmap item 2: API keys, a read-only delivery API for consuming apps, and an OpenAPI
document to generate clients from. The contract was written before the implementation and is kept in
step with it. Everything described here is implemented.

## Goals

- A consuming app fetches translations over HTTP, authenticated with an API key, and refreshes them
  periodically without restarting.
- Polling is cheap. An unchanged snapshot costs one conditional request and an empty `304`.
- Administrators create and revoke keys in the UI. Keys are stored so that a database leak does not
  reveal usable keys.
- The payload is simple enough to use from any stack (curl, Node, Python), not only from .NET.
- The contract is published as an OpenAPI document, so consumers can generate a client for their own
  stack.

## Non-goals (for this iteration)

- A .NET client package. The OpenAPI document covers the same need for now. A package that wires
  Polyglot into `IStringLocalizer` is a separate step, after the API has settled.
- Writing translations through the API. The editor UI is the only writer.
- Incremental deltas ("give me what changed since X"). `IStore` exposes no timestamps or row
  versions, so deltas would need change tracking in Polyglot's own schema. Deferred to the roadmap
  item that adds history.
- CORS. Keys belong on a server, not in a browser. A `Polyglot__Api__AllowedOrigins` setting can be
  added if a browser use case comes up.

## API keys

### Token format

```
pg_<id>_<secret>

id      8 characters, lower-case letters and digits, public
secret  43 characters, letters and digits, about 256 bits
```

Example: `pg_7fk2ab91_Zx8QvR...`. The full token is shown once, when the key is created.

- `id` is public. It is indexed, logged and shown in the UI, so a key can be recognised and revoked
  without storing the secret. It is lower case only, because identifiers get read off screens and out
  of logs, where case is easily lost.
- The secret uses letters and digits rather than base64url. Base64url contains `_`, which is also the
  separator in `pg_<id>_<secret>`, so any code that splits a token on `_` would break on about half
  the keys. The first version of our own parser did exactly that. 43 characters from an alphabet of
  62 give the same entropy as 32 random bytes. Tokens are parsed by position, so the separator can
  never be confused with token content.
- The secret is random with high entropy, so the stored verifier is a plain SHA-256 of it. A password
  KDF adds nothing here (hosted services treat their tokens the same way). The hash is compared in
  constant time.
- The `pg_` prefix makes keys easy to grep in logs and recognisable to secret scanners.

### Storage

Table `app.api_keys`, following the conventions of `app.users`:

| Column | Notes |
|---|---|
| `Id` (Guid) | primary key |
| `KeyId` (string, 8) | unique index; the public part of the token |
| `SecretHash` (byte[32]) | SHA-256 of the secret part |
| `Name` (string, 200) | what the key is for, e.g. `shop-frontend-prod` |
| `Scopes` (string, 200) | comma-separated; `translations:read` is the only value in v1 |
| `CreatedAt`, `CreatedByUserId` | audit |
| `ExpiresAt` (nullable) | optional expiry |
| `LastUsedAt` (nullable) | best effort, written at most once per minute per key |
| `IsDisabled` (bool) | revoke without losing the audit trail |

A key is valid when it exists, is not disabled and has not expired. `LastUsedAt` is imprecise on
purpose. Writing it on every request would turn every poll into a database write.

### Management

- `/admin/api-keys` (Admin role) lists the keys (name, `pg_<id>_...`, scopes, created, last used,
  expiry, status) and lets an administrator create, disable and delete them. After creation the page
  shows the full token once, with a copy button and a note that it cannot be shown again.
- The delivery API requires `Polyglot__Auth__Enabled=true`. With authentication off, no Razor pages
  are mapped, so there is nowhere to create or revoke a key, and the instance may have no database to
  keep keys in. On such an instance the `/api/v1/*` routes are not registered, and a startup warning
  says why.
- Keys cannot be seeded from configuration. That can be added later without breaking anything. It
  would mean a second way to verify keys, with no audit trail and no way to revoke a key without a
  restart.

### Authentication

Header: `X-Api-Key: pg_<id>_<secret>`.

The header is handled by an ASP.NET authentication scheme named `ApiKey`. The delivery endpoints
require that scheme explicitly. The sign-in cookie and OIDC never give access to the API, and an API
key never gives access to the editor or the admin pages.

## Endpoints

Mounted under `/api/v1`, respecting `Polyglot__PathBase`. Responses are `application/json`
(camelCase) and errors are `application/problem+json`.

### `GET /api/v1/cultures`

```json
{
  "version": "3f9a1c2d5e7b8a04",
  "defaultCulture": "en",
  "cultures": ["de", "en", "pl"]
}
```

The list is sorted, so the payload and its version are deterministic. It is read from the live
`CultureWayOptions`, the same source the editor reads. That source already contains the configured
`Polyglot__Cultures`, cultures the store persisted in an earlier run, and cultures added while the
instance runs. `defaultCulture` is resolved the same way the editor resolves it.

Known limit: a culture added on one replica reaches the other replicas only after a restart, because
CultureWay keeps the list per process. The editor behaves the same way today.

### `GET /api/v1/translations/{culture}`

Returns the translations an app needs to render one culture, optionally limited to some namespaces:
`/api/v1/translations/pl?namespace=Checkout&namespace=Common`.

```json
{
  "Checkout.Pay": "Zapłać",
  "Checkout.Summary.Total": "Razem",
  "Common.Cancel": "Anuluj"
}
```

The body contains only the translations, so an app can pass it directly to its i18n library. The
version is in the `ETag` header (see [Caching](#caching-and-change-detection)). The culture and
namespaces are the ones the caller asked for, so the response does not repeat them.

**Culture.** Required. Polyglot applies the fallback, not the caller. Every key is resolved along the
chain CultureWay's own `IStringLocalizer` uses: the culture, then its parents, then the default
culture (for example `pl-PL`, then `pl`, then `en`). The response contains every key that has a value
anywhere in that chain, without saying which culture the value came from. The app gets what the
localizer would have given it and does not need its own fallback logic. A key is missing only when no
culture in the chain has it. The app then shows the key itself, as the localizer does.

- `404` when neither the culture nor any of its parents is supported. Falling back to the default
  culture here would turn a typo such as `xx` into pages silently shown in English.
- A supported culture with no translations of its own returns `200` with whatever the chain provides.

**Namespaces.** CultureWay has no namespace entity. A key's namespace is everything before its last
dot, and the editor builds its namespace tree from those prefixes. The `namespace` parameter works
like the editor's filter: `namespace=Checkout` selects keys whose namespace is `Checkout` or is below
it (`Checkout.Pay`, `Checkout.Summary.Total`).

- Optional and repeatable. The response contains the union. Without the parameter, the whole culture
  is returned. A page usually needs its own namespace plus a shared one, and one request is simpler
  than two.
- A namespace with no keys returns `200` and an empty object, not `404`. A namespace exists only as
  long as it has keys, so deleting its last key must not break an app that asks for it.
- `400` for a malformed namespace: empty, or with an empty segment (`.Checkout`, `A..B`).
- Keys without a dot (shown as "(root)" in the editor) are returned only when there is no filter.
  Selecting only those keys can be added later without breaking anything.

**Payload.** The map uses full keys. They are not stripped of their namespace and not nested.
Stripping would cause collisions when several namespaces are requested, and apps look keys up by
their full name anyway. Nesting cannot represent CultureWay's keys, because one key can be the
namespace of another. `Checkout.Pay` and `Checkout.Pay.Tooltip` can both exist, and `Pay` cannot be a
string and an object at the same time. CultureWay allows this, and the editor even suggests it by
prefilling a new key with the selected namespace. Libraries that expect nested JSON, such as i18next,
read the flat map with `keySeparator: false`.

`null` values never appear. Values are returned exactly as stored. The API does not impose or convert
any placeholder syntax. Entries are sorted by key, so the payload and its version are deterministic.

Since the response is a bare map, new fields cannot be added to it. This is the one exception to
`/api/v1` being additive (see [Versioning](#versioning)). Anything else this response needs to carry
goes into a header, or into `/api/v2`.

### Composition

A snapshot merges `IStore.GetAllAsync()` with the defaults of any registered `IReadOnlySource`, with
store values taking precedence, the same way the editor reads translations. Polyglot registers no
read-only source today, but the delivery API must agree with the editor if one is added.

## Caching and change detection

- The version is the first 16 hex characters of a SHA-256 over the response content (sorted
  entries). `GET /api/v1/cultures` also includes it in the body as `version`. The translations
  response has it only in the header.
- It is sent as a strong ETag: `ETag: "a71b0ce4425d9f18"`.
- `If-None-Match` with a matching value returns `304` with no body.
- `Cache-Control: no-cache`. Clients always revalidate, and a shared proxy never serves a stale
  snapshot. The cheap path is the `304`.
- Each response has its own version, computed after the fallback chain and the namespace filter are
  applied. A change in `de` does not change the version a client polling `pl` sees, and a change in
  `Admin` does not affect a client polling `Checkout`. A change in the default culture does change the
  version of every culture that falls back to it, because their responses change too.

Reading every translation on every poll would be wasteful, so Polyglot keeps one snapshot in process
(all cultures, unfiltered). Each response is resolved and filtered from it in memory, and the result
is kept per culture and namespace set for as long as the snapshot lives. The snapshot lives for
`Polyglot__Api__SnapshotCacheSeconds` (default `10`).

The snapshot expires after a fixed time and is not invalidated on writes. With several replicas, a
write handled by one replica cannot invalidate the snapshots of the others. As a result, an edit
reaches consuming apps within `SnapshotCacheSeconds` plus the client's own poll interval.

## Errors

| Status | When | `type` |
|---|---|---|
| `400` | malformed culture code or namespace | `urn:polyglot:error:invalid-request` |
| `401` | missing, malformed, unknown, expired or disabled key | `urn:polyglot:error:unauthorized` |
| `403` | valid key without the required scope | `urn:polyglot:error:forbidden` |
| `404` | neither the culture nor any of its parents is supported | `urn:polyglot:error:not-found` |
| `429` | per-key rate limit exceeded; includes `Retry-After` | `urn:polyglot:error:rate-limited` |

The types are URNs, not URLs. Clients compare them, and a documentation link would eventually break.

`401` does not say whether a key is unknown or disabled. Administrators see that difference in the
logs. The caller does not need it.

Rate limiting uses a fixed window per key, `Polyglot__Api__RequestsPerMinute` (default `120`). That is
enough for polling and still limits a misconfigured client. The window is partitioned by the key
identifier read directly from the header, not by the authenticated principal, and the limiter runs
before authentication. This way a flood of invalid keys is rejected without a database lookup for
each request. Requests without a parsable key are counted per client address, as sign-in attempts
are.

## Configuration

| Variable | Default | Description |
|---|---|---|
| `Polyglot__Api__Enabled` | `true` | Delivery API. Requires `Polyglot__Auth__Enabled=true`, which in turn requires a database. |
| `Polyglot__Api__RequestsPerMinute` | `120` | Requests allowed per key per minute. |
| `Polyglot__Api__SnapshotCacheSeconds` | `10` | How long a snapshot is reused in process. `0` reads the store on every request. |
| `Polyglot__Api__OpenApi__Enabled` | `true` | Serves the OpenAPI document at `/openapi/v1.json`, without a key. |

## Versioning

`/api/v1` only grows. New fields and endpoints may appear, and existing fields keep their meaning.
The translations response is a bare map with no room for new fields, so it can only gain headers. A
breaking change means `/api/v2`, served next to v1, with its own document at `/openapi/v2.json`.
Field names and `operationId`s are part of the contract. Renaming one breaks every generated client,
so it counts as a breaking change.

## OpenAPI document

Polyglot does not ship a client. It publishes its contract, and consumers generate a client from it.

- Built with `Microsoft.AspNetCore.OpenApi` (part of ASP.NET Core 10, no Swashbuckle) and served at
  `/openapi/v1.json`, with the same document as YAML at `/openapi/v1.yaml`.
- The document uses **OpenAPI 3.0**, not the 3.1 default of ASP.NET Core 10. Generator support for
  3.1 is still uneven, and the document exists to generate clients. This can be revisited once the
  common generators support 3.1 well.
- Served without an API key, because the schema is not a secret. Pointing
  `openapi-generator -i https://polyglot.example.com/openapi/v1.json` at an instance is enough.
  `Polyglot__Api__OpenApi__Enabled=false` turns the document off.

To produce usable clients, the document has:

- An explicit `operationId` on every endpoint (`getCultures`, `getTranslations`). Generators derive
  method names from it, so an accidental rename breaks every generated client.
- A named response schema backed by a DTO record for `getCultures` (`CulturesResponse`), so the
  generated model has a stable name. `getTranslations` returns an object with
  `additionalProperties: string`, which generators turn into a plain map type such as
  `Dictionary<string, string>`, `map[string]string` or `Record<string, string>`.
- `namespace` as an array query parameter (`style: form`, `explode: true`), so a generated client
  takes a list of namespaces.
- A `securityScheme` of type `apiKey` in the `X-Api-Key` header, applied to every operation, so a
  generated client takes the key as configuration.
- Error responses (`400`, `401`, `403`, `404`, `429`) as `application/problem+json` with a
  `ProblemDetails` schema, so generated clients can map failures to typed exceptions.
- `If-None-Match` as a header parameter, `ETag` on `200`, and `304` as a response. Several generators
  handle a `304` without a body poorly. A client that does not send `If-None-Match` still works; it
  only transfers more data.
- No `servers` entry. A client is pointed at its own instance, and the committed copy must not depend
  on the host it was generated on.
- The tag `Delivery` on every operation. Generators name the client class after it (`DeliveryApi`).

The document is committed as `docs/openapi/v1.json`, so every contract change shows up in review,
just as migrations are committed. A test fetches the document from the running app and fails when the
committed copy differs, so CI fails on a stale copy. Running the tests with
`POLYGLOT_UPDATE_OPENAPI=1` rewrites the file. Generating the document at build time with
`Microsoft.Extensions.ApiDescription.Server` was the original plan. It does not work here: it runs
`Program` without a database, authentication then refuses to start, and the delivery API is never
mapped.

Generators only need the document URL. For people, `/api-reference/` serves an interactive reference
of the same document with Scalar (`Scalar.AspNetCore`), chosen over Swagger UI because it can fill in
a key (the demo's), shows ready requests in several languages, and fits the rest of the UI. The
document has no `servers`, so the reference is given this instance's path base on every request;
without it, requests would miss a sub-path such as `/polyglot/demo`. Everything that would call out of
the instance is turned off: telemetry, the AI chat, MCP, fonts from a CDN, and the toolbar that shares
to Scalar's cloud. The reference's scripts ship in the package.

```bash
# generate a TypeScript client
npx @openapitools/openapi-generator-cli generate \
  -i https://polyglot.example.com/openapi/v1.json -g typescript-fetch -o ./polyglot-client

# or call the API directly
curl -s -H "X-Api-Key: pg_7fk2ab91_..." https://polyglot.example.com/api/v1/translations/pl
```

A .NET package that plugs Polyglot into `IStringLocalizer` (probably a read-only CultureWay `IStore`
plus a refresher) is still planned, but not in this iteration. It would also need a public way to
reload CultureWay's cache. `TranslationCache` is `internal` and loaded once at startup.

## Deliberate limits

These were considered and left out on purpose. They are listed here so the decisions are not
reopened by accident.

- **No endpoint that returns every culture at once.** An app renders a page in one culture at a time.
  An app that serves several languages makes one request per culture, and each response has its own
  version.
- **No `fallback` parameter.** How a missing translation is resolved is Polyglot's policy, the same
  chain the localizer applies. If callers could turn it off, two apps could show different text for
  the same page.
- **Namespaces in the query, not the path.** `/translations/{culture}/{namespaces}` looks nicer, but:
  - a path parameter cannot be optional in OpenAPI, so it would take two routes and two operations
    instead of one;
  - a list in a path is a small format every client has to build and escape, while a repeated query
    parameter becomes a plain array in a generated client;
  - CultureWay keys may contain `,` or `/`, and reverse proxies mangle or reject `%2F` in a path
    segment.
- **No key seeded from configuration.** The delivery API requires authentication, as described under
  [Management](#management). Adding `Polyglot__Api__BootstrapKey` later only adds something. Removing
  it once instances depend on it would break them.
- **No per-culture keys.** A key reads every translation in the instance. Filtering per key would
  replace the single cached snapshot with one per combination of cultures. More importantly, it would
  not provide the isolation it seems to: two unrelated owners both want `pl`, so separating them is not
  a question of cultures. If Polyglot ever has to serve unrelated owners, the answer is projects or
  namespaces bound to a key, and per-culture scoping would have been wasted work. The `Scopes` column
  stays, so `translations:write` can be added without a migration.
