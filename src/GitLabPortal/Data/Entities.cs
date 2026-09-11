using System.ComponentModel.DataAnnotations;

namespace GitLabPortal.Data;

public enum UserRole
{
    Operator = 0,
    Admin = 1
}

/// <summary>
/// A GitLab project exposed through the portal, together with the access token used to read it.
/// </summary>
public class GitLabProject
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Base URL of the GitLab instance, e.g. https://gitlab.com</summary>
    [Required, MaxLength(256)]
    public string GitLabUrl { get; set; } = "https://gitlab.com";

    /// <summary>Numeric id or URL-encoded path of the project.</summary>
    [Required, MaxLength(256)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Access token ciphertext produced by <c>ITokenProtector</c>.</summary>
    [Required]
    public string AccessTokenProtected { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DefaultRef { get; set; } = "main";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<ProjectPermission> Permissions { get; set; } = [];
}

public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.Operator;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginUtc { get; set; }

    public List<ProjectPermission> Permissions { get; set; } = [];
}

/// <summary>
/// Per-project capabilities granted to a user: view pipelines, retry them, or trigger new ones.
/// </summary>
public class ProjectPermission
{
    public int Id { get; set; }

    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public int GitLabProjectId { get; set; }
    public GitLabProject? GitLabProject { get; set; }

    public bool CanView { get; set; } = true;
    public bool CanRetry { get; set; }
    public bool CanRun { get; set; }
}
