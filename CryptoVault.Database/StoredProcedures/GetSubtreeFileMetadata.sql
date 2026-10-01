CREATE PROCEDURE [dbo].[GetSubtreeFileMetadata]
    @RootLocator HIERARCHYID
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [MetadataPayload]
    FROM [dbo].[FileMetadata]
    WHERE ([PathLocator] = @RootLocator OR [PathLocator].IsDescendantOf(@RootLocator) = 1)
      AND [MetadataPayload] IS NOT NULL;
END;

