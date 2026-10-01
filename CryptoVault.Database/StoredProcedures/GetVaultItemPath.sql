CREATE PROCEDURE [dbo].[GetVaultItemPath]
    @PathLocator HIERARCHYID
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [file_stream].GetFileNamespacePath(1, 0) AS [ItemPath]
    FROM [dbo].[VaultFile]
    WHERE [path_locator] = @PathLocator;
END;

