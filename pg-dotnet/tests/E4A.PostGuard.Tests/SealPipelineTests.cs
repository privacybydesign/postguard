using System.Net;
using System.Text;
using E4A.PostGuard;
using E4A.PostGuard.Crypto;
using E4A.PostGuard.Models;
using E4A.PostGuard.Tests.TestHelpers;

namespace E4A.PostGuard.Tests;

public class SealPipelineTests
{
    private const string PkgUrl = "https://pkg.postguard.eu";
    private const string CryptifyUrl = "https://cryptify.postguard.eu";

    private static PostGuardConfig Config() => new()
    {
        PkgUrl = PkgUrl,
        CryptifyUrl = CryptifyUrl,
    };

    private static EncryptInput Input(SignMethod sign) => new()
    {
        Files = [new PgFile("a.txt", new MemoryStream(Encoding.UTF8.GetBytes("x")))],
        Recipients = [new RecipientBuilder("a@b.com", RecipientBaseType.Email)],
        Sign = sign,
    };

    /// <summary>A non-ApiKey signing method, to exercise the guard clause.</summary>
    private sealed class UnsupportedSign : SignMethod
    {
    }

    [Fact]
    public async Task SealAsync_NonApiKeySign_ThrowsArgumentException()
    {
        using var http = new HttpClient(new RecordingHttpMessageHandler());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SealPipeline.SealAsync(Config(), http, Input(new UnsupportedSign())));
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public async Task SealAsync_KeyFetchFailure_PropagatesNetworkException()
    {
        // Both PKG calls fire in parallel before any native seal; a PKG failure
        // must surface (here: a 500 on the parameters/sign-key fetch) rather than
        // proceeding to the native layer.
        var handler = new RecordingHttpMessageHandler();
        handler
            .EnqueueJson("boom", HttpStatusCode.InternalServerError)
            .EnqueueJson("boom", HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<E4A.PostGuard.Exceptions.NetworkException>(
            () => SealPipeline.SealAsync(Config(), http, Input(new ApiKeySign("key"))));

        // The MPK + signing-key fetches both went out (parallel key-fetch wiring).
        Assert.Contains(handler.Requests, r => r.Uri.ToString().EndsWith("/v2/parameters"));
        Assert.Contains(handler.Requests, r => r.Uri.ToString().EndsWith("/v2/irma/sign/key"));
    }
}
