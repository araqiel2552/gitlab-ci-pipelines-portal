using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GitLabPortal.Data;
using GitLabPortal.Models;

namespace GitLabPortal.Services;

/// <summary>
/// Thin wrapper around the GitLab REST API v4, authenticated per project with a stored PRIVATE-TOKEN.
/// </summary>
public class GitLabApiService(
    IHttpClientFactory httpClientFactory,
    ITokenProtector tokenProtector,
    ILogger<GitLabApiService> logger)
{
    public const string HttpClientName = "gitlab";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GitLabResult<List<GitLabPipeline>>> GetPipelinesAsync(
        GitLabProject project, string? refName = null, string? status = null, int page = 1, int perPage = 20, CancellationToken ct = default)
    {
        var query = $"pipelines?per_page={perPage}&page={page}&order_by=id&sort=desc";
        if (!string.IsNullOrWhiteSpace(refName)) query += $"&ref={Uri.EscapeDataString(refName)}";
        if (!string.IsNullOrWhiteSpace(status)) query += $"&status={Uri.EscapeDataString(status)}";
        return GetJsonAsync<List<GitLabPipeline>>(project, query, ct);
    }

    public Task<GitLabResult<GitLabPipeline>> GetPipelineAsync(
        GitLabProject project, long pipelineId, CancellationToken ct = default)
        => GetJsonAsync<GitLabPipeline>(project, $"pipelines/{pipelineId}", ct);

    public Task<GitLabResult<List<GitLabJob>>> GetPipelineJobsAsync(
        GitLabProject project, long pipelineId, CancellationToken ct = default)
        => GetJsonAsync<List<GitLabJob>>(project, $"pipelines/{pipelineId}/jobs?per_page=100&include_retried=false", ct);

    public Task<GitLabResult<GitLabJob>> GetJobAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
        => GetJsonAsync<GitLabJob>(project, $"jobs/{jobId}", ct);

    public Task<GitLabResult<List<GitLabBranch>>> GetBranchesAsync(
        GitLabProject project, CancellationToken ct = default)
        => GetJsonAsync<List<GitLabBranch>>(project, "repository/branches?per_page=100", ct);

    public async Task<GitLabResult<string>> GetJobLogAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
    {
        var prepared = Prepare(project, $"jobs/{jobId}/trace");
        if (prepared.Error is not null) return GitLabResult<string>.Fail(prepared.Error);

        try
        {
            using var request = prepared.CreateRequest(HttpMethod.Get);
            using var response = await prepared.Client.SendAsync(request, ct);

            // GitLab returns 404 while a job is still queued and has produced no trace yet.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return GitLabResult<string>.Ok(string.Empty);

            if (!response.IsSuccessStatusCode)
                return GitLabResult<string>.Fail(await DescribeFailureAsync(response, ct));

            return GitLabResult<string>.Ok(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            return Handle<string>(ex, project);
        }
    }

    public async Task<GitLabResult<ArtifactDownload>> DownloadArtifactsAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
    {
        var prepared = Prepare(project, $"jobs/{jobId}/artifacts");
        if (prepared.Error is not null) return GitLabResult<ArtifactDownload>.Fail(prepared.Error);

        try
        {
            using var request = prepared.CreateRequest(HttpMethod.Get);
            using var response = await prepared.Client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return GitLabResult<ArtifactDownload>.Fail("This job has no artifacts (or they have expired).");

            if (!response.IsSuccessStatusCode)
                return GitLabResult<ArtifactDownload>.Fail(await DescribeFailureAsync(response, ct));

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                           ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                           ?? $"artifacts-{jobId}.zip";
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/zip";

            return GitLabResult<ArtifactDownload>.Ok(new ArtifactDownload(bytes, Sanitize(fileName), contentType));
        }
        catch (Exception ex)
        {
            return Handle<ArtifactDownload>(ex, project);
        }
    }

    public Task<GitLabResult<GitLabPipeline>> CreatePipelineAsync(
        GitLabProject project, string refName, CancellationToken ct = default)
        => PostAsync<GitLabPipeline>(project, $"pipeline?ref={Uri.EscapeDataString(refName)}", ct);

    public Task<GitLabResult<GitLabPipeline>> RetryPipelineAsync(
        GitLabProject project, long pipelineId, CancellationToken ct = default)
        => PostAsync<GitLabPipeline>(project, $"pipelines/{pipelineId}/retry", ct);

    public Task<GitLabResult<GitLabPipeline>> CancelPipelineAsync(
        GitLabProject project, long pipelineId, CancellationToken ct = default)
        => PostAsync<GitLabPipeline>(project, $"pipelines/{pipelineId}/cancel", ct);

    public Task<GitLabResult<GitLabJob>> RetryJobAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
        => PostAsync<GitLabJob>(project, $"jobs/{jobId}/retry", ct);

    public Task<GitLabResult<GitLabJob>> CancelJobAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
        => PostAsync<GitLabJob>(project, $"jobs/{jobId}/cancel", ct);

    public Task<GitLabResult<GitLabJob>> PlayJobAsync(
        GitLabProject project, long jobId, CancellationToken ct = default)
        => PostAsync<GitLabJob>(project, $"jobs/{jobId}/play", ct);

    /// <summary>Validates connectivity and credentials for an unsaved project definition.</summary>
    public async Task<GitLabResult<string>> TestConnectionAsync(
        string gitLabUrl, string projectId, string plainToken, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(gitLabUrl, projectId, out var baseUri, out var error))
            return GitLabResult<string>.Fail(error);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
            request.Headers.Add("PRIVATE-TOKEN", plainToken);
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return GitLabResult<string>.Fail(await DescribeFailureAsync(response, ct));

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var name = doc.RootElement.TryGetProperty("path_with_namespace", out var p) ? p.GetString() : projectId;
            return GitLabResult<string>.Ok(name ?? projectId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitLab connection test failed for {ProjectId}.", projectId);
            return GitLabResult<string>.Fail($"Could not reach GitLab: {ex.Message}");
        }
    }

    private async Task<GitLabResult<T>> GetJsonAsync<T>(GitLabProject project, string relativePath, CancellationToken ct)
    {
        var prepared = Prepare(project, relativePath);
        if (prepared.Error is not null) return GitLabResult<T>.Fail(prepared.Error);

        try
        {
            using var request = prepared.CreateRequest(HttpMethod.Get);
            using var response = await prepared.Client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return GitLabResult<T>.Fail(await DescribeFailureAsync(response, ct));

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return value is null
                ? GitLabResult<T>.Fail("GitLab returned an empty response.")
                : GitLabResult<T>.Ok(value);
        }
        catch (Exception ex)
        {
            return Handle<T>(ex, project);
        }
    }

    private async Task<GitLabResult<T>> PostAsync<T>(GitLabProject project, string relativePath, CancellationToken ct)
    {
        var prepared = Prepare(project, relativePath);
        if (prepared.Error is not null) return GitLabResult<T>.Fail(prepared.Error);

        try
        {
            using var request = prepared.CreateRequest(HttpMethod.Post);
            using var response = await prepared.Client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return GitLabResult<T>.Fail(await DescribeFailureAsync(response, ct));

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return value is null
                ? GitLabResult<T>.Fail("GitLab returned an empty response.")
                : GitLabResult<T>.Ok(value);
        }
        catch (Exception ex)
        {
            return Handle<T>(ex, project);
        }
    }

    private PreparedRequest Prepare(GitLabProject project, string relativePath)
    {
        if (!TryBuildBaseUri(project.GitLabUrl, project.ProjectId, out var baseUri, out var error))
            return new PreparedRequest(null!, null!, null!, error);

        if (!tokenProtector.TryUnprotect(project.AccessTokenProtected, out var token) || string.IsNullOrWhiteSpace(token))
            return new PreparedRequest(null!, null!, null!, "The stored access token could not be decrypted. Please re-enter it in the settings.");

        var uri = new Uri($"{baseUri}/{relativePath}", UriKind.Absolute);
        return new PreparedRequest(httpClientFactory.CreateClient(HttpClientName), uri, token, null);
    }

    private static bool TryBuildBaseUri(string gitLabUrl, string projectId, out string baseUri, out string error)
    {
        baseUri = string.Empty;
        error = string.Empty;

        if (!Uri.TryCreate(gitLabUrl, UriKind.Absolute, out var host) ||
            (host.Scheme != Uri.UriSchemeHttp && host.Scheme != Uri.UriSchemeHttps))
        {
            error = "The configured GitLab URL is not a valid http(s) address.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            error = "No GitLab project id is configured.";
            return false;
        }

        baseUri = $"{host.GetLeftPart(UriPartial.Authority)}/api/v4/projects/{Uri.EscapeDataString(projectId.Trim())}";
        return true;
    }

    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "GitLab rejected the access token (401).",
            HttpStatusCode.Forbidden => "The access token lacks the required scope for this action (403).",
            HttpStatusCode.NotFound => "The requested GitLab resource was not found (404).",
            HttpStatusCode.TooManyRequests => "GitLab rate limit reached, please retry shortly (429).",
            _ => $"GitLab request failed with status {(int)response.StatusCode}."
        };

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return reason;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return $"{reason} {message}";
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"{reason} {err}";
        }
        catch (JsonException)
        {
            // Non-JSON error body; the generic reason is good enough.
        }

        return reason;
    }

    private GitLabResult<T> Handle<T>(Exception ex, GitLabProject project)
    {
        logger.LogError(ex, "GitLab API call failed for project {ProjectName}.", project.Name);
        return GitLabResult<T>.Fail(ex switch
        {
            TaskCanceledException => "The GitLab request timed out.",
            HttpRequestException => "Could not reach the GitLab server.",
            _ => "Unexpected error while calling GitLab."
        });
    }

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "artifacts.zip" : name;
    }

    private readonly record struct PreparedRequest(HttpClient Client, Uri Uri, string Token, string? Error)
    {
        public HttpRequestMessage CreateRequest(HttpMethod method)
        {
            var request = new HttpRequestMessage(method, Uri);
            request.Headers.Add("PRIVATE-TOKEN", Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            return request;
        }
    }
}
