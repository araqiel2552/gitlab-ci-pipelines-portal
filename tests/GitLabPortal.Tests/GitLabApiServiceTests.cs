using System.Net;
using System.Net.Http.Headers;
using GitLabPortal.Data;
using GitLabPortal.Models;
using GitLabPortal.Services;
using GitLabPortal.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitLabPortal.Tests;

public class GitLabApiServiceTests
{
    private static GitLabProject Project(
        string url = "https://gitlab.example.com",
        string projectId = "42",
        string token = FakeTokenProtector.Prefix + "secret-token") =>
        new()
        {
            Id = 1,
            Name = "Demo",
            GitLabUrl = url,
            ProjectId = projectId,
            AccessTokenProtected = token
        };

    private static GitLabApiService CreateService(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new FakeTokenProtector(), NullLogger<GitLabApiService>.Instance);

    [Fact]
    public async Task GetPipelinesAsync_calls_the_v4_pipelines_endpoint_with_the_private_token()
    {
        var handler = StubHttpMessageHandler.Json("""[{"id":7,"status":"success","ref":"main"}]""");
        var service = CreateService(handler);

        var result = await service.GetPipelinesAsync(Project());

        Assert.True(result.Success);
        var pipeline = Assert.Single(result.Value!);
        Assert.Equal(7, pipeline.Id);
        Assert.Equal("success", pipeline.Status);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://gitlab.example.com/api/v4/projects/42/pipelines", request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("secret-token", Assert.Single(request.Headers.GetValues("PRIVATE-TOKEN")));
    }

    [Fact]
    public async Task GetPipelinesAsync_appends_paging_and_filter_parameters()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var service = CreateService(handler);

        await service.GetPipelinesAsync(Project(), refName: "feature/a b", status: "failed", page: 3, perPage: 5);

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("per_page=5", query);
        Assert.Contains("page=3", query);
        Assert.Contains("ref=feature%2Fa%20b", query);
        Assert.Contains("status=failed", query);
    }

    [Fact]
    public async Task Project_paths_are_url_encoded()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var service = CreateService(handler);

        await service.GetBranchesAsync(Project(projectId: "group/sub/project"));

        Assert.StartsWith(
            "https://gitlab.example.com/api/v4/projects/group%2Fsub%2Fproject/repository/branches",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://gitlab.example.com")]
    public async Task An_invalid_gitlab_url_fails_before_any_request(string url)
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var service = CreateService(handler);

        var result = await service.GetPipelinesAsync(Project(url: url));

        Assert.False(result.Success);
        Assert.Contains("not a valid http(s) address", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_missing_project_id_fails_before_any_request()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var service = CreateService(handler);

        var result = await service.GetPipelinesAsync(Project(projectId: "  "));

        Assert.False(result.Success);
        Assert.Contains("No GitLab project id is configured", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_undecryptable_token_fails_before_any_request()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var service = CreateService(handler);

        var result = await service.GetPipelinesAsync(Project(token: "garbage"));

        Assert.False(result.Success);
        Assert.Contains("could not be decrypted", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "(401)")]
    [InlineData(HttpStatusCode.Forbidden, "(403)")]
    [InlineData(HttpStatusCode.NotFound, "(404)")]
    [InlineData(HttpStatusCode.TooManyRequests, "(429)")]
    [InlineData(HttpStatusCode.InternalServerError, "status 500")]
    public async Task Http_error_codes_are_translated_to_operator_friendly_messages(HttpStatusCode code, string expected)
    {
        var service = CreateService(StubHttpMessageHandler.Status(code));

        var result = await service.GetPipelinesAsync(Project());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task A_gitlab_json_error_body_is_appended_to_the_message()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized, """{"message":"401 Unauthorized"}"""));

        var result = await service.GetPipelinesAsync(Project());

        Assert.False(result.Success);
        Assert.Contains("401 Unauthorized", result.Error);
    }

    [Fact]
    public async Task A_non_json_error_body_is_ignored()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.BadGateway, "<html>gateway</html>"));

        var result = await service.GetPipelinesAsync(Project());

        Assert.False(result.Success);
        Assert.Equal("GitLab request failed with status 502.", result.Error);
    }

    [Fact]
    public async Task Network_failures_report_an_unreachable_server()
    {
        var service = CreateService(StubHttpMessageHandler.Throws(new HttpRequestException("no route")));

        var result = await service.GetPipelinesAsync(Project());

        Assert.False(result.Success);
        Assert.Equal("Could not reach the GitLab server.", result.Error);
    }

    [Fact]
    public async Task Timeouts_report_a_timed_out_request()
    {
        var service = CreateService(StubHttpMessageHandler.Throws(new TaskCanceledException()));

        var result = await service.GetPipelinesAsync(Project());

        Assert.False(result.Success);
        Assert.Equal("The GitLab request timed out.", result.Error);
    }

    [Fact]
    public async Task GetJobLogAsync_returns_the_raw_trace()
    {
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.OK, "$ echo hello\nhello\n");
        var service = CreateService(handler);

        var result = await service.GetJobLogAsync(Project(), 99);

        Assert.True(result.Success);
        Assert.Equal("$ echo hello\nhello\n", result.Value);
        Assert.EndsWith("/api/v4/projects/42/jobs/99/trace", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetJobLogAsync_treats_404_as_an_empty_trace_for_queued_jobs()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var result = await service.GetJobLogAsync(Project(), 99);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public async Task DownloadArtifactsAsync_returns_the_archive_and_its_file_name()
    {
        var payload = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"build-output.zip\""
            };
            return response;
        });
        var service = CreateService(handler);

        var result = await service.DownloadArtifactsAsync(Project(), 99);

        Assert.True(result.Success);
        Assert.Equal(payload, result.Value!.Content);
        Assert.Equal("build-output.zip", result.Value.FileName);
        Assert.Equal("application/zip", result.Value.ContentType);
    }

    [Fact]
    public async Task DownloadArtifactsAsync_strips_directory_traversal_from_the_file_name()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"../../etc/passwd\""
            };
            return response;
        });
        var service = CreateService(handler);

        var result = await service.DownloadArtifactsAsync(Project(), 99);

        Assert.True(result.Success);
        Assert.Equal("passwd", result.Value!.FileName);
    }

    [Fact]
    public async Task DownloadArtifactsAsync_falls_back_to_a_job_scoped_file_name()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.OK, "zip"));

        var result = await service.DownloadArtifactsAsync(Project(), 99);

        Assert.True(result.Success);
        Assert.Equal("artifacts-99.zip", result.Value!.FileName);
    }

    [Fact]
    public async Task DownloadArtifactsAsync_reports_missing_or_expired_artifacts()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var result = await service.DownloadArtifactsAsync(Project(), 99);

        Assert.False(result.Success);
        Assert.Contains("no artifacts", result.Error);
    }

    [Fact]
    public async Task CreatePipelineAsync_posts_to_the_pipeline_endpoint_with_an_escaped_ref()
    {
        var handler = StubHttpMessageHandler.Json("""{"id":11,"status":"created","ref":"release/1.0"}""");
        var service = CreateService(handler);

        var result = await service.CreatePipelineAsync(Project(), "release/1.0");

        Assert.True(result.Success);
        Assert.Equal(11, result.Value!.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith("/api/v4/projects/42/pipeline?ref=release%2F1.0", handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("cancel")]
    public async Task Pipeline_actions_post_to_the_matching_endpoint(string action)
    {
        var handler = StubHttpMessageHandler.Json("""{"id":11,"status":"running"}""");
        var service = CreateService(handler);

        var result = action == "retry"
            ? await service.RetryPipelineAsync(Project(), 11)
            : await service.CancelPipelineAsync(Project(), 11);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith($"/api/v4/projects/42/pipelines/11/{action}", handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("cancel")]
    [InlineData("play")]
    public async Task Job_actions_post_to_the_matching_endpoint(string action)
    {
        var handler = StubHttpMessageHandler.Json("""{"id":5,"name":"deploy","stage":"deploy","status":"pending"}""");
        var service = CreateService(handler);

        var result = action switch
        {
            "retry" => await service.RetryJobAsync(Project(), 5),
            "cancel" => await service.CancelJobAsync(Project(), 5),
            _ => await service.PlayJobAsync(Project(), 5)
        };

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith($"/api/v4/projects/42/jobs/5/{action}", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetPipelineJobsAsync_deserializes_jobs_and_artifact_metadata()
    {
        var handler = StubHttpMessageHandler.Json("""
            [
              {
                "id": 5,
                "name": "build",
                "stage": "build",
                "status": "success",
                "duration": 12.5,
                "artifacts_file": { "filename": "artifacts.zip", "size": 2048 }
              }
            ]
            """);
        var service = CreateService(handler);

        var result = await service.GetPipelineJobsAsync(Project(), 11);

        Assert.True(result.Success);
        var job = Assert.Single(result.Value!);
        Assert.Equal("build", job.Stage);
        Assert.Equal(12.5, job.Duration);
        Assert.True(job.HasArtifacts);
    }

    [Fact]
    public async Task An_empty_json_response_is_reported_as_a_failure()
    {
        var service = CreateService(StubHttpMessageHandler.Json("null"));

        var result = await service.GetPipelineAsync(Project(), 11);

        Assert.False(result.Success);
        Assert.Equal("GitLab returned an empty response.", result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_returns_the_project_path_on_success()
    {
        var handler = StubHttpMessageHandler.Json("""{"id":42,"path_with_namespace":"acme/widgets"}""");
        var service = CreateService(handler);

        var result = await service.TestConnectionAsync("https://gitlab.example.com", "42", "plain-token");

        Assert.True(result.Success);
        Assert.Equal("acme/widgets", result.Value);
        Assert.Equal("plain-token", Assert.Single(handler.LastRequest.Headers.GetValues("PRIVATE-TOKEN")));
        Assert.Equal("https://gitlab.example.com/api/v4/projects/42", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task TestConnectionAsync_rejects_an_invalid_url_without_calling_out()
    {
        var handler = StubHttpMessageHandler.Json("{}");
        var service = CreateService(handler);

        var result = await service.TestConnectionAsync("nope", "42", "plain-token");

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TestConnectionAsync_surfaces_rejected_credentials()
    {
        var service = CreateService(StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized));

        var result = await service.TestConnectionAsync("https://gitlab.example.com", "42", "bad-token");

        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_surfaces_transport_failures()
    {
        var service = CreateService(StubHttpMessageHandler.Throws(new HttpRequestException("dns failure")));

        var result = await service.TestConnectionAsync("https://gitlab.example.com", "42", "token");

        Assert.False(result.Success);
        Assert.Contains("Could not reach GitLab", result.Error);
    }
}
