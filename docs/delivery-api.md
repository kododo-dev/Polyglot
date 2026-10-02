# Delivery API — contract

Design for roadmap item 2: API keys, a read-only delivery API for consuming apps, and an OpenAPI
document they generate a client from. The contract was fixed before implementation and is kept in step
with it: the keys, the `ApiKey` scheme, rate limiting, `GET /api/v1/cultures` and
`GET /api/v1/translations/{culture}` with its snapshot cache are in place; the admin page and the
OpenAPI document are not yet.

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

### `GET /api/v1/translations/{culture}`

The translations an app needs to render in one culture, optionally narrowed to some namespaces:
`/api/v1/translations/pl?namespace=Checkout&namespace=Common`.

```json
{
  "Checkout.Pay": "Zapłać",
  "Checkout.Summary.Total": "Razem",
  "Common.Cancel": "Anuluj"
}
```

The body is the translations and nothing else, so an app hands it straight to its i18n library. The
rest travels outside it: the version is the `ETag` header (see
[Caching](#caching-and-change-detection)), and the culture and namespaces are the ones the caller
asked for, so echoing them back would add nothing.

**Culture.** Required, and the culture fallback is Polyglot's, not the caller's: every key is resolved
along the chain CultureWay's own `IStringLocalizer` uses — the culture, then its parents, then the
default culture (`pl-PL` → `pl` → `en`). The response therefore holds every key that has a value
anywhere in that chain, without saying which culture a value came from; an app gets what the
localizer would have given it and implements no fallback of its own. A key is absent only when no
culture in the chain has it, and the app shows the key itself, as the localizer does.

- `404` when neither the culture nor any of its parents is supported. Answering with the default
  culture instead would turn a typo such as `xx` into silently English pages.
- A supported culture with no translations of its own still returns `200` with whatever the chain
  provides — a real answer.

**Namespaces.** CultureWay has no namespace entity: a key's namespace is everything before its last
dot, and the editor builds its tree from those prefixes. `namespace` follows the editor's filter, so
`namespace=Checkout` selects keys whose namespace is `Checkout` or lies under it (`Checkout.Pay`,
`Checkout.Summary.Total`).

- Optional and repeatable; the response holds the union. Without it, the whole culture is returned. A
  page usually needs its own namespace and a shared one, and one request is simpler than two.
- A namespace with no keys returns `200` and an empty object, not `404`: a namespace exists only while
  it has keys, so deleting the last one must not break the app asking for it.
- `400` for a malformed namespace — empty, or with an empty segment (`.Checkout`, `A..B`).
- Keys without a dot (the editor's "(root)") are returned only without a filter. Selecting them alone
  can be added later without breaking anything.

**Payload.** Keys are full keys in a flat map — not stripped of the namespace and not nested. Stripping
would collide when several namespaces are requested, and the app looks keys up by their full name
anyway. Nesting cannot express CultureWay's keys, where one key may be the namespace of another —
`Checkout.Pay` and `Checkout.Pay.Tooltip` may both exist, and `Pay` cannot be a string and an object
at once. CultureWay does not forbid this, and the editor invites it by prefilling a new key with the
selected namespace. A nested-JSON library such as i18next reads the flat map with
`keySeparator: false`. `null` values never appear.
Values are returned as stored, without processing: the API neither imposes nor converts any
placeholder syntax. Entries are sorted by key, so the payload — and therefore the version — is
deterministic.

A bare map cannot grow fields, so this response is the one exception to `/api/v1` being additive
(see [Versioning](#versioning)): anything else it ever needs to say goes into a header, or into
`/api/v2`.

### Composition

A snapshot merges `IStore.GetAllAsync()` with any registered `IReadOnlySource` defaults, store values
winning, exactly as the editor's read path does. Polyglot registers no read-only source today, but
the delivery API must not disagree with the editor if one is ever added.

## Caching and change detection

- The version is SHA-256 over the response's canonical content (sorted entries), first 16 hex
  characters. `GET /api/v1/cultures` also carries it in the body as `version`; the translations
  response carries it only in the header.
- It is sent as the strong ETag: `ETag: "a71b0ce4425d9f18"`.
- `If-None-Match` with a matching value returns `304` and no body.
- `Cache-Control: no-cache` — always revalidate, never let a shared proxy serve a stale snapshot. The
  cheap path is the `304`, not a freshness guess.
- Every response carries its own version, computed after the fallback chain and the namespace filter
  are applied: a change in `de` does not invalidate a client polling `pl`, nor a change in `Admin` one
  polling `Checkout`. A change in the default culture does invalidate every culture that falls back to
  it, because their responses really change.

Building a snapshot per request would mean selecting every translation on every poll, so Polyglot
keeps one in process — every culture, unfiltered — and resolves and filters each response from it in
memory, memoising the result per culture and namespace set for the snapshot's lifetime. The snapshot
lives for a short TTL: `Polyglot__Api__SnapshotCacheSeconds` (default `10`). A TTL
rather than invalidation on write, because with several replicas a write handled by one replica
cannot invalidate the others, and the editor is the only writer. The visible effect is that an edit
reaches consuming apps within `SnapshotCacheSeconds` plus the client's own poll interval.

## Errors

| Status | When | `type` |
|---|---|---|
| `400` | malformed culture code or namespace | `urn:polyglot:error:invalid-request` |
| `401` | missing, malformed, unknown, expired or disabled key | `urn:polyglot:error:unauthorized` |
| `403` | valid key without the required scope | `urn:polyglot:error:forbidden` |
| `404` | neither the culture nor any of its parents is supported | `urn:polyglot:error:not-found` |
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
| `Polyglot__Api__SnapshotCacheSeconds` | `10` | How long a snapshot is reused in process. `0` reads the store on every request. |
| `Polyglot__Api__OpenApi__Enabled` | `true` | Serves the OpenAPI document at `/openapi/v1.json`, without a key. |

## Versioning

`/api/v1` is additive only: new fields and endpoints may appear, existing fields keep their meaning.
The translations response is a bare map and has no room for new fields; it can only gain headers.
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

- An explicit `operationId` on every endpoint (`getCultures`, `getTranslations`) — generators derive
  method names from it, and an accidental rename is a breaking change for every generated client.
- A named response schema backed by a real DTO record for `getCultures` (`CulturesResponse`), never
  an anonymous inline object, so the generated model gets a stable name. `getTranslations` returns an
  object with `additionalProperties: string`, which generators render as `Dictionary<string, string>`,
  `map[string]string`, `Record<string, string>` — a plain map type, not a model.
- `namespace` declared as an array query parameter (`style: form`, `explode: true`), so a generated
  client takes a list of namespaces rather than a string the caller has to assemble.
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

These were considered and left out on purpose, all recorded here so they are not re-litigated by
accident:

- **No endpoint returning every culture at once.** A consuming app renders a page in one culture at
  a time; an app serving several languages asks once per culture, and each answer keeps its own
  version.
- **No `fallback` parameter.** How a missing translation is resolved is Polyglot's policy, the same
  chain the localizer applies, not something a reader chooses per request. Letting callers switch it
  off would let two apps disagree about what the same page says.
- **Namespaces in the query, not the path.** `/translations/{culture}/{namespaces}` reads nicer, but a
  path parameter cannot be optional in OpenAPI (two routes and two operations instead of one), a list
  in a path is a mini-language every client must assemble and escape, while a repeated query parameter
  becomes a plain array in a generated client, and CultureWay keys may contain `/` — which reverse
  proxies mangle or reject in a path segment as `%2F` — or `,`.

- **No key seeded from configuration.** The delivery API requires authentication to be enabled, as
  described under [Management](#management). Adding `Polyglot__Api__BootstrapKey` later is purely
  additive; removing a path once instances depend on it is not.
- **No per-culture keys.** A key reads every translation in the instance. Filtering per key would turn
  the single cached snapshot into one per combination of cultures, and — the deciding argument — it
  would not give what it looks like it gives: two separate owners both want `pl`, so isolating them is
  not a culture-level concern. If Polyglot ever has to serve unrelated owners, the answer is projects
  or namespaces that a key is bound to, and per-culture scoping would have been wasted work. The
  `Scopes` column stays, so `translations:write` can arrive without a migration.
