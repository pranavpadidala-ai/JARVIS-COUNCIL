using System.Security.Cryptography;
using System.Text;

namespace Council.Security;

/// <summary>
/// Encrypts local conversation-memory content with Windows DPAPI for the
/// current Windows user. Existing plaintext records remain readable for
/// backwards compatibility and are encrypted on the next write only.
/// </summary>
public static class LocalMemoryProtector
{
    private const string Prefix = "dpapi:v1:";
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("JARVIS-Council.LocalMemory.v1");

    public static string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Encrypted JARVIS memory currently requires Windows DPAPI.");

        var clearBytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(
            clearBytes, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string storedValue)
    {
        ArgumentNullException.ThrowIfNull(storedValue);
        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return storedValue; // Legacy plaintext record.

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "This JARVIS memory file is protected for a Windows user.");

        var protectedBytes = Convert.FromBase64String(storedValue[Prefix.Length..]);
        var clearBytes = ProtectedData.Unprotect(
            protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }
}
