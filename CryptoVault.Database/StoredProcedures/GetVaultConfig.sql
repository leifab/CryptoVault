CREATE PROCEDURE [dbo].[GetVaultConfig]
    @ConfigKey NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [ConfigValue]
    FROM [dbo].[VaultConfig]
    WHERE [ConfigKey] = @ConfigKey;
END;

