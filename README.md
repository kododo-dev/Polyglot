# Polyglot

Self-hosted translation management for .NET. A standalone web app built on the [CultureWay](https://github.com/kododo-dev/CultureWay) packages: edit translations in a web UI with sign-in and roles, bring your own PostgreSQL, run it as a Docker image.

> Early development. The editor is protected by sign-in, but there is no API for consuming apps yet.

## Quick start

```bash
docker compose up --build
docker compose logs polyglot | grep "initial administrator"
```

The second command shows the generated password of the initial `admin` user (printed once). Open <http://localhost:8080>, sign in, and open the editor at `/translations`. Set `Polyglot__Auth__Bootstrap__Password` to choose the password yourself.

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

## Development

```bash
dotnet test src/Polyglot.slnx   # needs Docker (Testcontainers)
dotnet run --project src/Polyglot.Web
```

Adding an EF Core migration for the auth tables:

```bash
cd src/Polyglot.Web
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

## Roadmap

1. ~~Authentication and authorization: local users and OpenID Connect.~~
2. API keys and a delivery API for consuming apps, plus a client package.
3. Statuses, history, import and export.

## License

To be decided.
