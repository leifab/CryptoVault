CREATE TABLE [dbo].[VaultConfig]
(
    [ConfigKey] NVARCHAR(100) NOT NULL,
    [ConfigValue] NVARCHAR(MAX) NOT NULL,
    [RecordCreateDate] DATETIME2(7) NOT NULL 
        CONSTRAINT [DFVaultConfigRecordCreateDate] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PKVaultConfig] PRIMARY KEY CLUSTERED ([ConfigKey] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

