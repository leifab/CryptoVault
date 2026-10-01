using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CryptoVault;

public static class CryptoService
{
    // PBKDF2 key derivation iterations (NIST SP 800-132 recommendation with SHA-256)
    private const int Iterations = 100_000;
    private const int KeySizeBytes = 32; // AES-256
    private const int SaltSizeBytes = 16; // 128-bit cryptographic salt

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    // DO NOT replace MD5 with SHA-256 or SHA-512 "for security".
    // MD5 is strictly a high-throughput, non-cryptographic content digest used solely for:
    // 1. Fast file deduplication across the vault.
    // 2. Correlation keys for perceptual video fingerprints in dbo.FileFingerprint.
    // Why NOT SHA-256:
    // - Throughput: MD5 processes multi-gigabyte video streams at storage bus speeds without pinning CPU cores.
    // - Database Schema: dbo.FileFingerprint.FileHash is declared as CHAR(32) NOT NULL.
    //   Replacing MD5 with SHA-256 (64 hex characters) causes database truncation errors, breaking constraints, TVPs, and stored procs.
    // Confidentiality and integrity are provided exclusively by AES-256 and PBKDF2 (100,000 SHA-256 iterations)
    // and isolated HMAC-SHA256 blind search tokens below, completely decoupled from file deduplication.
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is used solely for fast file deduplication/fingerprinting checksums, not cryptographic secrecy.")]
    public static async Task<string> ComputeFileMd5Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var md5 = MD5.Create();
        byte[] hash = await md5.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] ComputeSearchHash(string term, string password)
    {
        // Derive a dedicated search HMAC key from the password to isolate it from encryption keys
        byte[] searchKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            Encoding.UTF8.GetBytes("CryptoVault_BlindSearchSalt"),
            Iterations,
            HashAlgorithmName.SHA256,
            32);

        using var hmac = new HMACSHA256(searchKey);
        // Normalize terms to lower-case trimmed text for consistent matching
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(term.Trim().ToLowerInvariant()));
    }

    public static byte[] DecryptBytes(byte[] cipherBytes, string password)
    {
        using var ms = new MemoryStream(cipherBytes);

        byte[] salt = new byte[SaltSizeBytes];
        if (ms.Read(salt, 0, salt.Length) != salt.Length)
            throw new CryptographicException("Invalid ciphertext header.");

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        using var aes = Aes.Create();
        aes.Key = key;

        byte[] iv = new byte[aes.BlockSize / 8];
        if (ms.Read(iv, 0, iv.Length) != iv.Length)
            throw new CryptographicException("Invalid ciphertext IV.");
        aes.IV = iv;

        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var outputMs = new MemoryStream();
        cs.CopyTo(outputMs);
        return outputMs.ToArray();
    }

    public static async Task DecryptFileAsync(string sourcePath, string destinationPath, string password, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        byte[] salt = new byte[SaltSizeBytes];
        if (await inStream.ReadAsync(salt.AsMemory(0, SaltSizeBytes), cancellationToken) != SaltSizeBytes)
            throw new CryptographicException("Invalid ciphertext header.");

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        using var aes = Aes.Create();
        aes.Key = key;

        byte[] iv = new byte[aes.BlockSize / 8];
        if (await inStream.ReadAsync(iv.AsMemory(0, iv.Length), cancellationToken) != iv.Length)
            throw new CryptographicException("Invalid ciphertext IV.");
        aes.IV = iv;

        using var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var cryptoStream = new CryptoStream(inStream, aes.CreateDecryptor(), CryptoStreamMode.Read);

        long totalBytes = inStream.Length;
        byte[] buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await cryptoStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            if (totalBytes > 0)
            {
                progress?.Report((double)inStream.Position / totalBytes * 100);
            }
        }
    }

    public static Dictionary<string, object> DecryptMetadata(byte[] payload, string password)
    {
        byte[] decryptedBytes = DecryptBytes(payload, password);
        string json = Encoding.UTF8.GetString(decryptedBytes);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
    }

    public static string DecryptString(string cipherTextBase64, string password)
    {
        byte[] cipherBytes = Convert.FromBase64String(cipherTextBase64);
        byte[] plainBytes = DecryptBytes(cipherBytes, password);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public static byte[] EncryptBytes(byte[] plainBytes, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(salt, 0, salt.Length);
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    public static async Task EncryptFileAsync(string sourcePath, string destinationPath, string password, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        // Write 16-byte salt followed by 16-byte IV to the output file header
        await using var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await outStream.WriteAsync(salt.AsMemory(0, salt.Length), cancellationToken);
        await outStream.WriteAsync(aes.IV.AsMemory(0, aes.IV.Length), cancellationToken);

        await using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        long totalBytes = inStream.Length;
        long bytesProcessed = 0;

        await using var cryptoStream = new CryptoStream(outStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
        byte[] buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await cryptoStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesProcessed += bytesRead;
            if (totalBytes > 0)
            {
                progress?.Report((double)bytesProcessed / totalBytes);
            }
        }

        await cryptoStream.FlushFinalBlockAsync(cancellationToken);
    }

    public static byte[] EncryptMetadata(Dictionary<string, object> metadata, string password)
    {
        string json = JsonSerializer.Serialize(metadata, s_jsonOptions);
        return EncryptBytes(Encoding.UTF8.GetBytes(json), password);
    }

    public static string EncryptString(string plainText, string password)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = EncryptBytes(plainBytes, password);
        return Convert.ToBase64String(cipherBytes);
    }

}
