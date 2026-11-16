namespace E4A.PostGuard.Exceptions;

public class PostGuardException : Exception
{
    public PostGuardException(string message) : base(message) { }
    public PostGuardException(string message, Exception inner) : base(message, inner) { }
}

public class NetworkException : PostGuardException
{
    // Raw upstream response body, retained for diagnostics only. Kept private
    // so it is never surfaced in the exception message or via a public property,
    // where it could leak sensitive upstream data into logs. See issue #41.
    private readonly string _body;

    public int StatusCode { get; }
    public string Url { get; }

    public NetworkException(int statusCode, string body, string url)
        : base($"HTTP {statusCode} received from upstream service")
    {
        StatusCode = statusCode;
        Url = url;
        _body = body;
    }
}

public class SealException : PostGuardException
{
    public SealException(string message) : base(message) { }
}
