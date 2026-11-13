using System.Text.Json;
using E4A.PostGuard.Crypto;
using E4A.PostGuard.Models;

namespace E4A.PostGuard.Tests;

public class BuildPolicyJsonTests
{
    private const string EmailAttributeType = "pbdf.sidn-pbdf.email.email";
    private const string DomainAttributeType = "pbdf.sidn-pbdf.email.domain";

    private static RecipientBuilder Email(string email) =>
        new(email, RecipientBaseType.Email);

    private static RecipientBuilder Domain(string email) =>
        new(email, RecipientBaseType.EmailDomain);

    [Fact]
    public void Email_Recipient_AddsEmailAttribute()
    {
        var json = SealPipeline.BuildPolicyJson([Email("alice@example.com")]);

        using var doc = JsonDocument.Parse(json);
        var con = doc.RootElement.GetProperty("alice@example.com").GetProperty("con");

        Assert.Equal(1, con.GetArrayLength());
        Assert.Equal(EmailAttributeType, con[0].GetProperty("t").GetString());
        Assert.Equal("alice@example.com", con[0].GetProperty("v").GetString());
    }

    [Fact]
    public void EmailDomain_Recipient_ExtractsDomainAfterAt()
    {
        var json = SealPipeline.BuildPolicyJson([Domain("bob@example.com")]);

        using var doc = JsonDocument.Parse(json);
        var con = doc.RootElement.GetProperty("bob@example.com").GetProperty("con");

        Assert.Equal(DomainAttributeType, con[0].GetProperty("t").GetString());
        Assert.Equal("example.com", con[0].GetProperty("v").GetString());
    }

    [Fact]
    public void EmailDomain_WithoutAtSign_UsesRawValue()
    {
        var json = SealPipeline.BuildPolicyJson([Domain("example.com")]);

        using var doc = JsonDocument.Parse(json);
        var con = doc.RootElement.GetProperty("example.com").GetProperty("con");

        Assert.Equal(DomainAttributeType, con[0].GetProperty("t").GetString());
        Assert.Equal("example.com", con[0].GetProperty("v").GetString());
    }

    [Fact]
    public void ExtraAttributes_AreAppendedAfterBase()
    {
        var recipient = Email("alice@example.com")
            .ExtraAttribute("pbdf.sidn-pbdf.mobilenumber.mobilenumber", "+31600000000")
            .ExtraAttribute("pbdf.gemeente.personalData.fullname", "Alice");

        var json = SealPipeline.BuildPolicyJson([recipient]);

        using var doc = JsonDocument.Parse(json);
        var con = doc.RootElement.GetProperty("alice@example.com").GetProperty("con");

        Assert.Equal(3, con.GetArrayLength());
        Assert.Equal(EmailAttributeType, con[0].GetProperty("t").GetString());
        Assert.Equal("pbdf.sidn-pbdf.mobilenumber.mobilenumber", con[1].GetProperty("t").GetString());
        Assert.Equal("pbdf.gemeente.personalData.fullname", con[2].GetProperty("t").GetString());
    }

    [Fact]
    public void MultipleRecipients_EachGetOwnPolicyEntry()
    {
        var json = SealPipeline.BuildPolicyJson([
            Email("alice@example.com"),
            Domain("bob@other.test"),
        ]);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("alice@example.com", out _));
        Assert.True(doc.RootElement.TryGetProperty("bob@other.test", out var bobEntry));
        Assert.Equal("other.test", bobEntry.GetProperty("con")[0].GetProperty("v").GetString());
    }

    [Fact]
    public void DuplicatePolicyKey_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => SealPipeline.BuildPolicyJson([
            Email("alice@example.com"),
            Email("alice@example.com"),
        ]));

        Assert.Equal("recipients", ex.ParamName);
    }

    [Fact]
    public void UnknownBaseType_Throws()
    {
        var bogus = new RecipientBuilder("alice@example.com", (RecipientBaseType)999);

        Assert.Throws<ArgumentException>(() => SealPipeline.BuildPolicyJson([bogus]));
    }
}
