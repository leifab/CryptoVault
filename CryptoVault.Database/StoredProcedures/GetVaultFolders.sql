CREATE PROCEDURE [dbo].[GetVaultFolders]
    @ParentLocator HIERARCHYID = NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [name] AS [Name], 
        [path_locator].ToString() AS [Locator]
    FROM [dbo].[VaultFile]
    WHERE [is_directory] = 1
      AND ((@ParentLocator IS NULL AND [parent_path_locator] IS NULL)
        OR (@ParentLocator IS NOT NULL AND [parent_path_locator] = @ParentLocator))
    ORDER BY [name] ASC;
END;

