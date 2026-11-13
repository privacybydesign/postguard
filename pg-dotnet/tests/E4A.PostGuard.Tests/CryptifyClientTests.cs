using System.Net;
using E4A.PostGuard.Api;
using E4A.PostGuard.Exceptions;
using E4A.PostGuard.Models;
using E4A.PostGuard.Tests.TestHelpers;

namespace E4A.PostGuard.Tests;

public class CryptifyClientTests
{
    private const string BaseUrl = "https://cryptify.postguard.eu";
    private const int ChunkSize = 1024 * 1024; // mirrors CryptifyClient.ChunkSize

    private static readonly Dictionary<string, string> InitToken =
        new() { ["cryptifytoken"] = "token-0" };

    private static RecipientBuilder Email(string email) =>
        new(email, RecipientBaseType.Email);

    private static (CryptifyClient Client, RecordingHttpMessageHandler Handler) NewClient()
    {
        var handler = new RecordingHttpMessageHandler();
        return (new CryptifyClient(new HttpClient(handler), BaseUrl), handler);
    }

    private static HttpResponseMessage TokenResponse(string token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(""),
        };
        response.Headers.TryAddWithoutValidation("cryptifytoken", token);
        return response;
    }

    [Fact]
    public async Task SingleChunk_WiresInitChunkFinalize()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"abc-123"}""", headers: InitToken)
            .Enqueue(TokenResponse("token-1"))
            .Enqueue(TokenResponse("token-2"));

        var data = new byte[100];
        var uuid = await client.UploadAsync(data, [Email("alice@example.com")], notify: null);

        Assert.Equal("abc-123", uuid);
        Assert.Equal(3, handler.Requests.Count);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"{BaseUrl}/fileupload/init", handler.Requests[0].Uri.ToString());

        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal($"{BaseUrl}/fileupload/abc-123", handler.Requests[1].Uri.ToString());

        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal($"{BaseUrl}/fileupload/finalize/abc-123", handler.Requests[2].Uri.ToString());
    }

    [Fact]
    public async Task ChunkContentRange_IsFormattedPerChunk()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"))
            .Enqueue(TokenResponse("t3"));

        // 1.5 chunks → two PUTs.
        var total = ChunkSize + 100;
        var uuid = await client.UploadAsync(new byte[total], [Email("a@b.com")], notify: null);
        Assert.Equal("u", uuid);

        var puts = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        Assert.Equal(2, puts.Count);

        // RFC 9110 §14.4: the range-end is inclusive (the last byte index of the
        // chunk), so a chunk covering bytes [offset, end) is reported as
        // `offset-(end-1)`. See encryption4all/postguard-dotnet#34.
        Assert.Equal($"bytes 0-{ChunkSize - 1}/*", puts[0].ContentRange);
        Assert.Equal($"bytes {ChunkSize}-{total - 1}/*", puts[1].ContentRange);
    }

    [Fact]
    public async Task Finalize_SendsTotalSizeContentRange()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"));

        var total = 4242;
        await client.UploadAsync(new byte[total], [Email("a@b.com")], notify: null);

        var finalize = handler.Requests.Single(r => r.Uri.ToString().Contains("/finalize/"));
        Assert.Equal($"bytes */{total}", finalize.ContentRange);
    }

    [Fact]
    public async Task TokenRotates_AcrossChunksAndFinalize()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken) // init -> token-0
            .Enqueue(TokenResponse("token-1"))                   // chunk 1 -> token-1
            .Enqueue(TokenResponse("token-2"))                   // chunk 2 -> token-2
            .Enqueue(TokenResponse("token-3"));                  // finalize ok

        await client.UploadAsync(new byte[ChunkSize + 1], [Email("a@b.com")], notify: null);

        // init carries no token; each subsequent request carries the token from
        // the previous response.
        Assert.Null(handler.Requests[0].CryptifyToken);
        Assert.Equal("token-0", handler.Requests[1].CryptifyToken); // first chunk uses init token
        Assert.Equal("token-1", handler.Requests[2].CryptifyToken); // second chunk uses chunk-1 token
        Assert.Equal("token-2", handler.Requests[3].CryptifyToken); // finalize uses last chunk token
    }

    [Fact]
    public async Task Init_JoinsRecipientEmailsWithComma()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"));

        await client.UploadAsync(
            new byte[10],
            [Email("alice@example.com"), Email("bob@example.com")],
            notify: null);

        Assert.Contains("alice@example.com,bob@example.com", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task Notify_DefaultsToSilentUpload()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"));

        await client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null);

        var body = handler.Requests[0].BodyText;
        Assert.Contains("\"confirm\":false", body);
        Assert.Contains("\"notifyRecipients\":false", body);
        Assert.Contains("\"mailLang\":\"EN\"", body);
    }

    [Fact]
    public async Task Notify_PropagatesOptionsToInitBody()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"));

        var notify = new NotifyOptions
        {
            Recipients = true,
            Sender = true,
            Message = "hello",
            Language = "NL",
        };
        await client.UploadAsync(new byte[10], [Email("a@b.com")], notify);

        var body = handler.Requests[0].BodyText;
        Assert.Contains("\"confirm\":true", body);
        Assert.Contains("\"notifyRecipients\":true", body);
        Assert.Contains("\"mailContent\":\"hello\"", body);
        Assert.Contains("\"mailLang\":\"NL\"", body);
    }

    [Fact]
    public async Task NullUuid_ThrowsPostGuardException()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"uuid":null}""", headers: InitToken);

        var ex = await Assert.ThrowsAsync<PostGuardException>(
            () => client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null));
        Assert.Contains("uuid", ex.Message);
    }

    [Fact]
    public async Task MissingInitToken_ThrowsPostGuardException()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("""{"uuid":"u"}"""); // no cryptifytoken header

        var ex = await Assert.ThrowsAsync<PostGuardException>(
            () => client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null));
        Assert.Contains("cryptifytoken", ex.Message);
    }

    [Fact]
    public async Task MissingChunkToken_ThrowsPostGuardException()
    {
        var (client, handler) = NewClient();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); // no token

        var ex = await Assert.ThrowsAsync<PostGuardException>(
            () => client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null));
        Assert.Contains("cryptifytoken", ex.Message);
    }

    [Fact]
    public async Task NonSuccessInit_ThrowsNetworkExceptionWithUrl()
    {
        var (client, handler) = NewClient();
        handler.EnqueueJson("nope", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<NetworkException>(
            () => client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null));
        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("/fileupload/init", ex.Url);
    }

    [Fact]
    public async Task Ctor_TrimsTrailingSlashFromUrl()
    {
        var handler = new RecordingHttpMessageHandler();
        handler
            .EnqueueJson("""{"uuid":"u"}""", headers: InitToken)
            .Enqueue(TokenResponse("t1"))
            .Enqueue(TokenResponse("t2"));
        var client = new CryptifyClient(new HttpClient(handler), BaseUrl + "/");

        await client.UploadAsync(new byte[10], [Email("a@b.com")], notify: null);

        // No double slash in the init URL.
        Assert.Equal($"{BaseUrl}/fileupload/init", handler.Requests[0].Uri.ToString());
    }
}
