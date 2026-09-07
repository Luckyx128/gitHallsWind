using System.Runtime.InteropServices;
using System.Text;

namespace GitHalls.App.Services;

/// <summary>
/// Secrets in the Windows Credential Manager — the OS store, the same place git
/// itself keeps credentials, and what the Keychain is on the Mac.
///
/// Nothing secret ever reaches settings.json: that file holds the site and the
/// email, and this holds the token. The user can see and revoke the entry in
/// Windows' own Credential Manager, which they cannot do with a file we invent.
/// </summary>
public static class CredentialStore
{
    private const int CredentialTypeGeneric = 1;

    /// <summary>Kept on this machine, for this user; never roamed to another.</summary>
    private const int PersistLocalMachine = 2;

    public static string? Read(string target)
    {
        if (!CredRead(target, CredentialTypeGeneric, 0, out var handle)) return null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;

            // The blob is UTF-16 and not NUL-terminated, so the length is bytes / 2.
            return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public static void Save(string target, string userName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobHandle = Marshal.AllocCoTaskMem(blob.Length);
        var targetHandle = Marshal.StringToCoTaskMemUni(target);
        var userHandle = Marshal.StringToCoTaskMemUni(userName);

        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetHandle,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobHandle,
                Persist = PersistLocalMachine,
                UserName = userHandle
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Windows Credential Manager refused to store the secret (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            // The blob held a secret; don't leave it in freed-but-unwritten
            // memory. Zeroed by length, not with ZeroFreeCoTaskMemUnicode —
            // that one scans for a NUL terminator this buffer doesn't have.
            Array.Clear(blob);
            for (var i = 0; i < blob.Length; i++) Marshal.WriteByte(blobHandle, i, 0);
            Marshal.FreeCoTaskMem(blobHandle);
            Marshal.FreeCoTaskMem(targetHandle);
            Marshal.FreeCoTaskMem(userHandle);
        }
    }

    /// <summary>Removes the entry. A target that isn't there is not a failure.</summary>
    public static void Delete(string target) => CredDelete(target, CredentialTypeGeneric, 0);

    // MARK: - advapi32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
