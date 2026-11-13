namespace Council.Core.Models;

public sealed record AIImage(
    string Name,
    string MimeType,
    byte[] Data);
