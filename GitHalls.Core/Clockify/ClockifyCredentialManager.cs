using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GitHalls.Core.Clockify;

public interface IClockifyCredentialManager
{
    string? GetApiKey();
    void SetApiKey(string apiKey);
    void ClearApiKey();
}

public class ClockifyCredentialManager : IClockifyCredentialManager
{
    private const string TargetName = "GitHalls:clockify";

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int reservedFlag);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    public string? GetApiKey()
    {
        if (CredRead(TargetName, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
        {
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                {
                    byte[] blob = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, blob, 0, cred.CredentialBlobSize);
                    return Encoding.Unicode.GetString(blob);
                }
            }
            finally
            {
                CredFree(credPtr);
            }
        }
        return null;
    }

    public void SetApiKey(string apiKey)
    {
        byte[] blob = Encoding.Unicode.GetBytes(apiKey);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "clockify_api"
            };
            CredWrite(ref cred, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public void ClearApiKey()
    {
        CredDelete(TargetName, CRED_TYPE_GENERIC, 0);
    }
}
