CREATE TABLE [dbo].[FileFingerprint]
(
    [FileHash] CHAR(32) NOT NULL,
    [TimestampMs] INT NOT NULL,
    [HashValue] BIGINT NOT NULL,
    [Chunk1] SMALLINT NOT NULL,
    [Chunk2] SMALLINT NOT NULL,
    [Chunk3] SMALLINT NOT NULL,
    [Chunk4] SMALLINT NOT NULL,

    CONSTRAINT [PKFileFingerprint] PRIMARY KEY CLUSTERED 
    (
        [FileHash] ASC,
        [TimestampMs] ASC
    ) ON [PRIMARY],

    INDEX [IXFileFingerprintChunk1] NONCLUSTERED ([Chunk1] ASC) INCLUDE ([HashValue]) ON [PRIMARY],
    INDEX [IXFileFingerprintChunk2] NONCLUSTERED ([Chunk2] ASC) INCLUDE ([HashValue]) ON [PRIMARY],
    INDEX [IXFileFingerprintChunk3] NONCLUSTERED ([Chunk3] ASC) INCLUDE ([HashValue]) ON [PRIMARY],
    INDEX [IXFileFingerprintChunk4] NONCLUSTERED ([Chunk4] ASC) INCLUDE ([HashValue]) ON [PRIMARY]
) ON [PRIMARY];

