CREATE PROCEDURE [dbo].[MatchFingerprints]
    @Fingerprints [dbo].[FileFingerprintType] READONLY,
    @MaxHammingDistance INT = 8
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalQueryFrames INT;
    DECLARE @MinMatchingFrames INT = 10;

    SELECT @TotalQueryFrames = COUNT(*) 
    FROM @Fingerprints;

    IF @TotalQueryFrames = 0
    BEGIN
        SELECT TOP 0
            CAST('' AS CHAR(32)) AS FileHash,
            CAST(0 AS INT) AS VideoStartOffsetMs,
            CAST(0 AS INT) AS MatchingFrameCount,
            0 AS TotalQueryFrames,
            CAST(0.0 AS FLOAT) AS ConfidenceScore;
        RETURN;
    END

    -- 1. Identify candidate matches by index lookups on 16-bit chunks
    -- 2. Filter candidate pairs by full 64-bit Hamming distance (using native BIT_COUNT)
    WITH CandidateMatches AS (
        SELECT 
            q.TimestampMs,
            db.FileHash,
            db.TimestampMs AS DbTimestampMs,
            (db.TimestampMs - q.TimestampMs) AS DeltaT
        FROM @Fingerprints q
        INNER JOIN [dbo].[FileFingerprint] db WITH (FORCESEEK)
            ON (db.Chunk1 = q.Chunk1 
             OR db.Chunk2 = q.Chunk2 
             OR db.Chunk3 = q.Chunk3 
             OR db.Chunk4 = q.Chunk4)
        WHERE BIT_COUNT(db.HashValue ^ q.HashValue) <= @MaxHammingDistance
    ),
    -- 3. Group deltaT into 1000ms buckets to tolerate timing jitter / dropped frames
    OffsetHistogram AS (
        SELECT 
            FileHash,
            FLOOR(DeltaT / 1000.0) * 1000 AS ClusteredDeltaT,
            COUNT(DISTINCT TimestampMs) AS MatchingFrameCount
        FROM CandidateMatches
        GROUP BY FileHash, FLOOR(DeltaT / 1000.0) * 1000
        HAVING COUNT(DISTINCT TimestampMs) >= @MinMatchingFrames
    )
    -- 4. Surface top candidate matches ranked by cluster strength
    SELECT TOP 5
        FileHash,
        CAST(ClusteredDeltaT AS INT) AS VideoStartOffsetMs,
        MatchingFrameCount,
        @TotalQueryFrames AS TotalQueryFrames,
        ROUND(CAST(MatchingFrameCount AS FLOAT) / @TotalQueryFrames, 4) AS ConfidenceScore
    FROM OffsetHistogram
    ORDER BY MatchingFrameCount DESC;
END;

