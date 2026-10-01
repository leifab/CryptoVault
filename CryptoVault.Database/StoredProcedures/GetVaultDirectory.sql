CREATE PROCEDURE [dbo].[GetVaultDirectory]
    @ParentLocator HIERARCHYID = NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        t.[name] AS [Name],
        t.[is_directory] AS [IsDirectory],
        t.[path_locator].ToString() AS [Locator],
        t.[file_stream].GetFileNamespacePath() AS [RelPath],
        CASE 
            WHEN t.[is_directory] = 1 THEN NULL 
            ELSE COALESCE(t.[cached_file_size], DATALENGTH(t.[file_stream]))
        END AS [FileSize],
        m.[MetadataPayload]
    FROM [dbo].[VaultFile] t
    LEFT JOIN [dbo].[FileMetadata] m ON t.[path_locator] = m.[PathLocator]
    WHERE (@ParentLocator IS NULL AND t.[parent_path_locator] IS NULL)
       OR (@ParentLocator IS NOT NULL AND t.[parent_path_locator] = @ParentLocator)
    ORDER BY t.[is_directory] DESC, t.[name] ASC;
END;

