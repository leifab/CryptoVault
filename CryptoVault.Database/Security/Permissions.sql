-- Stored Procedure Execution Permissions
GRANT EXECUTE ON OBJECT::[dbo].[DeleteFileFingerprint] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[DeleteFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[DeleteVaultFile] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetAllFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetPathLocatorByName] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetPathLocatorByStreamId] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetSampleVaultFiles] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetSubtreeFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultConfig] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultDirectory] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultFileStream] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultFolders] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultItemPath] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultRoot] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[GetVaultSubtreeLocators] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[MatchFingerprints] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[ReparentFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[SaveFileMetadata] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON OBJECT::[dbo].[SetVaultConfig] TO [CryptoVaultUser];
GO

-- User-Defined Table Type Execution Permissions (Required for TVPs)
GRANT EXECUTE ON TYPE::[dbo].[FileFingerprintType] TO [CryptoVaultUser];
GO
GRANT EXECUTE ON TYPE::[dbo].[SearchTermHashType] TO [CryptoVaultUser];
GO

