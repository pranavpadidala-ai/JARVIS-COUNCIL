using Council.Security;

namespace Council.Dev.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("api_key=super-secret-value", "api_key=\"[REDACTED]\"")]
    [InlineData("Password: \"hunter2\"", "Password: \"[REDACTED]\"")]
    [InlineData("Authorization: Bearer abcdefghijklmnop", "Authorization: Bearer [REDACTED]")]
    [InlineData("token sk-123456789012345678901234567890", "token [REDACTED]")]
    public void Redact_RemovesCommonCredentialFormats(string input, string expected)
    {
        var result = SecretRedactor.Redact(input);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("super-secret-value", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abcdefghijklmnop", result);
    }

    [Fact]
    public void LocalMemoryProtector_RoundTripsEncryptedContentOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string secretConversation = "remember this private research note";
        var protectedValue = LocalMemoryProtector.Protect(secretConversation);

        Assert.StartsWith("dpapi:v1:", protectedValue);
        Assert.DoesNotContain(secretConversation, protectedValue);
        Assert.Equal(secretConversation, LocalMemoryProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void LocalMemoryProtector_ReadsLegacyPlaintextRecords()
    {
        const string legacy = "older unencrypted conversation";
        Assert.Equal(legacy, LocalMemoryProtector.Unprotect(legacy));
    }

    [Fact]
    public void Redact_LeavesOrdinaryConversationTextUnchanged()
    {
        const string input = "Please summarize the structural analysis report.";

        Assert.Equal(input, SecretRedactor.Redact(input));
    }

    [Fact]
    public void Redact_HandlesNullAndEmptyInput()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }
}
