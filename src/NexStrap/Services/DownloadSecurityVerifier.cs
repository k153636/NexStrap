using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NexStrap.Services;

internal static class DownloadSecurityVerifier
{
    internal static void EnsureAllowedHttpsUrl(string url, params string[] allowedHosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Invalid download URL.");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only HTTPS download URLs are allowed.");

        if (allowedHosts.Length > 0 &&
            !allowedHosts.Any(h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Download host is not allowed: {uri.Host}");
    }

    internal static void VerifySha256(string filePath, string expectedSha256)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        var expected = expectedSha256.Trim().ToLowerInvariant();

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Downloaded file digest verification failed.");
    }

    internal static void VerifySignedExecutable(string filePath, params string[] allowedSubjects)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Downloaded executable not found.", filePath);

        using var trustData = new WINTRUST_DATA(filePath);
        var result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, trustData);
        if (result != 0)
            throw new InvalidOperationException($"Authenticode verification failed (0x{result:X8}).");

        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        if (!chain.Build(cert))
            throw new InvalidOperationException("Signer certificate chain validation failed.");

        if (allowedSubjects.Length > 0 &&
            !allowedSubjects.Any(s => cert.Subject.Contains(s, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Unexpected signer: {cert.Subject}");
    }

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        internal WINTRUST_FILE_INFO(string filePath)
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>();
            pcwszFilePath = filePath;
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }

        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WINTRUST_DATA : IDisposable
    {
        private readonly IntPtr _fileInfoPtr;

        internal WINTRUST_DATA(string filePath)
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>();
            dwUIChoice = 2;
            fdwRevocationChecks = 0;
            dwUnionChoice = 1;

            var fileInfo = new WINTRUST_FILE_INFO(filePath);
            _fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, _fileInfoPtr, false);
            pFile = _fileInfoPtr;

            dwStateAction = 0;
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = null;
            dwProvFlags = 0x00000040;
            dwUIContext = 0;
        }

        public uint cbStruct;
        public IntPtr pPolicyCallbackData = IntPtr.Zero;
        public IntPtr pSIPClientData = IntPtr.Zero;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;

        public void Dispose()
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(_fileInfoPtr);
            Marshal.FreeHGlobal(_fileInfoPtr);
        }
    }
}
