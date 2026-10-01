CREATE PROCEDURE [dbo].[DeleteVaultFile]
    @PathLocator HIERARCHYID,
    @IncludeDescendants BIT = 0
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    IF @IncludeDescendants = 1
    BEGIN
        DELETE FROM [dbo].[VaultFile]
        WHERE [path_locator] = @PathLocator
           OR [path_locator].IsDescendantOf(@PathLocator) = 1;
    END
    ELSE
    BEGIN
        DELETE FROM [dbo].[VaultFile]
        WHERE [path_locator] = @PathLocator;
    END
END;

