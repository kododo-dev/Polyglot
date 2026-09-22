# Delivery API — contract

Design for roadmap item 2: API keys, a read-only delivery API for consuming apps, and an OpenAPI
document they generate a client from. The contract was fixed before implementation and is kept in step
with it: the keys, the `ApiKey` scheme, rate limiting and `GET /api/v1/cultures` are in place; the
translations endpoints, the admin page and the OpenAPI document are not yet.

## Goals

- A consuming app fetches translations over HTTP, authenticated with an API key, and refreshes them
  periodically without restarting.
- Polling is cheap: an unchanged snapshot costs one conditional request and an empty `304` body.
- Keys are created and revoked by administrators in the UI, stored so that a database leak does not
  reveal usable keys.
- The payload is plain enough to consume from any stack (curl, Node, Python), not only from .NET.
- The contract is published as an OpenAPI document, so a consumer generates a client for their own
  stack instead of waiting for one to be written for them.

## Non-goals (for this iteration)

- A ready-made .NET client package. The OpenAPI document covers the same need for now; a package that
  wires Polyglot into `IStringLocalizer` is a separate step, deferred until the API has settled.
- Writing translations through the API (the editor UI remains the only writer).
- Incremental deltas ("give me what changed since X"). `IStore` exposes no timestamps or row
  versions, so a delta would require change tracking in Polyglot's own schema. Deferred to the
  roadmap item that brings history.
- CORS. Keys belong on a server, not in a browser. A future `Polyglot__Api__AllowedOrigins` can open
  this up if a browser-side use case appears.

## API keys

### Token format

```
pg_<id>_<secret>

id      8 characters, lower-case letters and digits, public
secret  43 characters, letters and digits, about 256 bits
```

Example: `pg_7fk2ab91_Zx8QvR...`, shown in full exactly once, at creation.

- `id` is a public identifier: it is indexed, logged and displayed in the UI, so a key can be
  recognised and revoked without the secret ever being stored. Lower case only, because an identifier
  gets read off a screen or out of a log, where case is easy to lose.
- The secret is letters and digits, not base64url, even though base64url would be the obvious encoding
  for 32 random bytes: its `_` collides with the separator in `pg_<id>_<secret>`, so anything that
  splits a token on `_` — including the first version of the parser here — breaks on about half the
  keys. 43 characters out of 62 carry the same entropy. Parsing is by position anyway, so the
  separator can never be mistaken for token content.
- The secret is high-entropy random, so the stored verifier is a plain SHA-256 of it — no password
  KDF needed, the same reasoning hosted services apply to their tokens. Compared in constant time.
- The `pg_` prefix makes keys greppable in logs and recognisable to secret scanners.

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

A key is valid when it exists, is not disabled and has not expired. `LastUsedAt` is deliberately
imprecise: writing it on every request would turn every poll into a database write.

### Management

- `/admin/api-keys` (Admin role): list (name, `pg_<id>_…`, scopes, created, last used, expiry,
  status), create, disable, delete. Creation shows the full token once, with a copy button and a note
  that it cannot be shown again.
- The delivery API therefore requires `Polyglot__Auth__Enabled=true`. With authentication off no Razor
  pages are mapped at all (`Program.cs:61-64`), so there is nowhere to mint or revoke a key, and the
  instance may be running without a database to keep keys in. On such an instance the `/api/v1/*`
  routes are not registered and a warning at startup says why. Seeding a key from configuration is
  deliberately left out — it can be added later without breaking anything, and it would cost a second
  verification path with no audit trail and no way to revoke a key short of a restart.

### Authentication

Header: `X-Api-Key: pg_<id>_<secret>`.

Implemented as an ASP.NET authentication scheme named `ApiKey`. The delivery endpoints require that
scheme explicitly, so the sign-in cookie and OIDC never grant API access, and an API key never
grants access to the editor or the admin pages.

## Endpoints

Mounted under `/api/v1`, respecting `Polyglot__PathBase`. All responses are `application/json`
(camelCase); errors are `application/problem+json`.

### `GET /api/v1/cultures`

```json
{
  "version": "3f9a1c2d5e7b8a04",
  "defaultCulture": "en",
  "cultures": ["de", "en", "pl"]
}
```

Sorted, so that the payload and its version are deterministic. Read from the live
`CultureWayOptions`, which is the source the editor itself reads: it already carries the configured
`Polyglot__Cultures`, the cultures the store persisted in an earlier run, and any added while the
instance runs, with `defaultCulture` resolved the same way the editor resolves it. One known limit
comes with it: a culture added on one replica reaches the others only after a restart, because
CultureWay keeps that list per process — the editor has the same behaviour today.

### `GET /api/v1/translations`

Every culture in one snapshot, for an app that serves several languages.

```json
{
  "version": "a71b0ce4425d9f18",
  "defaultCulture": "en",
  "cultures": {
    "en": { "Greeting": "Hello", "Validation.Required": "Required" },
    "pl": { "Greeting": "Czesc" }
  }
}
```

### `GET /api/v1/translations/{culture}`

One culture, for an app that only needs the active one.

```json
{
  "version": "c02f4419ab7d3e65",
  "culture": "pl",
  "translations": { "Greeting": "Czesc" }
}
```

- `404` when the culture is not among the supported ones. A supported culture with no translations
  returns `200` and an empty object — a real, if empty, answer.
- `?fallback=default` fills keys missing in `{culture}` with the default culture's values, for
  clients that do not implement fallback themselves. The default is `?fallback=none`.

Keys absent from a culture are absent from the object; `null` values never appear. Entries are sorted
by key, so the payload — and therefore the version — is deterministic.

### Composition

A snapshot merges `IStore.GetAllAsync()` with any registered `IReadOnlySource` defaults, store values
winning, exactly as the editor's read path does. Polyglot registers no read-only source today, but
the delivery API must not disagree with the editor if one is ever added.

## Caching and change detection

- `version` is SHA-256 over the resource's canonical content (sorted `culture`, `key`, `value`
  triples), first 16 hex characters.
- The same value is the strong ETag: `ETag: "a71b0ce4425d9f18"`.
- `If-None-Match` with a matching value returns `304` and no body.
- `Cache-Control: no-cache` — always revalidate, never let a shared proxy serve a stale snapshot. The
  cheap path is the `304`, not a freshness guess.
- Every resource carries its own version, so a change in `de` does not invalidate a client polling
  `pl`.

Building a snapshot per request would mean selecting every translation on every poll, so Polyglot
keeps one in process with a short TTL: `Polyglot__Api__SnapshotCacheSeconds` (default `10`). A TTL
rather than invalidation on write, because with several replicas a write handled by one replica
cannot invalidate the others, and the editor is the only writer. The visible effect is that an edit
reaches consuming apps within `SnapshotCacheSeconds` plus the client's own poll interval.

## Errors

| Status | When | `type` |
|---|---|---|
| `400` | malformed culture code, unknown `fallback` value | `urn:polyglot:error:invalid-request` |
| `401` | missing, malformed, unknown, expired or disabled key | `urn:polyglot:error:unauthorized` |
| `403` | valid key without the required scope | `urn:polyglot:error:forbidden` |
| `404` | unsupported culture | `urn:polyglot:error:not-found` |
| `429` | per-key rate limit exceeded; includes `Retry-After` | `urn:polyglot:error:rate-limited` |

The types are URNs rather than URLs: a client compares them, and a documentation link would rot.

`401` deliberately does not distinguish an unknown key from a disabled one; that difference belongs
in the logs administrators read, not in the response a caller gets.

Rate limiting: a fixed window per key, `Polyglot__Api__RequestsPerMinute` (default `120`) — generous
for polling, low enough to bound a misconfigured client. The window is partitioned by the key
identifier read straight out of the header, not by the authenticated principal, and the limiter runs
ahead of authentication. That is what makes a flood of invalid keys cost a rejection rather than a
database lookup each; anything without a parsable key falls back to a partition per client address,
the way sign-in already does.

## Configuration

| Variable | Default | Description |
|---|---|---|
| `Polyglot__Api__Enabled` | `true` | Delivery API. Requires `Polyglot__Auth__Enabled=true`, which in turn requires a database. |
| `Polyglot__Api__RequestsPerMinute` | `120` | Requests allowed per key per minute. |
| `Polyglot__Api__SnapshotCacheSeconds` | `10` | How long a snapshot is reused in process. |
| `Polyglot__Api__OpenApi__Enabled` | `true` | Serves the OpenAPI document at `/openapi/v1.json`, without a key. |

## Versioning

`/api/v1` is additive only: new fields and endpoints may appear, existing fields keep their meaning.
A breaking change means `/api/v2` served alongside v1, with its own OpenAPI document at
`/openapi/v2.json`. Field and `operationId` names are part of the contract: renaming one breaks every
generated client, so it counts as a new version.

## OpenAPI document

Instead of shipping a client, Polyglot publishes its contract and lets consumers generate one.

- Built with `Microsoft.AspNetCore.OpenApi` (part of ASP.NET Core 10, no Swashbuckle), served at
  `/openapi/v1.json` — and the same document as YAML at `/openapi/v1.yaml`.
- The document is pinned to **OpenAPI 3.0** rather than the 3.1 default of ASP.NET Core 10: generator
  support for 3.1 is still uneven, and generation is the entire point of publishing it. Worth
  revisiting once the common generators have caught up.
- Served without an API key. The schema is not a secret, and
  `openapi-generator -i https://polyglot.example.com/openapi/v1.json` should just work.
  `Polyglot__Api__OpenApi__Enabled=false` turns it off for instances that do not want it exposed.

What the document has to get right to produce usable clients:

- An explicit `operationId` on every endpoint (`getCultures`, `getTranslations`,
  `getCultureTranslations`) — generators derive method names from it, and an accidental rename is a
  breaking change for every generated client.
- Named response schemas backed by real DTO records (`CulturesResponse`, `TranslationsResponse`,
  `CultureTranslationsResponse`), never anonymous inline objects, so generated models get stable
  names. The key/value maps are declared as `additionalProperties: string`, which generators render as
  `Dictionary<string, string>`, `map[string]string`, `Record<string, string>`.
- A `securityScheme` of type `apiKey` in header `X-Api-Key`, applied to every operation, so a
  generated client exposes the key as configuration instead of leaving callers to add the header.
- Documented error responses (`400`, `401`, `403`, `404`, `429`) with `application/problem+json` and a
  `ProblemDetails` schema, so generated clients map failures to typed exceptions.
- `If-None-Match` declared as a header parameter, `ETag` on `200`, and `304` as a documented response.
  Several generators handle a bodyless `304` awkwardly, so conditional requests are documented but a
  client that ignores them still works — it just transfers more.

The document is also generated at build time (`Microsoft.Extensions.ApiDescription.Server`) and
committed as `docs/openapi/v1.json`, so every contract change shows up in a review diff. CI
regenerates it and fails when the committed copy is stale — the same reason migrations are committed
rather than inferred.

An interactive UI (Scalar, Swagger UI) is not part of ASP.NET Core's OpenAPI support and stays out of
scope; the document URL is all that generation needs.

```bash
# generate a TypeScript client
npx @openapitools/openapi-generator-cli generate \
  -i https://polyglot.example.com/openapi/v1.json -g typescript-fetch -o ./polyglot-client

# or skip generation entirely
curl -s -H "X-Api-Key: pg_7fk2ab91_..." https://polyglot.example.com/api/v1/translations/pl
```

A .NET package that plugs Polyglot into `IStringLocalizer` (most likely as a read-only CultureWay
`IStore` plus a refresher) remains the eventual convenience, but it is deliberately not part of this
iteration: it would also need a public cache-reload hook in CultureWay, whose `TranslationCache` is
`internal` and loaded once at startup.

## Deliberate limits

Two things were considered and left out on purpose, both recorded here so they are not re-litigated
by accident:

- **No key seeded from configuration.** The delivery API requires authentication to be enabled, as
  described under [Management](#management). Adding `Polyglot__Api__BootstrapKey` later is purely
  additive; removing a path once instances depend on it is not.
- **No per-culture keys.** A key reads every translation in the instance. Filtering per key would turn
  the single cached snapshot into one per combination of cultures, and — the deciding argument — it
  would not give what it looks like it gives: two separate owners both want `pl`, so isolating them is
  not a culture-level concern. If Polyglot ever has to serve unrelated owners, the answer is projects
  or namespaces that a key is bound to, and per-culture scoping would have been wasted work. The
  `Scopes` column stays, so `translations:write` can arrive without a migration.
