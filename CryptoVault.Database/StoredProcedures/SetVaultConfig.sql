CREATE PROCEDURE [dbo].[SetVaultConfig]
    @ConfigKey NVARCHAR(100),
    @ConfigValue NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    MERGE [dbo].[VaultConfig] WITH (HOLDLOCK) AS target
    USING (SELECT @ConfigKey AS ConfigKey) AS source
    ON (target.[ConfigKey] = source.[ConfigKey])
    WHEN MATCHED THEN
        UPDATE SET [ConfigValue] = @ConfigValue,
                   [RecordCreateDate] = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT ([ConfigKey], [ConfigValue], [RecordCreateDate])
        VALUES (@ConfigKey, @ConfigValue, SYSUTCDATETIME());
END;
