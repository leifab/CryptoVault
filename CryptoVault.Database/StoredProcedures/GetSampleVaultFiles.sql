CREATE PROCEDURE [dbo].[GetSampleVaultFiles]
    @TopCount INT = 5
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@TopCount) [name] AS [Name]
    FROM [dbo].[VaultFile]
    WHERE [is_directory] = 0 AND [name] LIKE N'%.enc';
END;

