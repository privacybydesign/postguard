using System.Net;
using System.Text.Json;
using E4A.PostGuard.Api;
using E4A.PostGuard.Exceptions;
using E4A.PostGuard.Tests.TestHelpers;

namespace E4A.PostGuard.Tests;

public class PkgClientTests
{
    private const string BaseUrl = "https://pkg.postguard.eu";

    private static (PkgClient Client, RecordingHttpMessageHandler Handler) NewClient()
    {
        var handler = new RecordingHttpMessageHandler();
        return (new PkgClient(new HttpClient(handler), BaseUrl), handler);
    }

    [Fact]
    public async Task FetchMpk_GetsParametersEndpoint()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"publicKey":"AAEC"}""");

        await client.FetchMpkJsonAsync();

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal($"{BaseUrl}/v2/parameters", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task FetchMpk_ReturnsSerializedStringValue()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"publicKey":"AAEC","ignored":42}""");

        var mpk = await client.FetchMpkJsonAsync();

        // A quoted base64 string is returned verbatim as a JSON string literal.
        Assert.Equal("\"AAEC\"", mpk);
    }

    [Fact]
    public async Task FetchMpk_ReturnsSerializedObjectValue()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"publicKey":{"alg":"kem","key":"AAEC"}}""");

        var mpk = await client.FetchMpkJsonAsync();

        // The publicKey sub-object is preserved as valid JSON.
        using var doc = JsonDocument.Parse(mpk);
        Assert.Equal("kem", doc.RootElement.GetProperty("alg").GetString());
        Assert.Equal("AAEC", doc.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public async Task FetchMpk_MissingPublicKey_Throws()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"somethingElse":"x"}""");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => client.FetchMpkJsonAsync());
    }

    [Fact]
    public async Task FetchMpk_NonSuccess_ThrowsNetworkException()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("server error", HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<NetworkException>(() => client.FetchMpkJsonAsync());
        Assert.Equal(502, ex.StatusCode);
        Assert.Contains("/v2/parameters", ex.Url);
    }

    [Fact]
    public async Task FetchSigningKeys_PostsWithBearerAuthAndBody()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"pubSignKey":{"k":"pub"}}""");

        await client.FetchSigningKeysAsync("my-api-key");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal($"{BaseUrl}/v2/irma/sign/key", req.Uri.ToString());
        Assert.Equal("Bearer my-api-key", req.Authorization);
        Assert.Contains("pbdf.sidn-pbdf.email.email", req.BodyText);
    }

    [Fact]
    public async Task FetchSigningKeys_ReturnsPubAndPriv()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"pubSignKey":{"k":"pub"},"privSignKey":{"k":"priv"}}""");

        var (pub, priv) = await client.FetchSigningKeysAsync("key");

        using var pubDoc = JsonDocument.Parse(pub);
        Assert.Equal("pub", pubDoc.RootElement.GetProperty("k").GetString());

        Assert.NotNull(priv);
        using var privDoc = JsonDocument.Parse(priv!);
        Assert.Equal("priv", privDoc.RootElement.GetProperty("k").GetString());
    }

    [Fact]
    public async Task FetchSigningKeys_AbsentPrivKey_ReturnsNull()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"pubSignKey":{"k":"pub"}}""");

        var (pub, priv) = await client.FetchSigningKeysAsync("key");

        Assert.NotNull(pub);
        Assert.Null(priv);
    }

    [Fact]
    public async Task FetchSigningKeys_NullPrivKey_ReturnsNull()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"pubSignKey":{"k":"pub"},"privSignKey":null}""");

        var (_, priv) = await client.FetchSigningKeysAsync("key");

        Assert.Null(priv);
    }

    [Fact]
    public async Task FetchSigningKeys_MissingPubKey_Throws()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"privSignKey":{"k":"priv"}}""");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => client.FetchSigningKeysAsync("key"));
    }

    [Fact]
    public async Task FetchSigningKeys_NonSuccess_ThrowsNetworkException()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("unauthorized", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<NetworkException>(
            () => client.FetchSigningKeysAsync("bad-key"));
        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("/v2/irma/sign/key", ex.Url);
    }
}
