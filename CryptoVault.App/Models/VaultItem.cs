namespace CryptoVault;

public class VaultItem
{
    public string DisplayName { get; set; } = string.Empty;
    // 32-character hexadecimal MD5 content digest used for fast deduplication and perceptual fingerprint correlation.
    public string? FileHash { get; set; }
    public bool IsDirectory { get; set; }
    public string ItemType => IsDirectory ? "Folder" : "File";
    public string? PathLocator { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
}
