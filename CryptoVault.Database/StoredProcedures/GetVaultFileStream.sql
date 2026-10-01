CREATE PROCEDURE [dbo].[GetVaultFileStream]
    @PathLocator HIERARCHYID
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [name] AS [Name],
        [file_type] AS [FileType],
        [cached_file_size] AS [FileSize],
        [file_stream] AS [FileStream]
    FROM [dbo].[VaultFile]
    WHERE [path_locator] = @PathLocator
      AND [is_directory] = 0;
END;

