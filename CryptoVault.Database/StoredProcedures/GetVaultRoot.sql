CREATE PROCEDURE [dbo].[GetVaultRoot]
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        N'[dbo].[VaultFile]' AS [TableName],
        FileTableRootPath(N'[dbo].[VaultFile]') AS [UNCPath];
END;

