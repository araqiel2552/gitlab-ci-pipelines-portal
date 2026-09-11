using GitLabPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitLabPortal.Tests;

public class TokenProtectorTests
{
    private static TokenProtector Create(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider(), NullLogger<TokenProtector>.Instance);

    [Fact]
    public void Protect_then_unprotect_round_trips_the_token()
    {
        var protector = Create();

        var ciphertext = protector.Protect("glpat-secret");

        Assert.NotEqual("glpat-secret", ciphertext);
        Assert.True(protector.TryUnprotect(ciphertext, out var plaintext));
        Assert.Equal("glpat-secret", plaintext);
    }

    [Fact]
    public void TryUnprotect_fails_on_tampered_ciphertext()
    {
        var protector = Create();

        Assert.False(protector.TryUnprotect("not-a-payload", out var plaintext));
        Assert.Equal(string.Empty, plaintext);
    }

    [Fact]
    public void Tokens_cannot_be_decrypted_with_a_different_key_ring()
    {
        var ciphertext = Create().Protect("glpat-secret");

        Assert.False(Create().TryUnprotect(ciphertext, out _));
    }
}
