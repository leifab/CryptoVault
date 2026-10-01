using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace CryptoVault;

public class FingerprintMatchResult
{
    public string ConfidencePercentage => $"{ConfidenceScore * 100:0.0}%";
    public double ConfidenceScore { get; set; }
    public string? DisplayName { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string FormattedOffset
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(Math.Abs(VideoStartOffsetMs));
            string sign = VideoStartOffsetMs < 0 ? "-" : "+";
            return $"{sign}{ts:mm\\:ss\\.fff}";
        }
    }
    public string FrameMatchDisplay => $"{MatchingFrameCount:N0} / {TotalQueryFrames:N0}";
    public int MatchingFrameCount { get; set; }
    public int TotalQueryFrames { get; set; }
    public int VideoStartOffsetMs { get; set; }
}

internal static class VideoUtilities
{
    internal static async Task BulkInsertSignaturesAsync(string connectionString, IEnumerable<FrameSignature> signatures)
    {
        using var bulkCopy = new SqlBulkCopy(connectionString)
        {
            DestinationTableName = "dbo.FileFingerprint",
            BatchSize = 5000
        };

        var table = CreateSignaturesDataTable(signatures);
        await bulkCopy.WriteToServerAsync(table);
    }

    // Constructs the DataTable mapping to User-Defined Table Type dbo.FileFingerprintType.
    // - FileHash: 32-character MD5 content digest correlating the video to dbo.FileFingerprint.
    // - Chunk1..4: 64-bit DCT perceptual hash split into four 16-bit chunks.
    //   Under the pigeonhole principle, two similar frames with Hamming distance <= 8 will
    //   share identical or near-identical sub-chunks, allowing indexed candidate searches
    //   via IXFileFingerprintChunk1..4 without full table scans.
    internal static DataTable CreateSignaturesDataTable(IEnumerable<FrameSignature> signatures)
    {
        var table = new DataTable();
        table.Columns.Add("FileHash", typeof(string));
        table.Columns.Add("TimestampMs", typeof(int));
        table.Columns.Add("HashValue", typeof(long));
        table.Columns.Add("Chunk1", typeof(short));
        table.Columns.Add("Chunk2", typeof(short));
        table.Columns.Add("Chunk3", typeof(short));
        table.Columns.Add("Chunk4", typeof(short));

        foreach (var sig in signatures)
        {
            table.Rows.Add(
                sig.FileId,
                sig.TimestampMs,
                sig.HashValue,
                (short)(sig.HashValue >> 48),
                (short)((sig.HashValue >> 32) & 0xFFFF),
                (short)((sig.HashValue >> 16) & 0xFFFF),
                (short)(sig.HashValue & 0xFFFF)
            );
        }

        return table;
    }

    private static async Task<double> GetVideoDurationAsync(string videoPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\" ",
            RedirectStandardOutput = true,
            RedirectStandardError = true, // capture logs/errors if needed
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double durationSeconds))
        {
            return durationSeconds;
        }

        return 0;
    }

    internal static async Task<List<FingerprintMatchResult>> MatchFingerprintsAsync(
        string connectionString,
        IEnumerable<FrameSignature> signatures,
        int maxHammingDistance = 8)
    {
        var results = new List<FingerprintMatchResult>();
        var table = CreateSignaturesDataTable(signatures);

        if (table.Rows.Count == 0)
            return results;

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("dbo.MatchFingerprints", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        var tvpParam = new SqlParameter("@Fingerprints", SqlDbType.Structured)
        {
            TypeName = "dbo.FileFingerprintType",
            Value = table
        };
        command.Parameters.Add(tvpParam);
        command.Parameters.AddWithValue("@MaxHammingDistance", maxHammingDistance);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string fileId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            int offsetMs = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
            int matchCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
            int totalFrames = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);
            double confidence = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture);

            results.Add(new FingerprintMatchResult
            {
                ConfidenceScore = confidence,
                DisplayName = null,
                FileId = fileId,
                MatchingFrameCount = matchCount,
                TotalQueryFrames = totalFrames,
                VideoStartOffsetMs = offsetMs
            });
        }

        return results;
    }

    internal static async Task<List<FrameSignature>?> ReadFramesAsync(string videoPath, string fileHash, IProgress<double>? progress = null)
    {
        double videoDuration = await GetVideoDurationAsync(videoPath);

        if (videoDuration <= 0)
        {
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-loglevel error -hide_banner -hwaccel cuda -hwaccel_output_format cuda -i \"{videoPath}\" " +
                        "-vf \"fps=2,scale_cuda=32:32,hwdownload,format=nv12,format=gray\" " +
                        "-f rawvideo -",
            RedirectStandardOutput = true,
            RedirectStandardError = true, // capture logs/errors if needed
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        using var stdout = process.StandardOutput.BaseStream;

        byte[] frameBuffer = new byte[32 * 32]; // exactly 1024 bytes per frame
        int frameIndex = 0;

        List<FrameSignature> signatures = new();
        // Read full frames until the stream ends
        while (true)
        {
            int bytesRead = await stdout.ReadAtLeastAsync(frameBuffer, frameBuffer.Length, throwOnEndOfStream: false);

            if (bytesRead < frameBuffer.Length)
            {
                break; // End of stream or partial trailing frame
            }

            progress?.Report((double)frameIndex / (videoDuration * 2) * 100);

            long hash = PerceptualHasher.ComputeDctHash(frameBuffer);
            int timestampMs = frameIndex * 500;

            signatures.Add(new FrameSignature(fileHash, timestampMs, hash));
            frameIndex++;
        }

        await process.WaitForExitAsync();

        return signatures;
    }

    internal sealed record FrameSignature(string FileId, int TimestampMs, long HashValue);

    // 2D Discrete Cosine Transform (DCT) Perceptual Hasher (pHash):
    // 1. Transforms 32x32 grayscale pixel luminance into the frequency domain.
    // 2. Samples the top-left 8x8 matrix (low frequencies representing global structure).
    // 3. Omits the DC component [0] (average luminance) to ensure illumination invariance.
    // 4. Quantizes the 63 AC coefficients around their median into a 64-bit fingerprint.
    protected static class PerceptualHasher
    {
        private const int ReducedSize = 8; // Top-left 8x8 contains structural low frequencies
        private const int Size = 32;

        private static readonly double s_sqrt1OverSize = Math.Sqrt(1.0 / Size);
        private static readonly double s_sqrt2OverSize = Math.Sqrt(2.0 / Size);

        internal static long ComputeDctHash(byte[] frame32x32)
        {
            // 1. Convert byte grid to double grid
            double[,] grid = new double[Size, Size];
            for (int y = 0; y < Size; y++)
            {
                int rowOffset = y * Size;
                for (int x = 0; x < Size; x++)
                {
                    grid[y, x] = frame32x32[rowOffset + x];
                }
            }

            // 2. Compute 2D DCT for the top-left 8x8 coefficients only
            double[] coefficients = new double[ReducedSize * ReducedSize];
            int idx = 0;

            for (int u = 0; u < ReducedSize; u++)
            {
                double cu = (u == 0) ? s_sqrt1OverSize : s_sqrt2OverSize;

                for (int v = 0; v < ReducedSize; v++)
                {
                    double cv = (v == 0) ? s_sqrt1OverSize : s_sqrt2OverSize;
                    double sum = 0.0;

                    for (int x = 0; x < Size; x++)
                    {
                        double cosX = Math.Cos(((2 * x + 1) * u * Math.PI) / (2.0 * Size));

                        for (int y = 0; y < Size; y++)
                        {
                            double cosY = Math.Cos(((2 * y + 1) * v * Math.PI) / (2.0 * Size));
                            sum += grid[y, x] * cosX * cosY;
                        }
                    }

                    coefficients[idx++] = cu * cv * sum;
                }
            }

            // 3. Find the median of the 64 coefficients (skip DC at [0] to ignore overall brightness)
            double[] nonDcValues = coefficients.Skip(1).ToArray();
            Array.Sort(nonDcValues);
            double median = (nonDcValues[31] + nonDcValues[32]) / 2.0;

            // 4. Set bits: 1 if coefficient > median, else 0
            long hash = 0;
            for (int i = 0; i < coefficients.Length; i++)
            {
                if (coefficients[i] > median)
                {
                    hash |= (1L << (63 - i));
                }
            }

            return hash;
        }
    }
}
