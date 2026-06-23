using System.Runtime.InteropServices;

namespace RdpManager.Services;

/// <summary>
/// Windows 資格情報マネージャーへ直接書き込む（CredWrite API）。
/// cmdkey.exe を起動しないため、パスワードがコマンドライン引数に露出しない。
/// </summary>
public static class CredentialManager
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_SESSION = 1; // ログオフで自動消去

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>資格情報マネージャーから TERMSRV/&lt;host&gt; の資格情報を読み出す。無ければ null。</summary>
    public static (string user, string password)? ReadTerminalServer(string host)
    {
        if (!CredRead($"TERMSRV/{host}", CRED_TYPE_GENERIC, 0, out var ptr))
            return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            string user = cred.UserName ?? "";
            string pass = "";
            if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                pass = Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2) ?? "";
            return (user, pass);
        }
        finally { CredFree(ptr); }
    }

    /// <summary>TERMSRV/&lt;host&gt; 形式の汎用資格情報を書き込む。</summary>
    public static bool WriteTerminalServer(string host, string user, string password)
    {
        var target = $"TERMSRV/{host}";
        var blob = IntPtr.Zero;
        try
        {
            var bytes = System.Text.Encoding.Unicode.GetBytes(password);
            blob = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_SESSION,
                UserName = user
            };
            return CredWrite(ref cred, 0);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (blob != IntPtr.Zero) Marshal.FreeCoTaskMem(blob);
        }
    }

    public static void DeleteTerminalServer(string host)
    {
        try { CredDelete($"TERMSRV/{host}", CRED_TYPE_GENERIC, 0); } catch { /* 無い場合は無視 */ }
    }
}
