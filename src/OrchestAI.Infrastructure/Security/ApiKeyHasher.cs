using System.Security.Cryptography;
using System.Text;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Security;

public sealed class ApiKeyHasher : IApiKeyHasher
{
    private const string Prefix = "orch_live_";
    private const int PublicKeyIdLength = 12;
    private const int SecretLength = 32;
    private const string Base62Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public GeneratedApiKey GenerateNew()
    {
        var publicKeyId = GenerateRandomBase62(PublicKeyIdLength);
        var secret = GenerateRandomBase62(SecretLength);
        var rawKey = $"{Prefix}{publicKeyId}.{secret}";
        var hashedSecret = Hash(secret);

        return new GeneratedApiKey(rawKey, publicKeyId, hashedSecret);
    }

    public ParsedApiKey? Parse(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var withoutPrefix = rawKey[Prefix.Length..];
        var dotIndex = withoutPrefix.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == withoutPrefix.Length - 1)
            return null;

        var publicKeyId = withoutPrefix[..dotIndex];
        var secret = withoutPrefix[(dotIndex + 1)..];
        return new ParsedApiKey(publicKeyId, secret);
    }

    public string Hash(string rawSecret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
        return Convert.ToHexString(hash);
    }

    public bool Verify(string rawSecret, string hashedSecret)
    {
        if (hashedSecret is null)
            return false;

        byte[] storedBytes;
        try
        {
            storedBytes = Convert.FromHexString(hashedSecret);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedBytes = Convert.FromHexString(Hash(rawSecret));
        return CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
    }

    private static string GenerateRandomBase62(int length)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Base62Alphabet[randomBytes[i] % Base62Alphabet.Length];
        return new string(chars);
    }
}
