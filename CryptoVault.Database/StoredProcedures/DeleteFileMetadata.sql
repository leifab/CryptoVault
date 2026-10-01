CREATE PROCEDURE [dbo].[DeleteFileMetadata]
    @PathLocator HIERARCHYID,
    @IncludeDescendants BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [dbo].[FileMetadata]
    WHERE [PathLocator] = @PathLocator
       OR (@IncludeDescendants = 1 AND [PathLocator].IsDescendantOf(@PathLocator) = 1);
END;

