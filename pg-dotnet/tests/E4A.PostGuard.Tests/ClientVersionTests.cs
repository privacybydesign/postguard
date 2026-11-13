using System.Net;

namespace E4A.PostGuard.Tests;

public class ClientVersionTests
{
    [Fact]
    public void HeaderValue_HasFourCommaFields()
    {
        var fields = ClientVersion.HeaderValue.Split(',');
        Assert.Equal(4, fields.Length);
    }

    [Fact]
    public void HeaderValue_HostIsDotnet_AppIsPgDotnet()
    {
        var fields = ClientVersion.HeaderValue.Split(',');
        Assert.Equal("dotnet", fields[0]);
        Assert.Equal("pg-dotnet", fields[2]);
    }

    [Fact]
    public void HeaderValue_HostVersionAndAppVersionAreClean()
    {
        var fields = ClientVersion.HeaderValue.Split(',');
        var hostVersion = fields[1];
        var appVersion = fields[3];

        Assert.False(string.IsNullOrWhiteSpace(hostVersion));
        Assert.False(string.IsNullOrWhiteSpace(appVersion));
        // app_version must not carry a "+<gitHash>" suffix or a stray comma.
        Assert.DoesNotContain('+', appVersion);
        Assert.DoesNotContain(',', appVersion);
    }

    [Fact]
    public void ApplyTo_AddsHeader_WhenCallerSuppliedNone()
    {
        using var http = new HttpClient();
        ClientVersion.ApplyTo(http, null);

        Assert.True(http.DefaultRequestHeaders.TryGetValues(ClientVersion.HeaderName, out var values));
        Assert.Equal(ClientVersion.HeaderValue, Assert.Single(values!));
    }

    [Fact]
    public void ApplyTo_DoesNotAdd_WhenCallerSuppliedExactCase()
    {
        using var http = new HttpClient();
        var headers = new Dictionary<string, string> { [ClientVersion.HeaderName] = "Outlook,1.0,pg4ol,9.9.9" };

        ClientVersion.ApplyTo(http, headers);

        // ApplyTo runs after the caller's own header loop in PostGuard; here we
        // assert it does not add a second value of its own.
        Assert.False(http.DefaultRequestHeaders.TryGetValues(ClientVersion.HeaderName, out _));
    }

    [Fact]
    public void ApplyTo_DoesNotAdd_WhenCallerSuppliedDifferentCasing()
    {
        using var http = new HttpClient();
        var headers = new Dictionary<string, string> { ["x-postguard-client-version"] = "Outlook,1.0,pg4ol,9.9.9" };

        ClientVersion.ApplyTo(http, headers);

        Assert.False(http.DefaultRequestHeaders.TryGetValues(ClientVersion.HeaderName, out _));
    }

    [Fact]
    public void ByoHttpClient_IsNotMutated()
    {
        // A caller-supplied HttpClient is used as-is; the SDK must not add the
        // client-version header to it (it may be shared across endpoints).
        var byo = new HttpClient(new CapturingHandler());
        using var pg = new PostGuard(new PostGuardConfig
        {
            PkgUrl = "https://pkg.example.com",
            CryptifyUrl = "https://storage.example.com",
            HttpClient = byo,
        });

        Assert.False(byo.DefaultRequestHeaders.TryGetValues(ClientVersion.HeaderName, out _));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
