CREATE TYPE [dbo].[FileFingerprintType] AS TABLE
(
    [FileHash] CHAR(32) NOT NULL,
    [TimestampMs] INT NOT NULL,
    [HashValue] BIGINT NOT NULL,
    [Chunk1] SMALLINT NOT NULL,
    [Chunk2] SMALLINT NOT NULL,
    [Chunk3] SMALLINT NOT NULL,
    [Chunk4] SMALLINT NOT NULL
);

