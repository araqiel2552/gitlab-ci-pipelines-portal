using System.Text.Json.Serialization;

namespace GitLabPortal.Models;

public class GitLabUserRef
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
}

public class GitLabCommit
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("short_id")] public string? ShortId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("author_name")] public string? AuthorName { get; set; }
}

public class GitLabPipeline
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("iid")] public long Iid { get; set; }
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "unknown";
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("sha")] public string? Sha { get; set; }
    [JsonPropertyName("web_url")] public string? WebUrl { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("user")] public GitLabUserRef? User { get; set; }

    public string ShortSha => Sha is { Length: > 8 } ? Sha[..8] : Sha ?? string.Empty;
}

public class GitLabArtifact
{
    [JsonPropertyName("file_type")] public string? FileType { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("file_format")] public string? FileFormat { get; set; }
}

public class GitLabJob
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("stage")] public string Stage { get; set; } = "unknown";
    [JsonPropertyName("status")] public string Status { get; set; } = "unknown";
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("allow_failure")] public bool AllowFailure { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("queued_duration")] public double? QueuedDuration { get; set; }
    [JsonPropertyName("failure_reason")] public string? FailureReason { get; set; }
    [JsonPropertyName("web_url")] public string? WebUrl { get; set; }
    [JsonPropertyName("user")] public GitLabUserRef? User { get; set; }
    [JsonPropertyName("commit")] public GitLabCommit? Commit { get; set; }
    [JsonPropertyName("pipeline")] public GitLabPipeline? Pipeline { get; set; }
    [JsonPropertyName("artifacts")] public List<GitLabArtifact> Artifacts { get; set; } = [];
    [JsonPropertyName("artifacts_file")] public GitLabArtifact? ArtifactsFile { get; set; }

    public bool HasArtifacts => ArtifactsFile is not null || Artifacts.Any(a => a.FileType is "archive");
}

public class GitLabBranch
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("default")] public bool IsDefault { get; set; }
}

/// <summary>Downloaded artifact archive.</summary>
public record ArtifactDownload(byte[] Content, string FileName, string ContentType);

/// <summary>Uniform result wrapper so the UI can surface GitLab errors instead of throwing.</summary>
public class GitLabResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }

    public static GitLabResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static GitLabResult<T> Fail(string error) => new() { Success = false, Error = error };
}
