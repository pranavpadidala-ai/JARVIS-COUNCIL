using Council.Core.Models;

namespace Council.Core.Providers;

/// <summary>
/// Optional provider capability for analysing image inputs without requiring
/// every text-only provider implementation to support multimodal requests.
/// </summary>
public interface IImageUnderstandingProvider
{
    Task<string> DescribeImagesAsync(
        string prompt,
        IReadOnlyList<AIImage> images,
        CancellationToken cancellationToken = default);
}
