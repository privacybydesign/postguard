using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace E4A.PostGuard.Api;

internal class PkgClient
{
    private readonly HttpClient _http;
    private readonly string _pkgUrl;

    public PkgClient(HttpClient http, string pkgUrl)
    {
        _http = http;
        _pkgUrl = pkgUrl.TrimEnd('/');
    }

    /// <summary>
    /// Fetches the master public key from PKG.
    /// Returns the publicKey value as a JSON string (quoted base64).
    /// </summary>
    public async Task<string> FetchMpkJsonAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"{_pkgUrl}/v2/parameters", ct);
        await response.EnsureSuccessAsync();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var publicKey = doc.RootElement.GetProperty("publicKey");
        return JsonSerializer.Serialize(publicKey);
    }

    /// <summary>
    /// Fetches signing keys using an API key.
    /// Returns the raw JSON strings for pubSignKey and privSignKey.
    /// </summary>
    public async Task<(string PubSignKeyJson, string? PrivSignKeyJson)> FetchSigningKeysAsync(
        string apiKey, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_pkgUrl}/v2/irma/sign/key")
        {
            Content = new StringContent(
                """{"pubSignId":[{"t":"pbdf.sidn-pbdf.email.email"}]}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        await response.EnsureSuccessAsync();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var pubSignKey = root.GetProperty("pubSignKey");
        var pubSignKeyJson = JsonSerializer.Serialize(pubSignKey);

        string? privSignKeyJson = null;
        if (root.TryGetProperty("privSignKey", out var privSignKey) &&
            privSignKey.ValueKind != JsonValueKind.Null)
        {
            privSignKeyJson = JsonSerializer.Serialize(privSignKey);
        }

        return (pubSignKeyJson, privSignKeyJson);
    }
}
