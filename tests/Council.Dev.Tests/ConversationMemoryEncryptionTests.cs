using Council.Core.Models;
using Council.Memory;

namespace Council.Dev.Tests;

public sealed class ConversationMemoryEncryptionTests
{
    [Fact]
    public async Task Append_MigratesLegacyPlaintextAndKeepsHistoryReadableOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "jarvis-memory-" + Guid.NewGuid().ToString("N") + ".jsonl");
        const string legacyText = "legacy private conversation";
        try
        {
            var legacyRecord = System.Text.Json.JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Role = "user",
                Content = legacyText
            });
            await File.WriteAllTextAsync(path, legacyRecord + Environment.NewLine);

            var store = new ConversationMemoryStore(path);
            await store.AppendAsync(new AIMessage("assistant", "new private response"));

            var persisted = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(legacyText, persisted);
            Assert.DoesNotContain("new private response", persisted);
            Assert.Contains("dpapi:v1:", persisted);

            var loaded = await store.LoadRecentAsync(10);
            Assert.Contains(loaded, message => message.Content == legacyText);
            Assert.Contains(loaded, message => message.Content == "new private response");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
