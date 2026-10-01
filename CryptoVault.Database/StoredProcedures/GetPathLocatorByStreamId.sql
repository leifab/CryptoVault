CREATE PROCEDURE [dbo].[GetPathLocatorByStreamId]
    @StreamId UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [path_locator].ToString() AS [Locator]
    FROM [dbo].[VaultFile]
    WHERE [stream_id] = @StreamId;
END;

