using Council.Dev.Terminal;
using Council.Dev.Validation;

namespace Council.Dev.Tests;

public sealed class ValidationLogStoreTests
{
    [Fact]
    public async Task Validation_logs_are_persisted_and_common_secrets_are_redacted()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ValidationLogStore(root);
            var result = new TerminalResult(
                1,
                "Build failed. API_KEY=super-secret-value",
                "Request failed: Bearer abc.def.ghi",
                TimeSpan.FromSeconds(2),
                false,
                false);

            var path = await store.SaveAsync("run-123", "build", result);
            var saved = await File.ReadAllTextAsync(path);

            Assert.Contains("[REDACTED]", saved);
            Assert.DoesNotContain("super-secret-value", saved);
            Assert.DoesNotContain("abc.def.ghi", saved);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}