using System.IO;

namespace CryptoVault;

public sealed class FileTableService
{
    public static string DecodeDirectoryName(string rawName, string password)
    {
        try
        {
            // Revert URL-safe base64 ('-' and '_' back to '+' and '/') and restore padding
            string base64 = rawName.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return CryptoService.DecryptString(base64, password);
        }
        catch
        {
            return rawName;
        }
    }

    public static string DecryptItemName(string rawName, string password)
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
            return CryptoService.DecryptString(base64, password);
        }
        catch
        {
            return rawName; // Fallback if not encrypted with this key
        }
    }

    public static string EncodeDirectoryName(string plainName, string password)
    {
        string encNameBase64 = CryptoService.EncryptString(plainName.Trim(), password);
        // URL-safe base64 encoding avoids illegal characters ('/' and '+') in Windows directory names
        return encNameBase64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string GenerateUniqueDisplayName(string desiredName, HashSet<string> existingNames, bool isDirectory = false)
    {
        if (!existingNames.Contains(desiredName))
            return desiredName;

        string stem = isDirectory ? desiredName : Path.GetFileNameWithoutExtension(desiredName);
        string ext = isDirectory ? "" : Path.GetExtension(desiredName);
        int counter = 1;

        while (true)
        {
            string candidate = $"{stem} ({counter}){ext}";
            if (!existingNames.Contains(candidate))
                return candidate;
            counter++;
        }
    }

    public static async Task<string?> GetPathLocatorByStreamIdWithRetryAsync(
        VaultRepository repository,
        Guid streamId,
        string oldLocator,
        int maxAttempts = 12,
        int delayMs = 100,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                string? locStr = await repository.GetPathLocatorByStreamIdAsync(streamId, cancellationToken);
                if (!string.IsNullOrEmpty(locStr) && !string.Equals(locStr, oldLocator, StringComparison.OrdinalIgnoreCase))
                {
                    return locStr;
                }
            }
            catch
            {
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        return null;
    }

    // DO NOT remove this polling retry loop or reduce the delay below 100-200ms.
    // Windows FileTable relies on an asynchronous kernel filter driver to detect file system
    // operations on the SMB share and register corresponding rows in [dbo].[VaultFile].
    // Transactional visibility in SQL Server typically lags physical SMB writes by 50-200ms.
    // Querying immediately without retries produces false-positive null/not-found locators.
    public static async Task<string?> GetPathLocatorWithRetryAsync(
        VaultRepository repository,
        string rawFileName,
        string? parentLocator,
        int maxAttempts = 15,
        int delayMs = 200,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                string? locator = await repository.GetPathLocatorByNameAsync(rawFileName, parentLocator, useFallback: true, cancellationToken);
                if (!string.IsNullOrEmpty(locator))
                    return locator;
            }
            catch
            {
                // Retry if file table filter driver registration has a slight delay
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        return null;
    }

    public static string GetUniqueFilePath(string directory, string fileName)
    {
        string fullPath = Path.Combine(directory, fileName);
        if (!File.Exists(fullPath))
            return fullPath;

        string ext = Path.GetExtension(fileName);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        int counter = 1;

        while (true)
        {
            string candidate = Path.Combine(directory, $"{nameWithoutExt} ({counter}){ext}");
            if (!File.Exists(candidate))
                return candidate;
            counter++;
        }
    }

    public static void PhysicalDelete(string physicalPath)
    {
        if (Directory.Exists(physicalPath))
        {
            Directory.Delete(physicalPath, recursive: true);
        }
        else if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
    }

    public static void PhysicalMove(string sourcePath, string targetPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, targetPath);
        }
        else if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, targetPath);
        }
    }
}
