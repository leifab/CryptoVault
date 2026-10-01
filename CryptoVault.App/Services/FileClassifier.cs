using System.IO;
namespace CryptoVault;

internal enum FileCategory
{
    Generic,
    Video,
    Audio,
    Image,
    Document,
    Archive
}

internal static class FileTypeClassifier
{

    private static readonly Dictionary<string, FileCategory> s_extensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = FileCategory.Video,
        [".mkv"] = FileCategory.Video,
        [".avi"] = FileCategory.Video,
        [".mpg"] = FileCategory.Video,
        [".mpeg"] = FileCategory.Video,
        [".mov"] = FileCategory.Video,
        [".wmv"] = FileCategory.Video,
        [".webm"] = FileCategory.Video,
        [".flv"] = FileCategory.Video,
        [".ogm"] = FileCategory.Video,
        [".ogv"] = FileCategory.Video,
        [".ts"] = FileCategory.Video,

        // Images
        [".jpg"] = FileCategory.Image,
        [".jpeg"] = FileCategory.Image,
        [".png"] = FileCategory.Image,
        [".gif"] = FileCategory.Image,
        [".webp"] = FileCategory.Image,
        [".tif"] = FileCategory.Image,
        [".bmp"] = FileCategory.Image,

        // Audio
        [".mp3"] = FileCategory.Audio,
        [".flac"] = FileCategory.Audio,
        [".wav"] = FileCategory.Audio,
        [".m4a"] = FileCategory.Audio,

        // Documents
        [".pdf"] = FileCategory.Document,
        [".doc"] = FileCategory.Document,
        [".docx"] = FileCategory.Document,
        [".txt"] = FileCategory.Document,
        [".xls"] = FileCategory.Document,
        [".xlsx"] = FileCategory.Document,

        // Archives
        [".zip"] = FileCategory.Archive,
        [".7z"] = FileCategory.Archive,
        [".rar"] = FileCategory.Archive,
        [".tar"] = FileCategory.Archive,
    };

    public static FileCategory GetCategory(string path)
    {
        string ext = Path.GetExtension(path);
        return s_extensionMap.TryGetValue(ext, out var category) ? category : FileCategory.Generic;
    }
}
