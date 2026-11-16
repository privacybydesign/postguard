using E4A.PostGuard.Models;

namespace E4A.PostGuard;

public class PostGuard : IDisposable
{
    private readonly PostGuardConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private bool _disposed;

    public PostGuard(PostGuardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;

        if (config.HttpClient is not null)
        {
            _http = config.HttpClient;
            _ownsHttp = false;
        }
        else
        {
            // SocketsHttpHandler with a bounded PooledConnectionLifetime so a
            // long-lived client still picks up DNS changes — recommended pattern
            // for singleton HttpClient. See:
            // https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            _http = new HttpClient(handler, disposeHandler: true);
            if (config.Timeout is { } timeout)
            {
                _http.Timeout = timeout;
            }
            if (config.Headers is not null)
            {
                foreach (var (key, value) in config.Headers)
                {
                    _http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }
            // Identify this SDK + version to the PKG and Cryptify on every
            // request. Skipped when the caller already set the header (they win).
            // Only for the SDK-owned client — a caller-supplied HttpClient may
            // be shared, so we never mutate its default headers.
            ClientVersion.ApplyTo(_http, config.Headers);
            _ownsHttp = true;
        }
    }

    /// <summary>
    /// Builder for signing methods.
    /// </summary>
    public SignBuilders Sign { get; } = new();

    /// <summary>
    /// Builder for recipients.
    /// </summary>
    public RecipientBuilders Recipient { get; } = new();

    /// <summary>
    /// Create a lazy encryption builder. No work is done until a terminal
    /// method (<see cref="Sealed.UploadAsync"/> or <see cref="Sealed.ToBytesAsync"/>)
    /// is called.
    /// </summary>
    public Sealed Encrypt(EncryptInput input)
    {
        return new Sealed(_config, _http, input);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttp)
        {
            _http.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

public class SignBuilders
{
    public ApiKeySign ApiKey(string apiKey) => new(apiKey);
}

public class RecipientBuilders
{
    public RecipientBuilder Email(string email) => new(email, RecipientBaseType.Email);
    public RecipientBuilder EmailDomain(string email) => new(email, RecipientBaseType.EmailDomain);
}
