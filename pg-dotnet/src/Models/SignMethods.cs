namespace E4A.PostGuard.Models;

public abstract class SignMethod
{
    internal SignMethod() { }
}

public class ApiKeySign : SignMethod
{
    public string ApiKey { get; }

    internal ApiKeySign(string apiKey)
    {
        ApiKey = apiKey;
    }
}
