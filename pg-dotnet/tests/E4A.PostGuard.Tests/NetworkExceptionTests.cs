using E4A.PostGuard.Exceptions;

namespace E4A.PostGuard.Tests;

public class NetworkExceptionTests
{
    [Fact]
    public void Ctor_PopulatesStatusAndUrlProperties()
    {
        var ex = new NetworkException(401, "Unauthorized", "https://pkg.postguard.eu/v2/irma/sign/key");

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("https://pkg.postguard.eu/v2/irma/sign/key", ex.Url);
    }

    [Fact]
    public void Ctor_MessageIsGenericAndIncludesStatus()
    {
        var ex = new NetworkException(500, "boom", "https://cryptify.postguard.eu/fileupload/init");

        Assert.Equal("HTTP 500 received from upstream service", ex.Message);
    }

    [Fact]
    public void Ctor_MessageDoesNotLeakRawUpstreamBody()
    {
        var ex = new NetworkException(500, "sensitive-upstream-detail", "https://cryptify.postguard.eu/fileupload/init");

        Assert.DoesNotContain("sensitive-upstream-detail", ex.Message);
    }

    [Fact]
    public void NetworkException_DoesNotExposePublicBodyProperty()
    {
        Assert.Null(typeof(NetworkException).GetProperty("Body"));
    }

    [Fact]
    public void Ctor_AllowsEmptyBody()
    {
        var ex = new NetworkException(204, "", "https://pkg.postguard.eu/v2/parameters");

        Assert.Equal(204, ex.StatusCode);
        Assert.Equal("https://pkg.postguard.eu/v2/parameters", ex.Url);
    }
}
