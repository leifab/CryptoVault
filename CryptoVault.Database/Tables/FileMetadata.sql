CREATE TABLE [dbo].[FileMetadata]
(
    [PathLocator] HIERARCHYID NOT NULL,
    [MetadataPayload] VARBINARY(MAX) NOT NULL,
    [RecordUpdateDate] DATETIME2(7) NOT NULL 
        CONSTRAINT [DFFileMetadataRecordUpdateDate] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PKFileMetadata] PRIMARY KEY CLUSTERED ([PathLocator] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

