using System.IO;

namespace CryptoVault;

public sealed class VaultImportService
{
    public static async Task<ImportBatchResult> ImportFilesAsync(
        VaultRepository repository,
        IEnumerable<string> sourceFilePaths,
        string? dropTargetLocator,
        string dropTargetDirectory,
        string password,
        Func<string, List<FingerprintMatchResult>, Task<bool>> duplicateConfirmationCallback,
        IEnumerable<VaultItem>? cachedItems = null,
        IProgress<ImportProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int processedCount = 0;
        int errorCount = 0;
        var errorMessages = new List<string>();

        foreach (string initialSourcePath in sourceFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(initialSourcePath))
                continue;

            string sourcePath = initialSourcePath;
            string originalFileName = Path.GetFileName(sourcePath);

            // Step 1: Filename cleanup routine
            string cleanedFileName = FileNameCleaner.CleanFileName(originalFileName);

            // Step 2: Save original file with new filename if normalized
            string sourceDir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(sourceDir) && !string.Equals(originalFileName, cleanedFileName, StringComparison.Ordinal))
            {
                string targetNewSourcePath = FileTableService.GetUniqueFilePath(sourceDir, cleanedFileName);
                try
                {
                    File.Move(sourcePath, targetNewSourcePath);
                    sourcePath = targetNewSourcePath;
                    cleanedFileName = Path.GetFileName(sourcePath);
                }
                catch
                {
                    // Fall back to original file path if rename fails
                }
            }

            // Compute 32-character MD5 content digest for fast vault deduplication and video fingerprint correlation
            string fileHash = string.Empty;
            try
            {
                fileHash = await CryptoService.ComputeFileMd5Async(sourcePath, cancellationToken);
            }
            catch
            {
                // Fallback to empty hash if unreadable
            }

            bool signaturesInserted = false;

            if (FileTypeClassifier.GetCategory(sourcePath) == FileCategory.Video)
            {
                progress?.Report(new ImportProgressReport(cleanedFileName, $"Fingerprinting: {cleanedFileName}...", 0, false));

                var frameProgress = new Progress<double>(p =>
                {
                    progress?.Report(new ImportProgressReport(cleanedFileName, $"Fingerprinting: {cleanedFileName}...", p, false));
                });

                var signatures = await VideoUtilities.ReadFramesAsync(sourcePath, fileHash, frameProgress);

                if (signatures != null && signatures.Count > 0)
                {
                    progress?.Report(new ImportProgressReport(cleanedFileName, "Checking for matching video content...", 0, true));

                    List<FingerprintMatchResult> matches = new();
                    try
                    {
                        matches = await VideoUtilities.MatchFingerprintsAsync(repository.ConnectionString, signatures);
                    }
                    catch
                    {
                        // sp_MatchFingerprints fallback if unavailable
                    }

                    if (matches.Count > 0)
                    {
                        await VaultMetadataService.ResolveMatchDisplayNamesAsync(repository, matches, cachedItems, password, cancellationToken);

                        bool importAnyway = await duplicateConfirmationCallback(cleanedFileName, matches);
                        if (!importAnyway)
                        {
                            progress?.Report(new ImportProgressReport(cleanedFileName, $"Skipped {cleanedFileName} (matching video content detected).", 0, false));
                            continue;
                        }
                    }

                    progress?.Report(new ImportProgressReport(cleanedFileName, "Storing signatures...", 0, true));
                    await VideoUtilities.BulkInsertSignaturesAsync(repository.ConnectionString, signatures);
                    signaturesInserted = true;
                }
            }

            progress?.Report(new ImportProgressReport(cleanedFileName, $"Encrypting: {cleanedFileName}...", 0, false));

            try
            {
                string encryptedNameBase64 = CryptoService.EncryptString(cleanedFileName, password);
                string safeEncFileName = encryptedNameBase64.Replace('+', '-').Replace('/', '_').TrimEnd('=') + ".enc";
                string destinationPath = Path.Combine(dropTargetDirectory, safeEncFileName);

                var encryptProgress = new Progress<double>(p =>
                {
                    progress?.Report(new ImportProgressReport(cleanedFileName, $"Encrypting: {cleanedFileName}...", p * 100, false));
                });

                await CryptoService.EncryptFileAsync(sourcePath, destinationPath, password, encryptProgress, cancellationToken);

                // Step 4: Resolve post-write locator from FileTable
                string? locator = await FileTableService.GetPathLocatorWithRetryAsync(repository, safeEncFileName, dropTargetLocator, cancellationToken: cancellationToken);
                if (string.IsNullOrEmpty(locator))
                {
                    throw new InvalidOperationException($"Could not resolve FileTable path locator for '{cleanedFileName}' after encryption.");
                }

                long fileSize = 0;
                try
                {
                    fileSize = new FileInfo(sourcePath).Length;
                }
                catch
                {
                    // Ignore file info read errors
                }

                // Split cleaned filename (without extension) at every underscore into distinct tags
                string nameStem = Path.GetFileNameWithoutExtension(cleanedFileName);
                var distinctTags = nameStem
                    .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var metadata = new Dictionary<string, object>
                {
                    ["filename"] = cleanedFileName,
                    ["tags"] = string.Join(", ", distinctTags),
                    ["hash"] = fileHash,
                    ["ImportedUtc"] = DateTime.UtcNow.ToString("o"),
                    ["SizeBytes"] = fileSize
                };

                await VaultMetadataService.SaveMetadataAsync(repository, locator, metadata, password, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                errorMessages.Add($"Error processing '{cleanedFileName}': {ex.Message}");

                // Rollback any signatures inserted for this failed file
                if (signaturesInserted && !string.IsNullOrEmpty(fileHash))
                {
                    try
                    {
                        await repository.DeleteFileFingerprintAsync(fileHash, cancellationToken);
                    }
                    catch
                    {
                        // Suppress rollback failures
                    }
                }
            }
        }

        return new ImportBatchResult(processedCount, errorCount, errorMessages);
    }

    public sealed record ImportBatchResult(int ProcessedCount, int ErrorCount, List<string> ErrorMessages);
    public sealed record ImportProgressReport(string FileName, string StatusMessage, double PercentComplete, bool IsIndeterminate);
}

