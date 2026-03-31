using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Security;

public class AesCredentialEncryptor : ICredentialEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    private static readonly string KeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".migrify", "encryption.key");

    public AesCredentialEncryptor(IConfiguration configuration, ILogger<AesCredentialEncryptor> logger)
    {
        // Priority: 1) env var, 2) appsettings, 3) key file, 4) auto-generate
        var keyString = Environment.GetEnvironmentVariable("MIGRIFY_ENCRYPTION_KEY");

        if (string.IsNullOrWhiteSpace(keyString))
            keyString = configuration["Encryption:Key"];

        if (string.IsNullOrWhiteSpace(keyString) && File.Exists(KeyFilePath))
            keyString = File.ReadAllText(KeyFilePath).Trim();

        if (string.IsNullOrWhiteSpace(keyString))
        {
            keyString = GenerateAndStoreKey();
            logger.LogInformation("Generated new encryption key and stored at {KeyFilePath}", KeyFilePath);
        }

        _key = Convert.FromBase64String(keyString);

        if (_key.Length != 32)
            throw new InvalidOperationException(
                "Encryption key must be exactly 32 bytes (256 bits). " +
                "Generate one with: openssl rand -base64 32");
    }

    private static string GenerateAndStoreKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var keyString = Convert.ToBase64String(key);

        var directory = Path.GetDirectoryName(KeyFilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(KeyFilePath, keyString);

        return keyString;
    }

    public string Encrypt(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherText = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherText, tag);

        // Format: nonce (12) + tag (16) + ciphertext
        var result = new byte[NonceSize + TagSize + cipherText.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        cipherText.CopyTo(result, NonceSize + TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherTextBase64)
    {
        var data = Convert.FromBase64String(cipherTextBase64);

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var cipherText = data[(NonceSize + TagSize)..];

        var plainBytes = new byte[cipherText.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherText, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
