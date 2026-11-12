using Council.Core.Models;

namespace Council.Core.Providers;

public interface IDocumentUnderstandingProvider
{
    Task<string> AnalyzeDocumentAsync(string prompt, AIImage document, CancellationToken cancellationToken = default);
}
