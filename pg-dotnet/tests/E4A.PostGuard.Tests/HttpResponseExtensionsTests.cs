using System.Net;
using E4A.PostGuard.Api;
using E4A.PostGuard.Exceptions;

namespace E4A.PostGuard.Tests;

public class HttpResponseExtensionsTests
{
    [Fact]
    public async Task EnsureSuccessAsync_SuccessStatus_DoesNotThrow()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        };

        await response.EnsureSuccessAsync();
    }

    [Fact]
    public async Task EnsureSuccessAsync_NonSuccess_ThrowsNetworkExceptionWithStatusAndUrl()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream boom"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://pkg.postguard.eu/v2/parameters")
        };

        var ex = await Assert.ThrowsAsync<NetworkException>(() => response.EnsureSuccessAsync());
        Assert.Equal(502, ex.StatusCode);
        Assert.Equal("https://pkg.postguard.eu/v2/parameters", ex.Url);
    }

    [Fact]
    public async Task EnsureSuccessAsync_NonSuccessWithoutRequestMessage_UsesUnknownUrl()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        };

        var ex = await Assert.ThrowsAsync<NetworkException>(() => response.EnsureSuccessAsync());
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("<unknown>", ex.Url);
    }
}
