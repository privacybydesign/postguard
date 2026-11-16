using E4A.PostGuard.Crypto;
using E4A.PostGuard.Models;

namespace E4A.PostGuard;

/// <summary>
/// A lazy encryption builder. Call <see cref="UploadAsync"/> or <see cref="ToBytesAsync"/>
/// to execute the encryption.
/// </summary>
public class Sealed
{
    private readonly PostGuardConfig _config;
    private readonly HttpClient _http;
    private readonly EncryptInput _input;

    internal Sealed(PostGuardConfig config, HttpClient http, EncryptInput input)
    {
        _config = config;
        _http = http;
        _input = input;
    }

    /// <summary>
    /// Encrypt the files and upload to Cryptify.
    /// </summary>
    /// <param name="options">Optional upload options (e.g. email notification).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result containing the UUID.</returns>
    public async Task<UploadResult> UploadAsync(
        UploadOptions? options = null,
        CancellationToken ct = default)
    {
        return await SealPipeline.SealAndUploadAsync(_config, _http, _input, options, ct);
    }

    /// <summary>
    /// Encrypt the files and return the raw sealed bytes (no upload).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sealed (encrypted + signed) byte array.</returns>
    public async Task<byte[]> ToBytesAsync(CancellationToken ct = default)
    {
        return await SealPipeline.SealAsync(_config, _http, _input, ct);
    }
}
