using System.Data.SqlClient;
using System.IO;

namespace CryptoVault;

public sealed class VaultAuthService
{
    private const string PasswordVerificationToken = "CryptoVault_Verification_Token_OK";

    // Three-Stage Password Verification & Key Invalidation Defense:
    // Stage 1: Checks dbo.VaultConfig for the encrypted 'PasswordVerification' token.
    // Stage 2: If unmigrated or absent, performs trial decryption on up to 5 sample .enc files.
    // Stage 3: If valid or empty vault, securely persists the encrypted verification token.
    // This barrier guarantees the user cannot unlock the vault with a mismatched password
    // and inadvertently corrupt payloads or file headers with the wrong key.
    public static async Task<(bool IsValid, bool IsNewVault)> VerifyOrInitializePasswordAsync(
        VaultRepository repository,
        string password,
        CancellationToken cancellationToken = default)
    {
        // 1. Check if PasswordVerification token exists in dbo.VaultConfig
        try
        {
            string? storedEncryptedToken = await repository.GetVaultConfigAsync("PasswordVerification", cancellationToken);
            if (!string.IsNullOrEmpty(storedEncryptedToken))
            {
                try
                {
                    string decrypted = CryptoService.DecryptString(storedEncryptedToken, password);
                    return (decrypted == PasswordVerificationToken, false);
                }
                catch
                {
                    return (false, false);
                }
            }
        }
        catch (SqlException ex) when (ex.Number == 208 || ex.Number == 2812)
        {
            // Table or stored procedure does not exist yet in an unmigrated database
        }

        // 2. If no VaultConfig token exists yet, check if there are existing .enc files in the FileTable
        var existingEncNames = await repository.GetSampleVaultFilesAsync(5, cancellationToken);
        if (existingEncNames.Count > 0)
        {
            bool anyDecrypted = false;
            foreach (string rawName in existingEncNames)
            {
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(rawName);
                    string base64 = baseName.Replace('-', '+').Replace('_', '/');
                    switch (base64.Length % 4)
                    {
                        case 2: base64 += "=="; break;
                        case 3: base64 += "="; break;
                    }
                    string decrypted = CryptoService.DecryptString(base64, password);
                    if (!string.IsNullOrWhiteSpace(decrypted))
                    {
                        anyDecrypted = true;
                        break;
                    }
                }
                catch
                {
                    // Incorrect password for this file
                }
            }

            if (!anyDecrypted)
            {
                // Existing files could not be decrypted with this password
                return (false, false);
            }
        }

        // 3. Vault is either new/empty or password was validated against existing files:
        // Store the encrypted verification token via dbo.SetVaultConfig.
        string verificationPayload = CryptoService.EncryptString(PasswordVerificationToken, password);
        await repository.SetVaultConfigAsync("PasswordVerification", verificationPayload, cancellationToken);
        return (true, existingEncNames.Count == 0);
    }
}

