CREATE PROCEDURE [dbo].[GetAllFileMetadata]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [MetadataPayload]
    FROM [dbo].[FileMetadata]
    WHERE [MetadataPayload] IS NOT NULL;
END;

