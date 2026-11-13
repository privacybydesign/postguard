using System.Net;
using System.Net.Http.Json;
using E4A.PostGuard.Api;
using E4A.PostGuard.Models;

namespace E4A.PostGuard.Tests;

public class CryptifyContentRangeTests
{
    private const int ChunkSize = 1024 * 1024; // mirrors CryptifyClient.ChunkSize

    /// <summary>
    /// Records the Content-Range header of every chunk PUT and replies with the
    /// canned init/chunk/finalize responses the upload flow expects.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> ChunkRanges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;

            if (path.EndsWith("/fileupload/init"))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { uuid = "test-uuid" })
                };
            }
            else if (path.Contains("/fileupload/finalize/"))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("")
                };
            }
            else // chunk PUT: /fileupload/{uuid}
            {
                // Read the body so the content stream is fully consumed, then
                // capture the Content-Range header for assertions.
                _ = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                ChunkRanges.Add(request.Content!.Headers.GetValues("Content-Range").Single());
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("")
                };
            }

            // Every init/chunk response must carry a rolling cryptifytoken.
            response.Headers.Add("cryptifytoken", "tok");
            return response;
        }
    }

    private static IReadOnlyList<RecipientBuilder> Recipient() =>
        [new RecipientBuilder("alice@example.com", RecipientBaseType.Email)];

    [Fact]
    public async Task StoreChunk_EmitsInclusiveEndByte()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new CryptifyClient(http, "https://cryptify.example/");

        // Single 5-byte chunk: bytes 0..4 inclusive.
        await client.UploadAsync(new byte[] { 1, 2, 3, 4, 5 }, Recipient(), null);

        // Not "bytes 0-5/*": RFC 9110 §14.4 range-end is inclusive, so the last
        // byte of a 5-byte chunk is index 4.
        Assert.Equal("bytes 0-4/*", Assert.Single(handler.ChunkRanges));
    }

    [Fact]
    public async Task StoreChunk_ConsecutiveChunksDoNotOverlap()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new CryptifyClient(http, "https://cryptify.example/");

        // ChunkSize + 1 bytes forces a full first chunk plus a 1-byte second chunk.
        await client.UploadAsync(new byte[ChunkSize + 1], Recipient(), null);

        Assert.Equal(2, handler.ChunkRanges.Count);
        // First chunk: inclusive end is ChunkSize - 1, not ChunkSize.
        Assert.Equal($"bytes 0-{ChunkSize - 1}/*", handler.ChunkRanges[0]);
        // Second chunk starts at ChunkSize — one past the first chunk's inclusive
        // end, so the ranges are contiguous with no overlapping byte.
        Assert.Equal($"bytes {ChunkSize}-{ChunkSize}/*", handler.ChunkRanges[1]);
    }
}
