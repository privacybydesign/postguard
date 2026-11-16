using System.Net;

namespace E4A.PostGuard.Tests.TestHelpers;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that records every outgoing request
/// (method, URI, headers and buffered body) and replies with a queued sequence
/// of responses. Lets the API-client tests exercise the full request/response
/// wiring without a live server.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly object _gate = new();

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Optional callback invoked just before each response is dequeued.</summary>
    public Action<RecordedRequest>? OnRequest { get; set; }

    public RecordingHttpMessageHandler Enqueue(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <summary>Enqueue a JSON 200 response, optionally with extra response headers.</summary>
    public RecordingHttpMessageHandler EnqueueJson(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return Enqueue(response);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now — HttpClient disposes the request after this returns.
        byte[]? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            HeaderValue(request, "cryptifytoken"),
            request.Headers.Authorization?.ToString(),
            ContentHeaderValue(request, "Content-Range"),
            body);

        HttpResponseMessage response;
        lock (_gate)
        {
            Requests.Add(recorded);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No queued response for {request.Method} {request.RequestUri}");
            }
            response = _responses.Dequeue();
        }

        OnRequest?.Invoke(recorded);
        response.RequestMessage = request;
        return response;
    }

    private static string? HeaderValue(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? ContentHeaderValue(HttpRequestMessage request, string name) =>
        request.Content != null && request.Content.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? CryptifyToken,
    string? Authorization,
    string? ContentRange,
    byte[]? Body)
{
    public string BodyText => Body == null ? "" : System.Text.Encoding.UTF8.GetString(Body);
}
