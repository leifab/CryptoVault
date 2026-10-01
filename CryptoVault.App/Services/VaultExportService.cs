using System.IO;

namespace CryptoVault;

public sealed class VaultExportService
{
    private static void CollectFilesAndFolders(
        string sourcePhysicalPath,
        string targetDestinationPath,
        string password,
        List<string> directoriesToCreate,
        List<ExportItemTarget> filesToDecrypt)
    {
        if (!Directory.Exists(sourcePhysicalPath))
            return;

        directoriesToCreate.Add(targetDestinationPath);

        foreach (var subDir in Directory.GetDirectories(sourcePhysicalPath))
        {
            string rawSubDirName = Path.GetFileName(subDir);
            string plainSubDirName = FileTableService.DecryptItemName(rawSubDirName, password);
            string childTargetDir = Path.Combine(targetDestinationPath, plainSubDirName);

            CollectFilesAndFolders(subDir, childTargetDir, password, directoriesToCreate, filesToDecrypt);
        }

        foreach (var filePath in Directory.GetFiles(sourcePhysicalPath))
        {
            string rawFileName = Path.GetFileName(filePath);
            string plainFileName = FileTableService.DecryptItemName(rawFileName, password);
            string childTargetFile = Path.Combine(targetDestinationPath, plainFileName);

            filesToDecrypt.Add(new ExportItemTarget(filePath, childTargetFile, plainFileName));
        }
    }

    public static async Task<int> ExportItemsAsync(
        IEnumerable<(string PhysicalPath, bool IsDirectory, string DisplayName)> itemsToExport,
        string baseDestinationPath,
        string password,
        IProgress<ExportProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directoriesToCreate = new List<string>();
        var filesToDecrypt = new List<ExportItemTarget>();

        foreach (var (physicalPath, isDirectory, displayName) in itemsToExport)
        {
            string targetPath = Path.Combine(baseDestinationPath, displayName);
            if (isDirectory)
            {
                CollectFilesAndFolders(physicalPath, targetPath, password, directoriesToCreate, filesToDecrypt);
            }
            else
            {
                filesToDecrypt.Add(new ExportItemTarget(physicalPath, targetPath, displayName));
            }
        }

        // 1. Create all target directory structures
        foreach (var dir in directoriesToCreate)
        {
            Directory.CreateDirectory(dir);
        }

        // 2. Decrypt all files
        int totalFiles = filesToDecrypt.Count;
        for (int i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = filesToDecrypt[i];
            int currentIndex = i;

            var fileProgress = new Progress<double>(filePercent =>
            {
                progress?.Report(new ExportProgressReport(currentIndex + 1, totalFiles, item.DisplayName, filePercent));
            });

            // Ensure parent directory exists for file
            string? fileDir = Path.GetDirectoryName(item.TargetFile);
            if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            await CryptoService.DecryptFileAsync(item.SourceFile, item.TargetFile, password, fileProgress, cancellationToken);
        }

        return totalFiles;
    }

    public sealed record ExportItemTarget(string SourceFile, string TargetFile, string DisplayName);
    public sealed record ExportProgressReport(int CurrentFileIndex, int TotalFiles, string DisplayName, double CurrentFileProgressPercent);
}

