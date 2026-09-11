using GitLabPortal.Services;

namespace GitLabPortal.Tests.Infrastructure;

/// <summary>Reversible "encryption" so tests can assert on the token that reaches GitLab.</summary>
public sealed class FakeTokenProtector : ITokenProtector
{
    public const string Prefix = "enc:";

    public string Protect(string plaintext) => Prefix + plaintext;

    public bool TryUnprotect(string ciphertext, out string plaintext)
    {
        if (ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            plaintext = ciphertext[Prefix.Length..];
            return true;
        }

        plaintext = string.Empty;
        return false;
    }
}
