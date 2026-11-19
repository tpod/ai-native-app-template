using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OneOf;

namespace AppTemplate.Api.Actions.Preview;

/// <summary>Issues and checks <c>base64url(json).base64url(hmac)</c> tokens. Expiry and staleness are checked by the caller.</summary>
public sealed class PreviewTokenSigner(IOptions<PreviewOptions> options)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public string Sign(PreviewTokenPayload payload)
    {
        var body = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJson.Options));
        return $"{body}.{Base64Url.EncodeToString(Mac(body))}";
    }

    public OneOf<PreviewTokenPayload, PreviewTokenInvalid> Verify(string? token)
    {
        try
        {
            var parts = token?.Split('.');
            if (parts is not [var body, var signature] || body.Length == 0)
            {
                return new PreviewTokenInvalid();
            }

            if (!CryptographicOperations.FixedTimeEquals(Mac(body), Base64Url.DecodeFromChars(signature)))
            {
                return new PreviewTokenInvalid();
            }

            var payload = JsonSerializer.Deserialize<PreviewTokenPayload>(
                Base64Url.DecodeFromChars(body), CanonicalJson.Options);
            return payload is null ? new PreviewTokenInvalid() : payload;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return new PreviewTokenInvalid();
        }
    }

    // The MAC covers the encoded text, so a re-encoding of the same bytes cannot verify.
    private byte[] Mac(string encodedBody) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(encodedBody));
}
