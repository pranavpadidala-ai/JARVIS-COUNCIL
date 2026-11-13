using Council.App;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Dev.Tests;

public sealed class ConsoleAssistantTests
{
    [Fact]
    public async Task Conversation_history_is_sent_to_provider()
    {
        var provider = new RecordingProvider();
        var originalInput = Console.In;
        var originalOutput = Console.Out;

        try
        {
            Console.SetIn(new StringReader("first question\nsecond question\n/exit\n"));
            Console.SetOut(new StringWriter());

            var assistant = new ConsoleAssistant(provider);
            await assistant.RunAsync();

            Assert.Equal(2, provider.Requests.Count);

            var secondRequest = provider.Requests[1].Messages;
            Assert.Contains(secondRequest, m => m.Role == "user" && m.Content == "first question");
            Assert.Contains(secondRequest, m => m.Role == "assistant" && m.Content == "Recorded: first question");
            Assert.Equal("second question", secondRequest[^1].Content);
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task Clear_command_resets_conversation_history()
    {
        var provider = new RecordingProvider();
        var originalInput = Console.In;
        var originalOutput = Console.Out;

        try
        {
            Console.SetIn(new StringReader("first question\n/clear\nsecond question\n/exit\n"));
            Console.SetOut(new StringWriter());

            var assistant = new ConsoleAssistant(provider);
            await assistant.RunAsync();

            Assert.Equal(2, provider.Requests.Count);
            var secondRequest = provider.Requests[1].Messages;
            Assert.DoesNotContain(secondRequest, m => m.Content == "first question");
            Assert.Contains(secondRequest, m => m.Role == "system");
            Assert.Equal("second question", secondRequest[^1].Content);
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
        }
    }

    private sealed class RecordingProvider : IAIProvider
    {
        public string Name => "Test";
        public List<AIRequest> Requests { get; } = [];

        public Task<AIResponse> AskAsync(
            AIRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var question = request.Messages.Last(m => m.Role == "user").Content;
            return Task.FromResult(new AIResponse(
                "Test",
                $"Recorded: {question}",
                "test-model"));
        }
    }
}
