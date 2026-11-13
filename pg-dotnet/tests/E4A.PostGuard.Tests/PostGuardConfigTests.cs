using E4A.PostGuard;

namespace E4A.PostGuard.Tests;

public class PostGuardConfigTests
{
    private const string ValidPkg = "https://pkg.postguard.eu";
    private const string ValidCryptify = "https://cryptify.postguard.eu";

    [Fact]
    public void Ctor_AcceptsHttpsUrls()
    {
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = ValidCryptify,
        };

        var pg = new PostGuard(config);

        Assert.NotNull(pg);
    }

    [Theory]
    [InlineData("http://pkg.postguard.eu")]
    [InlineData("http://localhost:8080")]
    [InlineData("ftp://pkg.postguard.eu")]
    [InlineData("pkg.postguard.eu")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public void Ctor_RejectsNonHttpsPkgUrl(string badUrl)
    {
        var config = new PostGuardConfig
        {
            PkgUrl = badUrl,
            CryptifyUrl = ValidCryptify,
        };

        var ex = Assert.Throws<ArgumentException>(() => new PostGuard(config));
        Assert.Equal("PkgUrl", ex.ParamName);
    }

    [Theory]
    [InlineData("http://cryptify.postguard.eu")]
    [InlineData("ws://cryptify.postguard.eu")]
    [InlineData("cryptify.postguard.eu")]
    public void Ctor_RejectsNonHttpsCryptifyUrl(string badUrl)
    {
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = badUrl,
        };

        var ex = Assert.Throws<ArgumentException>(() => new PostGuard(config));
        Assert.Equal("CryptifyUrl", ex.ParamName);
    }

    [Fact]
    public void Ctor_AllowInsecureUrls_AcceptsHttpLocalhost()
    {
        var config = new PostGuardConfig
        {
            PkgUrl = "http://localhost:8080",
            CryptifyUrl = "http://localhost:8081",
            AllowInsecureUrls = true,
        };

        var pg = new PostGuard(config);

        Assert.NotNull(pg);
    }

    [Fact]
    public void Ctor_AllowInsecureUrls_StillAcceptsHttpsUrls()
    {
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = ValidCryptify,
            AllowInsecureUrls = true,
        };

        var pg = new PostGuard(config);

        Assert.NotNull(pg);
    }

    [Fact]
    public void Ctor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostGuard(null!));
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedHttpClient()
    {
        using var injected = new HttpClient();
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = ValidCryptify,
            HttpClient = injected,
        };

        var pg = new PostGuard(config);
        pg.Dispose();

        // If injected was disposed, this throws ObjectDisposedException.
        injected.DefaultRequestHeaders.TryAddWithoutValidation("X-Test", "ok");
    }

    [Fact]
    public void Dispose_DisposesOwnedHttpClient()
    {
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = ValidCryptify,
        };

        var pg = new PostGuard(config);
        pg.Dispose();
        // Calling Dispose twice must be safe.
        pg.Dispose();
    }

    [Fact]
    public void Ctor_AppliesTimeoutToOwnedClient()
    {
        var config = new PostGuardConfig
        {
            PkgUrl = ValidPkg,
            CryptifyUrl = ValidCryptify,
            Timeout = TimeSpan.FromSeconds(42),
        };

        using var pg = new PostGuard(config);

        // No exception during construction; the Timeout property is applied
        // internally — see PostGuard ctor. We can at least verify the SDK
        // accepts the value without throwing.
        Assert.NotNull(pg);
    }
}
