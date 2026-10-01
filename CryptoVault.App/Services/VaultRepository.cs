using System.Data;
using System.Data.SqlClient;

namespace CryptoVault;

public sealed class VaultRepository
{
    private readonly string _connectionString;

    public VaultRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public string ConnectionString => _connectionString;
    public string DatabaseName => new SqlConnectionStringBuilder(_connectionString).InitialCatalog;

    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task DeleteFileFingerprintAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.DeleteFileFingerprint", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@FileHash", fileHash);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteFileMetadataAsync(string locator, bool includeDescendants = false, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.DeleteFileMetadata", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@PathLocator", locator);
        cmd.Parameters.AddWithValue("@IncludeDescendants", includeDescendants);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteVaultFileAsync(string locator, bool includeDescendants = false, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.DeleteVaultFile", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@PathLocator", locator);
        cmd.Parameters.AddWithValue("@IncludeDescendants", includeDescendants);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<byte[]>> GetAllFileMetadataAsync(CancellationToken cancellationToken = default)
    {
        var payloads = new List<byte[]>();
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetAllFileMetadata", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                payloads.Add((byte[])reader[0]);
            }
        }

        return payloads;
    }

    public async Task<List<VaultDirectoryEntry>> GetDirectoryContentsAsync(string? parentLocator, CancellationToken cancellationToken = default)
    {
        var entries = new List<VaultDirectoryEntry>();
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetVaultDirectory", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ParentLocator", (object?)parentLocator ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.GetString(0);
            bool isDir = reader.GetBoolean(1);
            string locator = reader.GetString(2);
            string relPath = reader.GetString(3);
            Int64? size = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            byte[]? payload = reader.IsDBNull(5) ? null : (byte[])reader[5];

            entries.Add(new VaultDirectoryEntry(name, isDir, locator, relPath, payload));
        }

        return entries;
    }

    public async Task<byte[]?> GetFileMetadataPayloadAsync(string locator, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetFileMetadata", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@PathLocator", locator);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is byte[] bytes ? bytes : null;
    }

    public async Task<string?> GetPathLocatorByNameAsync(string name, string? parentLocator, bool useFallback = true, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetPathLocatorByName", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@ParentLocator", (object?)parentLocator ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UseFallback", useFallback);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? result.ToString() : null;
    }

    public async Task<string?> GetPathLocatorByStreamIdAsync(Guid streamId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetPathLocatorByStreamId", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@StreamId", streamId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? result.ToString() : null;
    }

    public async Task<List<string>> GetSampleVaultFilesAsync(int topCount = 5, CancellationToken cancellationToken = default)
    {
        var sampleNames = new List<string>();
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetSampleVaultFiles", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@TopCount", topCount);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sampleNames.Add(reader.GetString(0));
        }

        return sampleNames;
    }

    public async Task<List<byte[]>> GetSubtreeFileMetadataAsync(string rootLocator, CancellationToken cancellationToken = default)
    {
        var payloads = new List<byte[]>();
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetSubtreeFileMetadata", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@RootLocator", rootLocator);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                payloads.Add((byte[])reader[0]);
            }
        }

        return payloads;
    }

    public async Task<List<SubtreeLocatorEntry>> GetSubtreeLocatorsAsync(string rootLocator, CancellationToken cancellationToken = default)
    {
        var entries = new List<SubtreeLocatorEntry>();
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetVaultSubtreeLocators", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@RootLocator", rootLocator);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new SubtreeLocatorEntry(reader.GetGuid(0), reader.GetString(1)));
        }

        return entries;
    }

    public async Task<string?> GetVaultConfigAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetVaultConfig", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ConfigKey", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? result.ToString() : null;
    }

    public async Task<string?> GetVaultItemPathAsync(string locator, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetVaultItemPath", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@PathLocator", locator);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? result.ToString() : null;
    }

    public async Task<VaultRootInfo> GetVaultRootAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.GetVaultRoot", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string tableName = reader.GetString(0);
            string uncPath = reader.GetString(1);
            return new VaultRootInfo(tableName, uncPath);
        }

        throw new InvalidOperationException("dbo.GetVaultRoot returned no rows.");
    }

    public async Task ReparentFileMetadataAsync(
        string oldLocator,
        string newLocator,
        byte[]? newPayload = null,
        SqlConnection? externalConnection = null,
        SqlTransaction? externalTransaction = null,
        CancellationToken cancellationToken = default)
    {
        SqlConnection conn = externalConnection ?? await CreateConnectionAsync(cancellationToken);
        try
        {
            using var cmd = new SqlCommand("dbo.ReparentFileMetadata", conn, externalTransaction)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@OldLocator", oldLocator);
            cmd.Parameters.AddWithValue("@NewLocator", newLocator);
            cmd.Parameters.AddWithValue("@NewPayload", (object?)newPayload ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (externalConnection == null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task SaveFileMetadataAsync(
        string locator,
        byte[] payload,
        IEnumerable<byte[]> searchTokens,
        CancellationToken cancellationToken = default)
    {
        var termsTable = new DataTable();
        termsTable.Columns.Add("TermHash", typeof(byte[]));

        foreach (byte[] token in searchTokens)
        {
            termsTable.Rows.Add(token);
        }

        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.SaveFileMetadata", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@PathLocator", locator);
        cmd.Parameters.AddWithValue("@MetadataPayload", payload);

        var tvpParam = new SqlParameter("@TermHashes", SqlDbType.Structured)
        {
            TypeName = "dbo.SearchTermHashType",
            Value = termsTable
        };
        cmd.Parameters.Add(tvpParam);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetVaultConfigAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var cmd = new SqlCommand("dbo.SetVaultConfig", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ConfigKey", key);
        cmd.Parameters.AddWithValue("@ConfigValue", value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public sealed record SubtreeLocatorEntry(Guid StreamId, string Locator);
    public sealed record VaultDirectoryEntry(string Name, bool IsDirectory, string Locator, string RelPath, byte[]? MetadataPayload);
    public sealed record VaultRootInfo(string TableName, string UncPath);
}
