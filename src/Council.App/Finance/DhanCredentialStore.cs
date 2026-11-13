using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Council.App.Finance;

/// <summary>
/// Reads Dhan credentials from the current Windows user's Credential Manager.
/// Environment variables remain a backwards-compatible fallback.
/// </summary>
internal static class DhanCredentialStore
{
    private const uint GenericCredential = 1;

    public static string? ReadClientId() =>
        ReadCredential("JARVIS:Dhan:ClientId") ??
        Environment.GetEnvironmentVariable("DHAN_CLIENT_ID");

    public static string? ReadAccessToken() =>
        ReadCredential("JARVIS:Dhan:AccessToken") ??
        Environment.GetEnvironmentVariable("DHAN_ACCESS_TOKEN");

    private static string? ReadCredential(string target)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        IntPtr credentialPointer = IntPtr.Zero;
        try
        {
            if (!CredRead(target, GenericCredential, 0, out credentialPointer) ||
                credentialPointer == IntPtr.Zero)
                return null;

            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize is 0 or > 5120 ||
                credential.CredentialBlobSize % 2 != 0)
                return null;

            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                var value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            if (credentialPointer != IntPtr.Zero)
                CredFree(credentialPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);
}
