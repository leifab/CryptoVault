CREATE PROCEDURE [dbo].[ReparentFileMetadata]
    @OldLocator HIERARCHYID,
    @NewLocator HIERARCHYID,
    @NewPayload VARBINARY(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- 1. Insert new locator with updated or existing payload
        INSERT INTO [dbo].[FileMetadata] ([PathLocator], [MetadataPayload], [RecordUpdateDate])
        SELECT @NewLocator, ISNULL(@NewPayload, [MetadataPayload]), SYSUTCDATETIME()
        FROM [dbo].[FileMetadata]
        WHERE [PathLocator] = @OldLocator;

        -- 2. Repoint search index tokens to new locator
        UPDATE [dbo].[MetadataSearchIndex]
        SET [PathLocator] = @NewLocator
        WHERE [PathLocator] = @OldLocator;

        -- 3. Delete old locator record
        DELETE FROM [dbo].[FileMetadata]
        WHERE [PathLocator] = @OldLocator;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

