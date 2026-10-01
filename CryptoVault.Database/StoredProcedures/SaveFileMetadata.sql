CREATE PROCEDURE [dbo].[SaveFileMetadata]
    @PathLocator HIERARCHYID,
    @MetadataPayload VARBINARY(MAX),
    @TermHashes [dbo].[SearchTermHashType] READONLY
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- 1. Upsert metadata payload via MERGE
        MERGE [dbo].[FileMetadata] WITH (HOLDLOCK) AS target
        USING (SELECT @PathLocator AS PathLocator) AS source
        ON (target.[PathLocator] = source.[PathLocator])
        WHEN MATCHED THEN
            UPDATE SET [MetadataPayload] = @MetadataPayload,
                       [RecordUpdateDate] = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([PathLocator], [MetadataPayload], [RecordUpdateDate])
            VALUES (@PathLocator, @MetadataPayload, SYSUTCDATETIME());

        -- 2. Refresh search tokens
        DELETE FROM [dbo].[MetadataSearchIndex]
        WHERE [PathLocator] = @PathLocator;

        INSERT INTO [dbo].[MetadataSearchIndex] ([TermHash], [PathLocator])
        SELECT [TermHash], @PathLocator
        FROM @TermHashes;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

