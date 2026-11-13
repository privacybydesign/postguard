using System.Runtime.InteropServices;
using E4A.PostGuard.Exceptions;

namespace E4A.PostGuard.Crypto;

internal static class Native
{
    private const string LibName = "pg_ffi";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pg_seal(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string mpkJson,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string policyJson,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pubSignKeyJson,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? privSignKeyJson,
        byte[] plaintext,
        nuint plaintextLen,
        out IntPtr output,
        out nuint outputLen);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void pg_free(IntPtr ptr, nuint len);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pg_last_error();

    private static string GetLastError()
    {
        var ptr = pg_last_error();
        return ptr == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringUTF8(ptr) ?? "unknown error";
    }

    public static byte[] Seal(
        string mpkJson,
        string policyJson,
        string pubSignKeyJson,
        string? privSignKeyJson,
        byte[] plaintext)
    {
        var result = pg_seal(
            mpkJson,
            policyJson,
            pubSignKeyJson,
            privSignKeyJson,
            plaintext,
            (nuint)plaintext.Length,
            out var outputPtr,
            out var outputLen);

        if (result != 0)
        {
            throw new SealException(GetLastError());
        }

        try
        {
            var output = new byte[(int)outputLen];
            Marshal.Copy(outputPtr, output, 0, (int)outputLen);
            return output;
        }
        finally
        {
            pg_free(outputPtr, outputLen);
        }
    }
}
