using System.IO;
using System.Text.RegularExpressions;

namespace CryptoVault;

public static class FileNameCleaner
{
    /// <summary>
    /// Cleans and normalizes a file name:
    /// 1. Preserves the extension.
    /// 2. Replaces invalid OS characters with underscores.
    /// 3. Normalizes whitespace to underscores.
    /// 4. Strips problematic punctuation (parentheses, brackets, quotes, etc.).
    /// 5. Collapses multiple consecutive underscores into a single underscore.
    /// 6. Trims leading/trailing underscores, hyphens, dots, and whitespace.
    /// </summary>
    public static string CleanFileName(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            return "unnamed_file";

        string extension = Path.GetExtension(originalFileName);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);

        // Replace invalid OS filename characters
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            nameWithoutExt = nameWithoutExt.Replace(c, '_');
        }

        // Replace brackets and parentheses with underscores to treat enclosed content as distinct sets
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"[()\[\]{}]+", "_");

        // Replace whitespace with underscores
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"\s+", "_");

        // Remove unwanted punctuation / symbols: ! @ # $ % ^ & + = ' " ` ~ , ; :
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"[!@#$%^&+=`~,;'""<>?|*:]+", "");

        // Collapse multiple underscores or dashes into single ones
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"_+", "_");
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"-+", "-");

        // Trim leading and trailing underscores, hyphens, and dots
        nameWithoutExt = nameWithoutExt.Trim('_', '-', '.', ' ');

        if (string.IsNullOrWhiteSpace(nameWithoutExt))
        {
            nameWithoutExt = "cleaned_file";
        }

        return nameWithoutExt + extension;
    }
}

