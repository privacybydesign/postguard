using E4A.PostGuard.Exceptions;

namespace E4A.PostGuard.Api;

internal static class HttpResponseExtensions
{
    /// <summary>
    /// Throws a <see cref="NetworkException"/> when the response is not a success
    /// status code, capturing the status, upstream body and request URL.
    /// </summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var url = response.RequestMessage?.RequestUri?.ToString() ?? "<unknown>";
            throw new NetworkException((int)response.StatusCode, body, url);
        }
    }
}
