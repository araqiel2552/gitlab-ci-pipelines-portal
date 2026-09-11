using GitLabPortal.Models;

namespace GitLabPortal.Tests;

public class GitLabModelsTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abc123", "abc123")]
    [InlineData("0123456789abcdef", "01234567")]
    public void ShortSha_truncates_only_long_shas(string? sha, string expected)
        => Assert.Equal(expected, new GitLabPipeline { Sha = sha }.ShortSha);

    [Fact]
    public void HasArtifacts_is_false_when_nothing_was_produced()
        => Assert.False(new GitLabJob().HasArtifacts);

    [Fact]
    public void HasArtifacts_is_true_when_an_artifacts_file_exists()
        => Assert.True(new GitLabJob { ArtifactsFile = new GitLabArtifact { Size = 10 } }.HasArtifacts);

    [Fact]
    public void HasArtifacts_is_true_for_an_archive_entry()
        => Assert.True(new GitLabJob { Artifacts = [new GitLabArtifact { FileType = "archive" }] }.HasArtifacts);

    [Fact]
    public void HasArtifacts_ignores_non_archive_entries()
        => Assert.False(new GitLabJob { Artifacts = [new GitLabArtifact { FileType = "trace" }] }.HasArtifacts);

    [Fact]
    public void Ok_carries_the_value()
    {
        var result = GitLabResult<int>.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_carries_the_error()
    {
        var result = GitLabResult<int>.Fail("boom");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
        Assert.Equal(0, result.Value);
    }
}
