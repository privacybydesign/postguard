using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using E4A.PostGuard.Exceptions;
using E4A.PostGuard.Models;

namespace E4A.PostGuard.Api;

internal class CryptifyClient
{
    private const int ChunkSize = 1024 * 1024; // 1 MB

    private readonly HttpClient _http;
    private readonly string _cryptifyUrl;

    public CryptifyClient(HttpClient http, string cryptifyUrl)
    {
        _http = http;
        _cryptifyUrl = cryptifyUrl.TrimEnd('/');
    }

    public async Task<string> UploadAsync(
        byte[] sealedData,
        IReadOnlyList<RecipientBuilder> recipients,
        NotifyOptions? notify,
        CancellationToken ct = default)
    {
        var recipientEmails = string.Join(",", recipients.Select(r => r.Email));

        // Step 1: Initialize upload
        var (uuid, token) = await InitUploadAsync(recipientEmails, notify, ct);

        // Step 2: Upload chunks
        var offset = 0;
        while (offset < sealedData.Length)
        {
            var chunkLen = Math.Min(ChunkSize, sealedData.Length - offset);
            var end = offset + chunkLen;
            token = await StoreChunkAsync(uuid, token, sealedData, offset, end, ct);
            offset = end;
        }

        // Step 3: Finalize
        await FinalizeUploadAsync(uuid, token, sealedData.Length, ct);

        return uuid;
    }

    private async Task<(string Uuid, string Token)> InitUploadAsync(
        string recipientEmails, NotifyOptions? notify, CancellationToken ct)
    {
        var body = new
        {
            recipient = recipientEmails,
            mailContent = notify?.Message ?? "",
            mailLang = notify?.Language ?? "EN",
            // Maps to the existing wire-level `confirm` field. Default false —
            // the upload is silent unless the caller opts in via Notify.Sender.
            confirm = notify?.Sender ?? false,
            // Default false in the SDK overrides Cryptify's server-side default
            // of true so callers get a silent upload regardless of which Cryptify
            // version they're hitting. Older Cryptify deployments simply ignore
            // the field.
            notifyRecipients = notify?.Recipients ?? false,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_cryptifyUrl}/fileupload/init")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _http.SendAsync(request, ct);
        await response.EnsureSuccessAsync();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var uuid = doc.RootElement.GetProperty("uuid").GetString()
            ?? throw new PostGuardException("Missing uuid in init response");

        var token = GetCryptifyToken(response)
            ?? throw new PostGuardException("Missing cryptifytoken header in init response");

        return (uuid, token);
    }

    private async Task<string> StoreChunkAsync(
        string uuid, string token, byte[] data, int offset, int end, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{_cryptifyUrl}/fileupload/{uuid}")
        {
            Content = new ByteArrayContent(data, offset, end - offset)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // `end` is the exclusive end index of the chunk; RFC 9110 §14.4 range-end
        // is inclusive, so emit the last byte index (end - 1). `end > offset`
        // always holds here (chunkLen >= 1), so this never goes negative.
        request.Content.Headers.Add("Content-Range", $"bytes {offset}-{end - 1}/*");
        request.Headers.TryAddWithoutValidation("cryptifytoken", token);

        var response = await _http.SendAsync(request, ct);
        await response.EnsureSuccessAsync();

        return GetCryptifyToken(response)
            ?? throw new PostGuardException("Missing cryptifytoken header in chunk response");
    }

    private async Task FinalizeUploadAsync(
        string uuid, string token, int totalSize, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_cryptifyUrl}/fileupload/finalize/{uuid}")
        {
            Content = new ByteArrayContent([])
        };
        request.Content.Headers.Add("Content-Range", $"bytes */{totalSize}");
        request.Headers.TryAddWithoutValidation("cryptifytoken", token);

        var response = await _http.SendAsync(request, ct);
        await response.EnsureSuccessAsync();
    }

    private static string? GetCryptifyToken(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("cryptifytoken", out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
