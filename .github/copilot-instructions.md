# GitLab Operator Portal Guidelines

## Project Context
Building a Blazor Server (.NET 10) web application to serve as an internal pipeline monitoring portal for operators who do not have GitLab user accounts. The portal lets authorized operators monitor pipeline execution, stream terminal logs, and download artifacts via the GitLab REST API.

## Architecture & Tech Stack
- **Framework:** Blazor Server (.NET 10) with Interactive Server render mode.
- **Database:** SQLite via Entity Framework Core (`AppDbContext`, `IDbContextFactory`).
- **Authentication:** OpenID Connect (OIDC) integrated with Okta.
- **GitLab Integration:** `HttpClient` service targeting GitLab REST API v4 (`/api/v4/projects/{id}/...`) authenticated via `PRIVATE-TOKEN` (Project or Personal Access Token).

## Application Requirements & Specifications

### 1. Database Schema (`Data/AppDbContext.cs`)
- **GitLabSettings Entity:** Stores `GitLabUrl`, `ProjectId`, and `AccessToken`.
- **AppUser Entity:** Stores `Email` and `Role` (`Operator`, `Admin`).
- Register using `AddDbContextFactory<AppDbContext>` for thread-safe Blazor circuit operation.

### 2. GitLab API Service (`Services/GitLabApiService.cs`)
Implement methods to communicate with GitLab REST API using the stored Access Token:
- `GetPipelinesAsync(string projectId)` -> `GET api/v4/projects/{id}/pipelines`
- `GetPipelineJobsAsync(string projectId, long pipelineId)` -> `GET api/v4/projects/{id}/pipelines/{id}/jobs`
- `GetJobLogAsync(string projectId, long jobId)` -> `GET api/v4/projects/{id}/jobs/{id}/trace`
- `DownloadArtifactsAsync(string projectId, long jobId)` -> `GET api/v4/projects/{id}/jobs/{id}/artifacts`

### 3. Authentication & Security
- Configure Okta OIDC in `Program.cs` using `AddOpenIdConnect` with `CookieAuthenticationDefaults`.
- Require authorization across all Razor pages except login handlers.

### 4. UI Layout & Components
- **Dashboard (`Home.razor`):** Mimic the GitLab pipeline view. Show pipeline lists with status badges (`success`, `failed`, `running`), branch names, and stage cards grouping individual jobs.
- **Log Streamer:** Display job raw console output in a styled terminal box (`bg-dark text-light`).
- **Artifact Downloader:** Provide a button per job to fetch artifact zips via JS interop (`downloadFile`).
- **Settings Page (`Settings.razor`):** Form allowing admins to update the stored GitLab project ID and access token.

## Code Conventions
- Use async/await for all DB and HTTP network operations.
- Inject `IDbContextFactory<AppDbContext>` inside Blazor components rather than scoped `AppDbContext`.
- Handle missing configuration gracefully with user alerts before calling GitLab API endpoints.