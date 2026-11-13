# Securely save Dhan credentials in the current Windows user's Credential Manager.
# The access token is collected as SecureString; no .env/project file is written.
# Run from the repository root:
#   .\scripts\Set-DhanCredentials.ps1

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $env:OS -ne "Windows_NT") {
    throw "Windows Credential Manager is available only on Windows."
}

$clientId = Read-Host "Dhan Client ID"
if ([string]::IsNullOrWhiteSpace($clientId) -or $clientId -notmatch '^\d{1,24}$') {
    throw "The Dhan Client ID must contain 1 to 24 digits."
}
$accessToken = Read-Host "Dhan Access Token" -AsSecureString
if ($accessToken.Length -lt 1 -or $accessToken.Length -gt 2500) {
    $accessToken.Dispose()
    throw "The access token length is invalid."
}

$source = @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class JarvisDhanCredentialWriter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public UInt32 Flags;
        public UInt32 Type;
        [MarshalAs(UnmanagedType.LPWStr)] public String TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public UInt32 CredentialBlobSize;
        public IntPtr CredentialBlob;
        public UInt32 Persist;
        public UInt32 AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public String UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, UInt32 flags);

    public static void WriteText(String target, String userName, String value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        try { WriteBlob(target, userName, bytes); }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    public static void WriteSecure(String target, String userName, IntPtr secureString, Int32 characterCount)
    {
        var bytes = new byte[checked(characterCount * 2)];
        try
        {
            Marshal.Copy(secureString, bytes, 0, bytes.Length);
            WriteBlob(target, userName, bytes);
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    private static void WriteBlob(String target, String userName, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 5120)
            throw new ArgumentException("Credential must contain 1 to 2,560 UTF-16 characters.");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = 1,
                TargetName = target,
                UserName = userName,
                CredentialBlobSize = (UInt32)bytes.Length,
                CredentialBlob = buffer,
                Persist = 2
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not store the credential.");
        }
        finally
        {
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.FreeHGlobal(buffer);
        }
    }
}
"@

try {
    Add-Type -TypeDefinition $source -Language CSharp
    $tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($accessToken)
    try {
        [JarvisDhanCredentialWriter]::WriteText("JARVIS:Dhan:ClientId", $clientId, $clientId)
        [JarvisDhanCredentialWriter]::WriteSecure("JARVIS:Dhan:AccessToken", $clientId, $tokenPointer, $accessToken.Length)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($tokenPointer)
    }
}
finally {
    $accessToken.Dispose()
    $clientId = $null
}

Write-Host "Dhan credentials saved to Windows Credential Manager for the current Windows user."
Write-Host "JARVIS will read them automatically on its next start. Do not share the access token."
