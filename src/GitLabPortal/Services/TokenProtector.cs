using Microsoft.AspNetCore.DataProtection;

namespace GitLabPortal.Services;

/// <summary>Encrypts GitLab access tokens before they are persisted to SQLite.</summary>
public interface ITokenProtector
{
    string Protect(string plaintext);
    bool TryUnprotect(string ciphertext, out string plaintext);
}

public class TokenProtector(IDataProtectionProvider provider, ILogger<TokenProtector> logger) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("GitLabPortal.AccessToken.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public bool TryUnprotect(string ciphertext, out string plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(ciphertext);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt a stored GitLab access token.");
            plaintext = string.Empty;
            return false;
        }
    }
}
