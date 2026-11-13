using System.Reflection;
using System.Runtime.InteropServices;

namespace E4A.PostGuard;

/// <summary>
/// Builds and applies the <c>X-POSTGUARD-CLIENT-VERSION</c> header so the PKG
/// and Cryptify servers can attribute requests to this SDK and version. The
/// value format (<c>host,host_version,app,app_version</c>) is shared with the
/// other PostGuard clients (e.g. the Outlook add-in sends
/// <c>Outlook,1.0,pg4ol,…</c>); this SDK sends
/// <c>dotnet,&lt;framework&gt;,pg-dotnet,&lt;version&gt;</c>.
/// </summary>
internal static class ClientVersion
{
    internal const string HeaderName = "X-POSTGUARD-CLIENT-VERSION";

    private const string App = "pg-dotnet";

    /// <summary>The computed <c>host,host_version,app,app_version</c> value (computed once).</summary>
    internal static readonly string HeaderValue = BuildHeaderValue();

    private static string BuildHeaderValue()
    {
        var host = "dotnet";
        var hostVersion = Sanitize(RuntimeInformation.FrameworkDescription); // e.g. ".NET 8.0.7"
        var appVersion = Sanitize(ReadAssemblyVersion());
        return $"{host},{hostVersion},{App},{appVersion}";
    }

    /// <summary>
    /// Read this SDK assembly's version. Prefers
    /// <see cref="AssemblyInformationalVersionAttribute"/> (carries the full
    /// semver from the csproj &lt;Version&gt;, which is pg-core's version),
    /// stripping any <c>+&lt;gitHash&gt;</c> suffix the SDK may append. Falls
    /// back to the 3-part assembly version, then "unknown".
    /// </summary>
    private static string ReadAssemblyVersion()
    {
        var asm = typeof(ClientVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "unknown";
    }

    /// <summary>
    /// The value is comma-delimited and the servers split on ',' expecting
    /// exactly four fields — never let a comma (or CR/LF) inside a field break
    /// the format or inject a header. Empty/blank collapses to "unknown".
    /// </summary>
    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "unknown";
        }
        return s.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    /// <summary>
    /// Add the client-version header to <paramref name="http"/>'s default
    /// request headers, unless the caller already supplied that header (any
    /// casing) via <paramref name="callerHeaders"/> — in which case the
    /// caller's value wins (e.g. an embedding host identifying itself).
    /// </summary>
    internal static void ApplyTo(HttpClient http, IDictionary<string, string>? callerHeaders)
    {
        var callerSetIt = callerHeaders is not null &&
            callerHeaders.Keys.Any(k => string.Equals(k, HeaderName, StringComparison.OrdinalIgnoreCase));
        if (!callerSetIt)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(HeaderName, HeaderValue);
        }
    }
}
