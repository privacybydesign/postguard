namespace E4A.PostGuard.Models;

internal enum RecipientBaseType
{
    Email,
    EmailDomain,
}

public class RecipientBuilder
{
    public string Email { get; }
    internal RecipientBaseType BaseType { get; }
    internal List<(string Type, string Value)> Extras { get; } = [];

    internal RecipientBuilder(string email, RecipientBaseType baseType)
    {
        Email = email;
        BaseType = baseType;
    }

    public RecipientBuilder ExtraAttribute(string type, string value)
    {
        Extras.Add((type, value));
        return this;
    }
}
