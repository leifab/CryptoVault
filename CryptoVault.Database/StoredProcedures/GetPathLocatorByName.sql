CREATE PROCEDURE [dbo].[GetPathLocatorByName]
    @Name NVARCHAR(255),
    @ParentLocator HIERARCHYID = NULL,
    @UseFallback BIT = 1
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Locator VARCHAR(4000);

    IF @ParentLocator IS NULL
    BEGIN
        SELECT TOP (1) @Locator = [path_locator].ToString()
        FROM [dbo].[VaultFile]
        WHERE [parent_path_locator] IS NULL AND [name] = @Name;
    END
    ELSE
    BEGIN
        SELECT TOP (1) @Locator = [path_locator].ToString()
        FROM [dbo].[VaultFile]
        WHERE [parent_path_locator] = @ParentLocator AND [name] = @Name;
    END

    IF @Locator IS NULL AND @UseFallback = 1
    BEGIN
        SELECT TOP (1) @Locator = [path_locator].ToString()
        FROM [dbo].[VaultFile]
        WHERE [name] = @Name
        ORDER BY [creation_time] DESC;
    END

    SELECT @Locator AS [Locator];
END;

