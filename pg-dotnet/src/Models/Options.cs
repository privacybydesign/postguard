namespace E4A.PostGuard.Models;

public record PgFile(string Name, Stream Content);

public class EncryptInput
{
    public required IReadOnlyList<PgFile> Files { get; init; }
    public required IReadOnlyList<RecipientBuilder> Recipients { get; init; }
    public required SignMethod Sign { get; init; }
}

public class UploadOptions
{
    public NotifyOptions? Notify { get; init; }
}

public class NotifyOptions
{
    /// <summary>
    /// Send a notification email to each recipient with a download
    /// link. Default false — the upload is silent unless this (or
    /// <see cref="Sender"/>) is explicitly enabled.
    /// </summary>
    public bool Recipients { get; init; } = false;

    /// <summary>
    /// Send a confirmation email back to the sender. Default false.
    /// Independent of <see cref="Recipients"/>.
    /// </summary>
    public bool Sender { get; init; } = false;

    /// <summary>
    /// Optional unencrypted message body included in any notification
    /// email(s) sent — both the per-recipient mail and the sender
    /// confirmation, when those are enabled.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>Notification email template language. Default 'EN'.</summary>
    public string Language { get; init; } = "EN";
}
