using Council.Core.Models;
using Council.Core.Providers;

namespace Council.App;

public sealed class ConsoleAssistant
{
    private const int MaximumConversationMessages = 25;

    private const string SystemPrompt = """
        You are JARVIS, a capable, practical personal AI assistant.
        Be clear, accurate, and concise. Explain technical topics step by step.
        Do not claim to have accessed files, apps, devices, or the internet unless a tool actually provided that information.
        You currently have text conversation only. Do not claim to control the computer yet.
        If a request requires a capability that is not available, say so plainly and suggest the next practical step.
        """;

    private readonly IAIProvider _provider;
    private readonly List<AIMessage> _history = [];

    public ConsoleAssistant(IAIProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        ResetConversation();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("                         J.A.R.V.I.S");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Provider : {_provider.Name}");
        Console.WriteLine("Type a message and press Enter.");
        Console.WriteLine("Commands: /help, /clear, /exit");
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (input is null)
            {
                break;
            }

            input = input.Trim();

            if (input.Length == 0)
            {
                continue;
            }

            if (string.Equals(input, "/exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(input, "/help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                continue;
            }

            if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
            {
                ResetConversation();
                Console.WriteLine("Conversation cleared.");
                continue;
            }

            _history.Add(new AIMessage("user", input));
            TrimHistory();

            try
            {
                Console.WriteLine();
                Console.Write("JARVIS: ");

                var response = await _provider.AskAsync(
                    new AIRequest(_history.ToArray(), Temperature: 0.4),
                    cancellationToken);

                Console.WriteLine(response.Content);
                Console.WriteLine();

                _history.Add(new AIMessage("assistant", response.Content));
                TrimHistory();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine();
                Console.WriteLine("JARVIS session cancelled.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"I couldn't complete that request: {ex.Message}");
                Console.WriteLine("Your conversation is still available. You can try again.");
                Console.WriteLine();
                _history.RemoveAt(_history.Count - 1);
            }
        }

        Console.WriteLine();
        Console.WriteLine("JARVIS session ended.");
    }

    private void ResetConversation()
    {
        _history.Clear();
        _history.Add(new AIMessage("system", SystemPrompt));
    }

    private void TrimHistory()
    {
        while (_history.Count > MaximumConversationMessages + 1)
        {
            _history.RemoveAt(1);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("JARVIS commands:");
        Console.WriteLine("  /help   Show this help");
        Console.WriteLine("  /clear  Start a fresh conversation");
        Console.WriteLine("  /exit   Close JARVIS");
        Console.WriteLine();
    }
}
