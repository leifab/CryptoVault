CREATE TABLE [dbo].[MetadataSearchIndex]
(
    [TermHash] BINARY(32) NOT NULL,
    [PathLocator] HIERARCHYID NOT NULL,

    CONSTRAINT [PKMetadataSearchIndex] PRIMARY KEY CLUSTERED ([TermHash] ASC, [PathLocator] ASC),
    CONSTRAINT [FKFileMetadataMetadataSearchIndex] FOREIGN KEY ([PathLocator])
        REFERENCES [dbo].[FileMetadata] ([PathLocator])
        ON DELETE CASCADE,

    INDEX [IXMetadataSearchIndexLocator] NONCLUSTERED ([PathLocator] ASC) ON [PRIMARY]
) ON [PRIMARY];

