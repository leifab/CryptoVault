CREATE PROCEDURE [dbo].[GetVaultSubtreeLocators]
    @RootLocator HIERARCHYID
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [stream_id] AS [StreamId],
        [path_locator].ToString() AS [Locator]
    FROM [dbo].[VaultFile]
    WHERE [path_locator] = @RootLocator
       OR [path_locator].IsDescendantOf(@RootLocator) = 1;
END;

