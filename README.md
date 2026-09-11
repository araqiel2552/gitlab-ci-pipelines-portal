# GitLab CI/CD Pipeline Portal

A Blazor Server (.NET 10) portal that mirrors the GitLab CI/CD pipeline experience for people who do
not have a GitLab account. Operators sign in with the company IdP (Okta / OIDC) and — within the
limits an administrator grants them — can watch pipelines run, inspect jobs, stream job logs and
download artifacts.

## Features

- **Multi-project** — administrators register any number of GitLab projects, each with its own
  instance URL and access token (stored encrypted with ASP.NET Data Protection).
- **User management** — per-user, per-project capabilities: `view`, `retry / cancel`, `run a new
  pipeline`. Admins implicitly get everything.
- **Pipeline dashboard** — pipeline list with GitLab-style status badges, ref, trigger source,
  duration, plus status filtering and auto-refresh.
- **Pipeline view** — jobs grouped in stage columns, with per-job retry / cancel / play actions.
- **Job view** — live-tailing terminal log, timing details, artifact list and download.

## Project layout

```
src/GitLabPortal
├── Components/Pages        Home, ProjectPipelines, PipelineDetail, JobDetail, Admin/*, Account/*
├── Components/Shared       StatusBadge, RedirectToLogin
├── Data                    AppDbContext, entities, EF Core migrations
├── Models                  GitLab REST API DTOs
└── Services                GitLabApiService, PortalAccessService, TokenProtector, …
```

## Running locally

```bash
dotnet run --project src/GitLabPortal
```

With no Okta settings present and `ASPNETCORE_ENVIRONMENT=Development`, the portal exposes a simple
development sign-in page at `/account/sign-in`. It only accepts emails that already exist in the
portal user table; `Portal:BootstrapAdmins` in `appsettings.Development.json` seeds
`admin@localhost` as the first administrator.

The SQLite database (`gitlabportal.db`) is created and migrated automatically on startup.

## Configuration

| Setting | Description |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQLite connection string. |
| `Okta:Authority` | Okta issuer URL, e.g. `https://dev-123.okta.com/oauth2/default`. |
| `Okta:ClientId` / `Okta:ClientSecret` | OIDC web application credentials. |
| `Okta:CallbackPath` | Redirect URI path, default `/authorization-code/callback`. |
| `Portal:BootstrapAdmins` | Emails promoted to administrator at startup. |

Store the client secret with `dotnet user-secrets` or environment variables — never in
`appsettings.json`. Outside Development the application refuses to start without Okta configured.

## GitLab access tokens

Each project needs a Project or Personal Access Token with the `api` scope. `read_api` is enough for
a view-only setup, but retrying or triggering pipelines requires `api`. Tokens are encrypted before
being written to the database and are never returned to the browser.
