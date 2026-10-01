CREATE PROCEDURE [dbo].[DeleteFileFingerprint]
    @FileHash CHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [dbo].[FileFingerprint]
    WHERE [FileHash] = @FileHash;
END;

