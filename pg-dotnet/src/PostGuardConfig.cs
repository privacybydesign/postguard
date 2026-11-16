namespace E4A.PostGuard;

public class PostGuardConfig
{
    public required string PkgUrl { get; init; }
    public required string CryptifyUrl { get; init; }

    /// <summary>
    /// Extra headers added to every PKG and Cryptify request (only when the SDK
    /// owns the <see cref="HttpClient"/> — see <see cref="HttpClient"/>). The SDK
    /// also sends an <c>X-POSTGUARD-CLIENT-VERSION</c> header automatically;
    /// supply that key here (any casing) to override it (e.g. an embedding host
    /// identifying itself).
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Opt-in escape hatch to allow non-https URLs (e.g. http://localhost) for
    /// local development and testing. Defaults to false: the SDK refuses to
    /// send API keys and signing keys over plaintext connections.
    /// </summary>
    public bool AllowInsecureUrls { get; init; }

    /// <summary>
    /// Optional caller-supplied <see cref="System.Net.Http.HttpClient"/>. When
    /// set, the SDK reuses this client for all PKG and Cryptify calls and does
    /// NOT dispose it — ownership stays with the caller (DI-friendly). The SDK
    /// also does NOT mutate its headers, so it will not add
    /// <c>X-POSTGUARD-CLIENT-VERSION</c>; set that yourself if you want it. When
    /// null, <see cref="PostGuard"/> creates and owns a single long-lived client
    /// (and sends <c>X-POSTGUARD-CLIENT-VERSION</c> automatically).
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// Request timeout applied to the SDK-owned <see cref="System.Net.Http.HttpClient"/>.
    /// Ignored when <see cref="HttpClient"/> is supplied (the caller owns the
    /// timeout in that case). Defaults to <see cref="System.Net.Http.HttpClient"/>'s
    /// own default of 100 seconds when null.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    internal void Validate()
    {
        if (AllowInsecureUrls)
        {
            return;
        }

        RequireHttps(PkgUrl, nameof(PkgUrl));
        RequireHttps(CryptifyUrl, nameof(CryptifyUrl));
    }

    private static void RequireHttps(string url, string name)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"{name} must be an absolute https:// URL (got '{url}'). " +
                "Set AllowInsecureUrls = true to opt in to plaintext URLs for local testing.",
                name);
        }
    }
}
