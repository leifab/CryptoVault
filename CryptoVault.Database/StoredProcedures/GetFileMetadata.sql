CREATE PROCEDURE [dbo].[GetFileMetadata]
    @PathLocator HIERARCHYID
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [MetadataPayload]
    FROM [dbo].[FileMetadata]
    WHERE [PathLocator] = @PathLocator;
END;

