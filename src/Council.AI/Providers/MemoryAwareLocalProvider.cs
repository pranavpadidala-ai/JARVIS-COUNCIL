using System.Runtime.InteropServices;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Resource-aware local brain for a laptop with 8 GB RAM and no GPU: uses the larger, smarter local model
/// only when enough memory is free to load it without swapping, otherwise the small one. If the large
/// model fails (for example it ran out of memory), the small one answers instead.
/// </summary>
public sealed class MemoryAwareLocalProvider : IAIProvider
{
    private readonly IAIProvider _large;
    private readonly IAIProvider _small;
    private readonly long _largeNeedsBytes;
    private readonly Func<long?> _freeBytes;

    public MemoryAwareLocalProvider(IAIProvider large, IAIProvider small, long largeNeedsBytes, Func<long?>? freeBytes = null)
    {
        _large = large;
        _small = small;
        _largeNeedsBytes = largeNeedsBytes;
        _freeBytes = freeBytes ?? AvailablePhysicalMemory;
    }

    public string Name => $"{_small.Name} (larger model when RAM allows)";

    public async Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        var free = _freeBytes();
        if (free is null || free < _largeNeedsBytes)
            return await _small.AskAsync(request, cancellationToken);
        try
        {
            return await _large.AskAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await _small.AskAsync(request, cancellationToken);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatus
    {
        public uint Length = 64;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

    public static long? AvailablePhysicalMemory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var status = new MemoryStatus();
        return GlobalMemoryStatusEx(status) ? (long)status.AvailablePhysical : null;
    }
}
