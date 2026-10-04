# Polyglot

Self-hosted translation management for .NET, built on the [CultureWay](https://github.com/kododo-dev/CultureWay) packages. Translations are edited in a web UI with sign-in and roles, and apps read them through a read-only API. It runs as a Docker image on top of your own PostgreSQL.

> Early development.

## Quick start

Polyglot is published as `ghcr.io/kododo-dev/polyglot` for `linux/amd64` and `linux/arm64`. Save this as `compose.yaml`:

```yaml
services:
  polyglot:
    image: ghcr.io/kododo-dev/polyglot:latest
    ports:
      - "8080:8080"
    environment:
      ConnectionStrings__Default: "Host=db;Port=5432;Database=polyglot;Username=polyglot;Password=polyglot;Gss Encryption Mode=Disable"
      Polyglot__Cultures: "en,pl,de"
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: polyglot
      POSTGRES_USER: polyglot
      POSTGRES_PASSWORD: polyglot
    volumes:
      - polyglot-db:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U polyglot -d polyglot"]
      interval: 5s
      timeout: 3s
      retries: 10

volumes:
  polyglot-db:
```

Then start it and read the password of the initial administrator:

```bash
docker compose up -d
docker compose logs polyglot | grep "initial administrator"
```

The password of the initial `admin` user is printed once. Open <http://localhost:8080>, sign in, and open the editor at `/translations`. Set `Polyglot__Auth__Bootstrap__Password` to choose the password yourself.

Image tags: `latest` is the newest release, `1.2` follows the newest patch of 1.2, and `1.2.3` is one release. Pin a version in production.

## Configuration

Everything is configured through environment variables (or `appsettings.json`).

### Instance

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__Default` | none | PostgreSQL connection string. Required unless authentication is disabled; without it translations live in memory. |
| `Polyglot__Cultures` | `en` | Comma-separated cultures the instance starts with, e.g. `en,pl,de`. Languages added in the UI are persisted by the store. |
| `Polyglot__DefaultCulture` | `en` | Default culture. Added to the list if missing. |
| `Polyglot__EditorPath` | `/translations` | Path the editor is mounted at. |
| `Polyglot__PathBase` | empty | Path base when served under a sub-path behind a reverse proxy. |

The container listens on port `8080` and trusts `X-Forwarded-*` headers. `GET /health` returns `200` when the app is up.

### Authentication

| Variable | Default | Description |
|---|---|---|
| `Polyglot__Auth__Enabled` | `true` | Set to `false` to run without authentication, only for instances protected some other way. Anyone who can reach the instance can then edit translations. |
| `Polyglot__Auth__Local__Enabled` | `true` | Username and password sign-in. |
| `Polyglot__Auth__Bootstrap__Username` | `admin` | Initial administrator, created when the instance has no users. |
| `Polyglot__Auth__Bootstrap__Password` | generated | Its password. When empty, a random one is generated and logged once. |
| `Polyglot__Auth__LoginAttemptsPerMinute` | `10` | Sign-in attempts allowed per client address per minute. |

Roles: **Admin** (editor plus user management), **Editor** (translation editor), **None** (signed in, no access). Role changes and disabling a user take effect on the next request. The last active administrator cannot be demoted or disabled. Users are managed at `/admin/users`, and everyone can change their own password at `/account`.

Data Protection keys (which protect the sign-in cookie) are stored in the database, so sessions survive restarts and work across replicas. They are stored unencrypted; protect access to the database accordingly.

### OpenID Connect

Any OIDC provider works (Keycloak, Entra ID, Authentik, Google, ...). The app uses the authorization code flow with PKCE. Register `https://<your-host>/signin-oidc` as the redirect URI.

| Variable | Default | Description |
|---|---|---|
| `Polyglot__Auth__Oidc__Authority` | none | Issuer URL. OIDC is enabled when this and `ClientId` are set. |
| `Polyglot__Auth__Oidc__ClientId` / `ClientSecret` | none | Client credentials. |
| `Polyglot__Auth__Oidc__DisplayName` | `Single sign-on` | Label of the sign-in button. |
| `Polyglot__Auth__Oidc__ExtraScopes` | empty | Comma-separated scopes requested in addition to `openid profile email`, e.g. a `groups` scope. |
| `Polyglot__Auth__Oidc__RequireHttpsMetadata` | `true` | Set to `false` only for a local provider without TLS. |
| `Polyglot__Auth__Oidc__GroupsClaim` | `groups` | Claim (in the ID token) that carries the user's groups. |
| `Polyglot__Auth__Oidc__AdminGroup` / `EditorGroup` | none | Members become Admin / Editor. With either set, the role is recomputed at every sign-in and users in neither group get `None`. |
| `Polyglot__Auth__Oidc__DefaultRole` | `Editor` | Role of a new user when no group is configured. Afterwards administrators manage it. |

Users are created on their first sign-in. To use OIDC only, set `Polyglot__Auth__Local__Enabled=false` together with `AdminGroup` (so that someone can administer the instance).

### Delivery API

A read-only HTTP API from which consuming apps fetch their translations. It authenticates with API keys, which administrators issue at `/admin/api-keys`. A new key is shown once, when it is created. Requires authentication to be enabled.

```bash
curl -H "X-Api-Key: pg_7fk2ab91_..." https://polyglot.example.com/api/v1/cultures
curl -H "X-Api-Key: pg_7fk2ab91_..." "https://polyglot.example.com/api/v1/translations/pl?namespace=Checkout&namespace=Common"
```

`GET /api/v1/translations/{culture}` returns a flat map of full keys to values, for example `{"Checkout.Pay": "Zapłać"}`. Every key is resolved along the same chain CultureWay's `IStringLocalizer` uses: the culture, then its parents, then the default culture. `namespace` is optional and can be repeated. It selects keys in that namespace or under it, as the editor's namespace filter does.

Responses have an `ETag`. Send it back as `If-None-Match` and the API answers `304` when nothing has changed. An edit reaches consuming apps within `SnapshotCacheSeconds` plus their own poll interval.

There is no client library. To get a client, generate one from the OpenAPI 3.0 document at `/openapi/v1.json` (also available as `.yaml`). It is served without a key, and a copy is in [docs/openapi/v1.json](docs/openapi/v1.json).

```bash
npx @openapitools/openapi-generator-cli generate \
  -i https://polyglot.example.com/openapi/v1.json -g typescript-fetch -o ./polyglot-client
```

| Variable | Default | Description |
|---|---|---|
| `Polyglot__Api__Enabled` | `true` | The delivery API. Not mapped when authentication is disabled, because there is then no way to issue or revoke a key. |
| `Polyglot__Api__RequestsPerMinute` | `120` | Requests allowed per key per minute; requests without a usable key are counted per client address. Excess requests get `429` with `Retry-After`. |
| `Polyglot__Api__SnapshotCacheSeconds` | `10` | How long the API reuses one read of all translations. `0` reads the database on every request. |
| `Polyglot__Api__OpenApi__Enabled` | `true` | Serves the OpenAPI document. |

Design notes: [docs/delivery-api.md](docs/delivery-api.md).

## Development

```bash
dotnet test src/Polyglot.slnx   # needs Docker (Testcontainers)
dotnet run --project src/Polyglot.Web
docker compose up --build       # builds the image from source, with PostgreSQL
```

Adding an EF Core migration for the auth tables:

```bash
cd src/Polyglot.Web
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

A test fails when [docs/openapi/v1.json](docs/openapi/v1.json) differs from the document the app serves. After changing the API, regenerate the file and commit it:

```bash
POLYGLOT_UPDATE_OPENAPI=1 dotnet test src/Polyglot.slnx --filter OpenApiTests
```

### Releases

Pushing a `v*` tag (for example `v0.1.0`) runs CI and then publishes the image to GHCR for `linux/amd64` and `linux/arm64`, with the version from the tag. A pre-release tag such as `v0.2.0-rc.1` is published under its own tag only and does not move `latest`.

## Roadmap

1. ~~Authentication and authorization: local users and OpenID Connect.~~
2. ~~API keys and a delivery API for consuming apps, with an OpenAPI document for generating
   clients ([docs/delivery-api.md](docs/delivery-api.md)).~~
3. A published Docker image, so Polyglot can be run without building it from source.
4. Statuses, history, import and export.
5. A .NET client package that plugs a consuming app into Polyglot through `IStringLocalizer`.
6. AI help with translation: connect an AI model that suggests translations into other languages.

## License

To be decided.
