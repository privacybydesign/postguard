using E4A.PostGuard.Crypto;
using E4A.PostGuard.Exceptions;

namespace E4A.PostGuard.Tests;

/// <summary>
/// The only test that crosses the P/Invoke boundary. The rest of the suite stops at the HTTP
/// layer, so without this a <c>pg-ffi</c> that no longer loads, or whose exports no longer match
/// <see cref="Native"/>, would pass every test and fail in the consumer's process.
///
/// It needs the native library for the host platform in <c>src/runtimes/</c>. That folder is not
/// committed: run <c>pg-ffi/build.sh</c> first, which is what CI does. A missing library fails
/// this test instead of skipping it, because a skip in CI would hide exactly the breakage the
/// test exists for.
/// </summary>
public class NativeLibraryTests
{
    [Fact]
    public void Seal_ReachesTheNativeLibrary_AndSurfacesItsError()
    {
        // Not valid key material, so pg_seal returns non-zero and the message comes back
        // through pg_last_error. That exercises two of the three exports and the error path;
        // pg_free is only reachable with real keys from a PKG.
        SealException ex;
        try
        {
            ex = Assert.Throws<SealException>(() => Native.Seal("{}", "{}", "{}", null, [1, 2, 3]));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Assert.Fail(
                "the pg-ffi native library did not load. Build it for this platform with "
                    + $"`pg-ffi/build.sh`, which copies it to pg-dotnet/src/runtimes/. ({e.GetType().Name}: {e.Message})");
            throw;
        }

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.NotEqual("unknown error", ex.Message);
    }
}
