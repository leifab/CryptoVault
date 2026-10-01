using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace CryptoVault;

public sealed class VaultMetadataService
{
    public static List<byte[]> BuildSearchTermHashes(Dictionary<string, object> metadata, string password)
    {
        var terms = ExtractSearchTerms(metadata);
        var hashes = new List<byte[]>(terms.Count);

        foreach (string term in terms)
        {
            hashes.Add(CryptoService.ComputeSearchHash(term, password));
        }

        return hashes;
    }

    public static HashSet<string> ExtractSearchTerms(Dictionary<string, object> metadata)
    {
        var termsToIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in metadata)
        {
            termsToIndex.Add(kvp.Key);

            if (kvp.Value is IEnumerable<string> stringList)
            {
                foreach (var val in stringList)
                {
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        termsToIndex.Add(val);
                        termsToIndex.Add($"{kvp.Key}:{val}");
                    }
                }
            }
            else
            {
                string valStr = kvp.Value?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(valStr))
                {
                    if (valStr.Contains(',') || string.Equals(kvp.Key, "tags", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = valStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var part in parts)
                        {
                            termsToIndex.Add(part);
                            termsToIndex.Add($"{kvp.Key}:{part}");
                        }
                    }
                    else
                    {
                        termsToIndex.Add(valStr);
                        termsToIndex.Add($"{kvp.Key}:{valStr}");
                    }
                }
            }
        }

        return termsToIndex;
    }

    public static async Task ReparentSubtreeMetadataAsync(
        VaultRepository repository,
        List<(Guid StreamId, string OldLocator, string NewLocator)> movedItems,
        bool wasRenamed,
        string? rootOldLocator,
        string? newDisplayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var connection = await repository.CreateConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        try
        {
            foreach (var moved in movedItems)
            {
                if (wasRenamed && !string.IsNullOrEmpty(newDisplayName) && moved.OldLocator == rootOldLocator)
                {
                    // Fetch existing payload
                    byte[]? payloadBytes = null;
                    using (var getCmd = new SqlCommand("dbo.GetFileMetadata", connection, tx))
                    {
                        getCmd.CommandType = CommandType.StoredProcedure;
                        getCmd.Parameters.AddWithValue("@PathLocator", moved.OldLocator);
                        var res = await getCmd.ExecuteScalarAsync(cancellationToken);
                        if (res is byte[] b)
                        {
                            payloadBytes = b;
                        }
                    }

                    if (payloadBytes != null)
                    {
                        var meta = CryptoService.DecryptMetadata(payloadBytes, password);
                        meta["filename"] = newDisplayName;

                        string nameStem = Path.GetFileNameWithoutExtension(newDisplayName);
                        var distinctTags = nameStem
                            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        meta["tags"] = string.Join(", ", distinctTags);

                        byte[] newPayload = CryptoService.EncryptMetadata(meta, password);
                        var termHashes = BuildSearchTermHashes(meta, password);

                        var termsTable = new DataTable();
                        termsTable.Columns.Add("TermHash", typeof(byte[]));
                        foreach (byte[] hash in termHashes)
                        {
                            termsTable.Rows.Add((object)hash);
                        }

                        using (var saveCmd = new SqlCommand("dbo.SaveFileMetadata", connection, tx))
                        {
                            saveCmd.CommandType = CommandType.StoredProcedure;
                            saveCmd.Parameters.AddWithValue("@PathLocator", moved.NewLocator);
                            saveCmd.Parameters.AddWithValue("@MetadataPayload", newPayload);
                            var tvp = saveCmd.Parameters.AddWithValue("@TermHashes", termsTable);
                            tvp.SqlDbType = SqlDbType.Structured;
                            tvp.TypeName = "dbo.SearchTermHashType";
                            await saveCmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        using (var delOldCmd = new SqlCommand("dbo.DeleteFileMetadata", connection, tx))
                        {
                            delOldCmd.CommandType = CommandType.StoredProcedure;
                            delOldCmd.Parameters.AddWithValue("@PathLocator", moved.OldLocator);
                            delOldCmd.Parameters.AddWithValue("@IncludeDescendants", false);
                            await delOldCmd.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                }
                else
                {
                    // Standard move without rename (or child item of moved directory)
                    using var reparentCmd = new SqlCommand("dbo.ReparentFileMetadata", connection, tx)
                    {
                        CommandType = CommandType.StoredProcedure
                    };
                    reparentCmd.Parameters.AddWithValue("@OldLocator", moved.OldLocator);
                    reparentCmd.Parameters.AddWithValue("@NewLocator", moved.NewLocator);
                    await reparentCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task ResolveMatchDisplayNamesAsync(
        VaultRepository repository,
        List<FingerprintMatchResult> matches,
        IEnumerable<VaultItem>? cachedItems,
        string password,
        CancellationToken cancellationToken = default)
    {
        var unresolvedHashes = new HashSet<string>(matches.Select(m => m.FileId), StringComparer.OrdinalIgnoreCase);

        // 1. Check in-memory items
        if (cachedItems != null)
        {
            foreach (var item in cachedItems)
            {
                if (!string.IsNullOrEmpty(item.FileHash) && unresolvedHashes.Contains(item.FileHash))
                {
                    foreach (var m in matches.Where(m => string.Equals(m.FileId, item.FileHash, StringComparison.OrdinalIgnoreCase)))
                    {
                        m.DisplayName = item.DisplayName;
                    }
                    unresolvedHashes.Remove(item.FileHash);
                }
            }
        }

        // 2. Query dbo.GetAllFileMetadata for any remaining hashes
        if (unresolvedHashes.Count > 0)
        {
            try
            {
                var payloads = await repository.GetAllFileMetadataAsync(cancellationToken);
                foreach (byte[] bytes in payloads)
                {
                    if (unresolvedHashes.Count == 0)
                        break;

                    try
                    {
                        var meta = CryptoService.DecryptMetadata(bytes, password);
                        if (meta.TryGetValue("hash", out var h) && h != null)
                        {
                            string hashStr = h.ToString()!;
                            if (unresolvedHashes.Contains(hashStr))
                            {
                                string resolvedName = meta.TryGetValue("filename", out var fn) && fn != null
                                    ? fn.ToString()!
                                    : hashStr;

                                foreach (var m in matches.Where(m => string.Equals(m.FileId, hashStr, StringComparison.OrdinalIgnoreCase)))
                                {
                                    m.DisplayName = resolvedName;
                                }
                                unresolvedHashes.Remove(hashStr);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore decryption failures for foreign payloads
                    }
                }
            }
            catch
            {
                // Fallback gracefully if query fails
            }
        }

        // 3. Fallback to FileId if filename could not be resolved
        foreach (var m in matches)
        {
            if (string.IsNullOrEmpty(m.DisplayName))
            {
                m.DisplayName = m.FileId;
            }
        }
    }

    public static async Task SaveMetadataAsync(
        VaultRepository repository,
        string pathLocator,
        Dictionary<string, object> metadata,
        string password,
        CancellationToken cancellationToken = default)
    {
        byte[] encryptedPayload = CryptoService.EncryptMetadata(metadata, password);
        var termHashes = BuildSearchTermHashes(metadata, password);
        await repository.SaveFileMetadataAsync(pathLocator, encryptedPayload, termHashes, cancellationToken);
    }
}

