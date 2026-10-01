using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CryptoVault;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "MainWindow is a top-level WPF Window whose lifetime is managed by Application shutdown; _dirLoadLock is explicitly disposed in OnMainWindowClosed.")]
public partial class MainWindow : Window
{
    private int _activeImportBatches;
    private string? _connectionString;
    private string? _currentLocator;
    private string _currentSortColumn = "Type";
    private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
    private string _currentVirtualPath = "/";
    // Serializes directory queries so rapid UI navigation events (e.g. double-click spam)
    // cannot interleave async SQL fetches and corrupt the VaultListView state.
    private readonly SemaphoreSlim _dirLoadLock = new(1, 1);
    private string? _encryptionPassword;
    private string? _fileTableName;
    private string? _fileTableUncPath;
    private readonly Stack<(string? Locator, string Path)> _navHistory = new();
    private VaultRepository? _repository;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "CryptoVault_" + Guid.NewGuid().ToString("N"));

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnMainWindowClosed;
    }

    private void ApplySort()
    {
        if (VaultListView.ItemsSource == null)
            return;

        var view = CollectionViewSource.GetDefaultView(VaultListView.ItemsSource) as ListCollectionView;
        if (view != null)
        {
            view.CustomSort = new VaultItemComparer(_currentSortColumn, _currentSortDirection);
        }

        UpdateHeaderIndicators();
    }

    private async Task DeleteSelectedItemsAsync()
    {
        if (_activeImportBatches > 0)
        {
            MessageBox.Show(
                "File deletion is disabled while an import batch is in progress.\nPlease wait for the current operation to finish.",
                "Import in Progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectedItems = VaultListView.SelectedItems
            .OfType<VaultItem>()
            .Where(item => item.DisplayName != ".." && !string.IsNullOrEmpty(item.PathLocator))
            .ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Please select one or more files or folders to delete.", "Delete Item", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string promptMessage = selectedItems.Count == 1
            ? (selectedItems[0].IsDirectory
                ? $"Are you sure you want to permanently delete folder '{selectedItems[0].DisplayName}' and all its contents?\n\nThis action cannot be undone."
                : $"Are you sure you want to permanently delete file '{selectedItems[0].DisplayName}'?\n\nThis action cannot be undone.")
            : $"Are you sure you want to permanently delete {selectedItems.Count} selected item(s) and any nested contents?\n\nThis action cannot be undone.";

        var confirm = MessageBox.Show(
            promptMessage,
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;
        StatusTextBlock.Text = $"Deleting {selectedItems.Count} item(s)...";

        try
        {
            string? currentFolder = await GetCurrentPhysicalPathAsync();
            int deletedCount = 0;

            foreach (var selected in selectedItems)
            {
                // 1. Delete physical file or directory from FileTable UNC path
                if (!string.IsNullOrEmpty(currentFolder))
                {
                    string physicalPath = Path.Combine(currentFolder, selected.RawName);
                    try
                    {
                        FileTableService.PhysicalDelete(physicalPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete physical path '{physicalPath}': {ex.Message}");
                    }
                }

                // 2. Clean up SQL fingerprints, metadata & index entries (and ensure FileTable row is gone)
                if (_repository != null)
                {
                    var hashesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(selected.FileHash))
                    {
                        hashesToDelete.Add(selected.FileHash);
                    }

                    if (selected.IsDirectory || hashesToDelete.Count == 0)
                    {
                        var payloads = await _repository.GetSubtreeFileMetadataAsync(selected.PathLocator!);
                        foreach (var bytes in payloads)
                        {
                            if (!string.IsNullOrEmpty(_encryptionPassword))
                            {
                                try
                                {
                                    var meta = CryptoService.DecryptMetadata(bytes, _encryptionPassword);
                                    if (meta.TryGetValue("hash", out var h) && h != null)
                                    {
                                        string hashStr = h.ToString()?.Trim() ?? "";
                                        if (!string.IsNullOrEmpty(hashStr))
                                        {
                                            hashesToDelete.Add(hashStr);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Suppress if payload cannot be decrypted
                                }
                            }
                        }
                    }

                    if (hashesToDelete.Count > 0)
                    {
                        foreach (var hash in hashesToDelete)
                        {
                            await _repository.DeleteFileFingerprintAsync(hash);
                        }
                    }

                    await _repository.DeleteFileMetadataAsync(selected.PathLocator!, includeDescendants: true);
                    await _repository.DeleteVaultFileAsync(selected.PathLocator!, includeDescendants: true);
                }

                deletedCount++;
            }

            StatusTextBlock.Text = $"Deleted {deletedCount} item(s).";
            await LoadDirectoryContentsAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Failed to delete item(s).";
            MessageBox.Show($"Failed to delete item(s):\n{ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = false;
        }
    }

    private async Task ExecuteMoveBatchAsync(
        List<VaultItem> itemsToMove,
        string? targetLocator,
        string targetPhysicalFolder,
        string targetVirtualPath)
    {
        if (_repository == null || string.IsNullOrEmpty(_encryptionPassword))
            return;

        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;
        StatusTextBlock.Text = $"Preparing to move {itemsToMove.Count} item(s)...";

        try
        {
            Directory.CreateDirectory(targetPhysicalFolder);
            string? currentPhysicalFolder = await GetCurrentPhysicalPathAsync();
            if (string.IsNullOrEmpty(currentPhysicalFolder))
            {
                throw new InvalidOperationException("Could not resolve current physical folder path.");
            }

            // 1. Query all existing children in destination folder to detect display name collisions
            var rawDestItems = await _repository.GetDirectoryContentsAsync(targetLocator);
            var destItems = new List<(string DisplayName, string RawName, bool IsDirectory, string Locator, string? FileHash)>();

            foreach (var r in rawDestItems)
            {
                string? hash = null;
                if (!r.IsDirectory && r.MetadataPayload != null)
                {
                    try
                    {
                        var meta = CryptoService.DecryptMetadata(r.MetadataPayload, _encryptionPassword);
                        if (meta.TryGetValue("hash", out var h) && h != null)
                        {
                            hash = h.ToString();
                        }
                    }
                    catch { }
                }

                string dName = FileTableService.DecryptItemName(r.Name, _encryptionPassword);
                destItems.Add((dName, r.Name, r.IsDirectory, r.Locator, hash));
            }

            var destDisplayNames = new HashSet<string>(destItems.Select(d => d.DisplayName), StringComparer.OrdinalIgnoreCase);

            bool applyToAll = false;
            CollisionResolution? batchResolution = null;
            int movedCount = 0;
            int skippedCount = 0;
            var errors = new List<string>();

            for (int i = 0; i < itemsToMove.Count; i++)
            {
                var item = itemsToMove[i];
                StatusTextBlock.Text = $"Moving ({i + 1} of {itemsToMove.Count}): {item.DisplayName}...";

                bool wasRenamed = false;
                string newDisplayName = item.DisplayName;
                string targetRawName = item.RawName;

                // Check for display name collision in destination folder
                if (destDisplayNames.Contains(item.DisplayName))
                {
                    CollisionResolution resolution;
                    string autoSuggestedName = FileTableService.GenerateUniqueDisplayName(item.DisplayName, destDisplayNames, item.IsDirectory);

                    if (applyToAll && batchResolution.HasValue)
                    {
                        resolution = batchResolution.Value;
                    }
                    else
                    {
                        bool moreConflictsPossible = (itemsToMove.Count - i) > 1;
                        var conflictDialog = new CollisionDialog(item.DisplayName, autoSuggestedName, item.IsDirectory, moreConflictsPossible)
                        {
                            Owner = this
                        };

                        if (conflictDialog.ShowDialog() != true || conflictDialog.Resolution == CollisionResolution.Cancel)
                        {
                            // User canceled the move batch
                            break;
                        }

                        resolution = conflictDialog.Resolution;
                        if (conflictDialog.ApplyToAll)
                        {
                            applyToAll = true;
                            batchResolution = resolution;
                        }

                        if (resolution == CollisionResolution.CustomRename)
                        {
                            newDisplayName = conflictDialog.NewName;
                        }
                    }

                    if (resolution == CollisionResolution.Cancel)
                    {
                        break;
                    }

                    if (resolution == CollisionResolution.Skip)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (resolution == CollisionResolution.Overwrite)
                    {
                        // Overwrite destination file: clean up destination file first
                        var existing = destItems.FirstOrDefault(d => string.Equals(d.DisplayName, item.DisplayName, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(existing.Locator))
                        {
                            string existingPhysical = Path.Combine(targetPhysicalFolder, existing.RawName);
                            FileTableService.PhysicalDelete(existingPhysical);

                            if (!string.IsNullOrEmpty(existing.FileHash))
                            {
                                await _repository.DeleteFileFingerprintAsync(existing.FileHash);
                            }

                            await _repository.DeleteFileMetadataAsync(existing.Locator, includeDescendants: false);
                            await _repository.DeleteVaultFileAsync(existing.Locator, includeDescendants: false);

                            destItems.Remove(existing);
                            destDisplayNames.Remove(existing.DisplayName);
                        }
                    }
                    else if (resolution == CollisionResolution.AutoRename || resolution == CollisionResolution.CustomRename)
                    {
                        if (resolution == CollisionResolution.AutoRename)
                        {
                            newDisplayName = autoSuggestedName;
                        }

                        wasRenamed = true;
                        string encBase64 = CryptoService.EncryptString(newDisplayName, _encryptionPassword);
                        targetRawName = encBase64.Replace('+', '-').Replace('/', '_').TrimEnd('=') + (item.IsDirectory ? "" : ".enc");
                    }
                }

                string sourcePhysical = Path.Combine(currentPhysicalFolder, item.RawName);
                string destPhysical = Path.Combine(targetPhysicalFolder, targetRawName);

                if (item.IsDirectory ? !Directory.Exists(sourcePhysical) : !File.Exists(sourcePhysical))
                {
                    errors.Add($"Source item not found on disk: {item.DisplayName}");
                    continue;
                }

                // 2. Capture pre-move (stream_id, OldLocator) for item (and all descendants if folder)
                var subtreeEntries = await _repository.GetSubtreeLocatorsAsync(item.PathLocator!);
                var capturedItems = subtreeEntries.Select(s => (s.StreamId, s.Locator)).ToList();

                // 3. Perform physical move via UNC share
                try
                {
                    FileTableService.PhysicalMove(sourcePhysical, destPhysical);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to move '{item.DisplayName}': {ex.Message}");
                    continue;
                }

                // 4. Resolve post-move NewLocator values by stream_id with resilient retries
                var resolvedLocators = new List<(Guid StreamId, string OldLocator, string NewLocator)>();
                foreach (var captured in capturedItems)
                {
                    string? newLoc = await FileTableService.GetPathLocatorByStreamIdWithRetryAsync(_repository, captured.StreamId, captured.Locator);
                    if (newLoc != null)
                    {
                        resolvedLocators.Add((captured.StreamId, captured.Locator, newLoc));
                    }
                }

                // 5. Update metadata tables atomically within a SQL transaction
                await VaultMetadataService.ReparentSubtreeMetadataAsync(
                    _repository,
                    resolvedLocators,
                    wasRenamed,
                    item.PathLocator!,
                    newDisplayName,
                    _encryptionPassword);

                destDisplayNames.Add(newDisplayName);
                movedCount++;
            }

            StatusTextBlock.Text = errors.Count == 0
                ? $"Successfully moved {movedCount} item(s) to '{targetVirtualPath}'."
                : $"Moved {movedCount} item(s) with {errors.Count} error(s).";

            if (errors.Count > 0)
            {
                string summary = string.Join("\n", errors.Take(10));
                if (errors.Count > 10)
                {
                    summary += $"\n...and {errors.Count - 10} more errors.";
                }
                MessageBox.Show($"Move completed with errors:\n\n{summary}", "Move Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadDirectoryContentsAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Move operation failed.";
            MessageBox.Show($"Error during move operation:\n{ex.Message}", "Move Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = false;
        }
    }

    private async Task<string?> GetCurrentPhysicalPathAsync()
    {
        if (_repository == null || string.IsNullOrEmpty(_fileTableUncPath))
            return null;

        if (_currentLocator == null)
            return _fileTableUncPath;

        try
        {
            var fullPath = await _repository.GetVaultItemPathAsync(_currentLocator);
            return !string.IsNullOrEmpty(fullPath) ? fullPath : _fileTableUncPath;
        }
        catch
        {
            return _fileTableUncPath;
        }
    }

    private async Task LoadDirectoryContentsAsync()
    {
        await _dirLoadLock.WaitAsync();
        try
        {
            if (_repository == null || string.IsNullOrEmpty(_fileTableName))
                return;

            string? requestedLocator = _currentLocator;
            CurrentPathTextBlock.Text = _currentVirtualPath;
            var items = new List<VaultItem>();

            // If we are in a subfolder, offer the ".." navigation item
            if (requestedLocator != null)
            {
                items.Add(new VaultItem
                {
                    DisplayName = "..",
                    IsDirectory = true,
                    PathLocator = null,
                    RawName = ".."
                });
            }

            var entries = await _repository.GetDirectoryContentsAsync(requestedLocator);
            foreach (var entry in entries)
            {
                string? fileHash = null;

                if (!entry.IsDirectory && entry.MetadataPayload != null)
                {
                    try
                    {
                        var metadata = CryptoService.DecryptMetadata(entry.MetadataPayload, _encryptionPassword!);
                        if (metadata.TryGetValue("hash", out var h) && h != null)
                        {
                            fileHash = h.ToString();
                        }
                    }
                    catch
                    {
                        // Payload might not be encrypted with current password or is invalid
                    }
                }

                string displayName = FileTableService.DecryptItemName(entry.Name, _encryptionPassword!);

                items.Add(new VaultItem
                {
                    DisplayName = displayName,
                    RawName = entry.Name,
                    IsDirectory = entry.IsDirectory,
                    PathLocator = entry.Locator,
                    RelativePath = entry.RelPath,
                    FileHash = fileHash
                });
            }

            // Only update the list view if the current directory locator hasn't changed while we were fetching
            if (_currentLocator == requestedLocator)
            {
                VaultListView.ItemsSource = items;
                ApplySort();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load folder items:\n{ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _dirLoadLock.Release();
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutDialog { Owner = this };
        about.ShowDialog();
    }

    private async void OnConnectRepoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectDialog { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            StatusTextBlock.Text = "Connecting to repository...";

            try
            {
                _connectionString = dialog.ConnectionString;
                _repository = new VaultRepository(_connectionString);

                var vaultRoot = await _repository.GetVaultRootAsync();
                if (vaultRoot == null)
                {
                    StatusTextBlock.Text = "Connected, but no FileTable found.";
                    MessageBox.Show(
                        "Connected to the database, but no FileTable was found.\nPlease ensure FileTable is configured.",
                        "No FileTable Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    UpdateConnectionState(isConnected: true, _repository.DatabaseName);

                    _currentLocator = null;
                    _currentVirtualPath = "/";
                    _navHistory.Clear();
                    VaultListView.ItemsSource = null;
                    CurrentPathTextBlock.Text = "/";
                    return;
                }

                _fileTableName = vaultRoot.TableName;
                _fileTableUncPath = vaultRoot.UncPath;

                bool passwordVerified = false;
                while (!passwordVerified)
                {
                    var passDialog = new PasswordDialog { Owner = this };
                    if (passDialog.ShowDialog() != true)
                    {
                        _connectionString = null;
                        _repository = null;
                        _fileTableUncPath = null;
                        _fileTableName = null;
                        _encryptionPassword = null;
                        UpdateConnectionState(isConnected: false);
                        StatusTextBlock.Text = "Connection aborted: Password required.";
                        return;
                    }

                    string enteredPassword = passDialog.Password;
                    StatusTextBlock.Text = "Verifying repository password...";

                    var (isValid, isNewVault) = await VaultAuthService.VerifyOrInitializePasswordAsync(
                        _repository,
                        enteredPassword);

                    if (isValid)
                    {
                        _encryptionPassword = enteredPassword;
                        passwordVerified = true;
                        UpdateConnectionState(isConnected: true, _repository.DatabaseName);

                        if (isNewVault)
                        {
                            StatusTextBlock.Text = "New repository initialized with master password.";
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "The password entered is incorrect for this repository.\nPlease check your password and try again.",
                            "Incorrect Password",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }

                _currentLocator = null;
                _currentVirtualPath = "/";
                _navHistory.Clear();
                await LoadDirectoryContentsAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Connection failed.";
                MessageBox.Show($"Could not connect to database:\n{ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var selected = VaultListView.SelectedItems.OfType<VaultItem>().ToList();
        bool hasSelection = selected.Count > 0 && selected.All(i => i.DisplayName != "..");
        bool singleSelection = selected.Count == 1 && selected[0].DisplayName != "..";

        DecryptToMenuItem.IsEnabled = hasSelection;
        DeleteMenuItem.IsEnabled = hasSelection;
        MoveToMenuItem.IsEnabled = hasSelection;
        MetadataMenuItem.IsEnabled = singleSelection;
    }

    private async void OnDecryptToClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_encryptionPassword))
        {
            MessageBox.Show("Repository is not connected or unlocked.", "Encryption Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedItems = VaultListView.SelectedItems
            .OfType<VaultItem>()
            .Where(item => item.DisplayName != "..")
            .ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Please select one or more files or folders to decrypt.", "Decrypt Items", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? currentFolder = await GetCurrentPhysicalPathAsync();
        if (string.IsNullOrEmpty(currentFolder))
        {
            MessageBox.Show("Could not resolve current vault physical path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Destination Folder for Decryption",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        string destinationFolder = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(destinationFolder) || !Directory.Exists(destinationFolder))
        {
            return;
        }

        OperationProgressBar.Visibility = Visibility.Visible;

        try
        {
            var exportItems = selectedItems.Select(item =>
                (Path.Combine(currentFolder, item.RawName), item.IsDirectory, item.DisplayName));

            var progress = new Progress<VaultExportService.ExportProgressReport>(report =>
            {
                StatusTextBlock.Text = $"Decrypting ({report.CurrentFileIndex} of {report.TotalFiles}): {report.DisplayName}...";
                double overallPercent = ((report.CurrentFileIndex - 1 + (report.CurrentFileProgressPercent / 100.0)) / report.TotalFiles) * 100.0;
                OperationProgressBar.Value = overallPercent;
            });

            int totalExported = await VaultExportService.ExportItemsAsync(exportItems, destinationFolder, _encryptionPassword, progress);

            OperationProgressBar.Value = 100;
            StatusTextBlock.Text = $"Successfully decrypted {totalExported} file(s) to '{destinationFolder}'.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Decryption failed.";
            MessageBox.Show($"An unexpected error occurred during decryption:\n{ex.Message}", "Decryption Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = false;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedItemsAsync();
    }

    private void OnDisconnectRepoClick(object sender, RoutedEventArgs e)
    {
        _connectionString = null;
        _repository = null;
        _fileTableName = null;
        _fileTableUncPath = null;
        _encryptionPassword = null;
        _currentLocator = null;
        _currentVirtualPath = "/";
        _navHistory.Clear();

        UpdateConnectionState(isConnected: false);
        VaultListView.ItemsSource = null;
        CurrentPathTextBlock.Text = "/";
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnGridViewColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null)
            return;

        string? headerString = header.Column.Header as string;
        if (string.IsNullOrEmpty(headerString))
            return;

        string cleanHeader = headerString.Replace(" ▲", "").Replace(" ▼", "").Trim();

        if (string.Equals(_currentSortColumn, cleanHeader, StringComparison.OrdinalIgnoreCase))
        {
            _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _currentSortColumn = cleanHeader;
            _currentSortDirection = ListSortDirection.Ascending;
        }

        ApplySort();
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _dirLoadLock.Dispose();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete temp directory: {ex.Message}");
        }
    }

    private async void OnMetadataClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = VaultListView.SelectedItems
            .OfType<VaultItem>()
            .Where(item => item.DisplayName != "..")
            .ToList();

        if (selectedItems.Count != 1 || string.IsNullOrEmpty(selectedItems[0].PathLocator))
        {
            MessageBox.Show("Please select exactly one file or folder first.", "Select Item", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = selectedItems[0];

        if (_repository == null || string.IsNullOrEmpty(_encryptionPassword))
            return;

        Dictionary<string, object> currentMetadata = new();

        try
        {
            var bytes = await _repository.GetFileMetadataPayloadAsync(selected.PathLocator!);
            if (bytes != null)
            {
                currentMetadata = CryptoService.DecryptMetadata(bytes, _encryptionPassword);
            }

            var dialog = new MetadataDialog(selected.DisplayName, currentMetadata) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var updatedMetadata = dialog.GetMetadataDictionary();
            await VaultMetadataService.SaveMetadataAsync(_repository, selected.PathLocator!, updatedMetadata, _encryptionPassword);
            StatusTextBlock.Text = $"Metadata saved for {selected.DisplayName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load or save metadata:\n{ex.Message}", "Metadata Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnMoveToClick(object sender, RoutedEventArgs e)
    {
        if (_repository == null || string.IsNullOrEmpty(_encryptionPassword) ||
            string.IsNullOrEmpty(_fileTableName) || string.IsNullOrEmpty(_fileTableUncPath))
        {
            MessageBox.Show("Repository is not connected or unlocked.", "Move Item", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_activeImportBatches > 0)
        {
            MessageBox.Show(
                "Moving items is disabled while an import batch is in progress.\nPlease wait for the current operation to finish.",
                "Import in Progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectedItems = VaultListView.SelectedItems
            .OfType<VaultItem>()
            .Where(item => item.DisplayName != ".." && !string.IsNullOrEmpty(item.PathLocator))
            .ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Please select one or more files or folders to move.", "Move Item", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Circular Move Prevention (Anti-Corruption Shield):
        // Moving a folder into itself or any of its descendants creates an infinite hierarchyid cycle,
        // causing orphaned subtrees and recursion faults in FileTable traversal.
        // We query dbo.GetVaultSubtreeLocators to exclude the entire subtree from target selection.
        var excludedLocators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderItems = selectedItems.Where(i => i.IsDirectory).ToList();

        if (folderItems.Count > 0)
        {
            try
            {
                foreach (var folder in folderItems)
                {
                    var subtree = await _repository.GetSubtreeLocatorsAsync(folder.PathLocator!);
                    foreach (var entry in subtree)
                    {
                        excludedLocators.Add(entry.Locator);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to query excluded descendant locators: {ex.Message}");
            }
        }

        var folderDialog = new FolderPickerDialog(
            _connectionString!,
            _fileTableName,
            _fileTableUncPath,
            _encryptionPassword,
            excludedLocators)
        {
            Owner = this
        };

        if (folderDialog.ShowDialog() != true)
            return;

        string? targetLocator = folderDialog.SelectedLocator;
        string targetPhysicalFolder = folderDialog.SelectedPhysicalPath;
        string targetVirtualPath = folderDialog.SelectedVirtualPath;

        // Verify that target is not the same folder where items currently reside
        if (string.Equals(targetLocator, _currentLocator, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The selected items are already in this folder.", "Move to...", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExecuteMoveBatchAsync(selectedItems, targetLocator, targetPhysicalFolder, targetVirtualPath);
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        if (_repository == null || string.IsNullOrEmpty(_encryptionPassword))
        {
            MessageBox.Show("Please connect to a repository first.", "Repository Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new InputDialog("New Folder", "Enter new folder name:") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string plainFolderName = dialog.InputText;
            string safeEncFolderName = FileTableService.EncodeDirectoryName(plainFolderName, _encryptionPassword);

            string? targetDirectory = await GetCurrentPhysicalPathAsync();
            if (string.IsNullOrEmpty(targetDirectory))
                throw new InvalidOperationException("Could not determine current repository directory.");

            string fullNewFolderPath = Path.Combine(targetDirectory, safeEncFolderName);
            Directory.CreateDirectory(fullNewFolderPath);

            await LoadDirectoryContentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadDirectoryContentsAsync();
    }

    private async void OnVaultListViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            await DeleteSelectedItemsAsync();
        }
    }

    private async void OnVaultListViewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 1. Check what was clicked in the visual tree
        DependencyObject? current = e.OriginalSource as DependencyObject;
        ListViewItem? lvi = null;

        while (current != null && current != VaultListView)
        {
            // If double-clicked on a column header, ignore (headers handle their own sorting)
            if (current is GridViewColumnHeader)
                return;

            if (current is ListViewItem item)
            {
                lvi = item;
                break;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        if (lvi == null || VaultListView.SelectedItem is not VaultItem selected)
            return;

        // 2. Identify which column was clicked
        Point posInRow = e.GetPosition(lvi);
        double typeWidth = TypeColumn?.ActualWidth ?? 70;
        double nameWidth = NameColumn?.ActualWidth ?? 200;

        bool isHashColumn = posInRow.X >= (typeWidth + nameWidth);
        bool isNameColumn = posInRow.X >= typeWidth && posInRow.X < (typeWidth + nameWidth);

        // Also check GridViewRowPresenter child index for fine-grained accuracy
        DependencyObject? cell = e.OriginalSource as DependencyObject;
        while (cell != null && cell != lvi)
        {
            var parent = VisualTreeHelper.GetParent(cell);
            if (parent is GridViewRowPresenter presenter)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(presenter); i++)
                {
                    if (VisualTreeHelper.GetChild(presenter, i) == cell)
                    {
                        if (i == 2) { isHashColumn = true; isNameColumn = false; }
                        else if (i == 1) { isNameColumn = true; isHashColumn = false; }
                        break;
                    }
                }
                break;
            }
            cell = parent;
        }

        // 3. Handle folder navigation
        if (selected.IsDirectory)
        {
            if (selected.DisplayName == "..")
            {
                if (_navHistory.Count > 0)
                {
                    var previous = _navHistory.Pop();
                    _currentLocator = previous.Locator;
                    _currentVirtualPath = previous.Path;
                    await LoadDirectoryContentsAsync();
                }
                return;
            }

            _navHistory.Push((_currentLocator, _currentVirtualPath));
            _currentLocator = selected.PathLocator;
            _currentVirtualPath = $"{_currentVirtualPath.TrimEnd('/')}/{selected.DisplayName}/";
            await LoadDirectoryContentsAsync();
            return;
        }

        // 4. Handle files: double clicking on the Hash column copies hash to clipboard
        if (isHashColumn)
        {
            if (!string.IsNullOrWhiteSpace(selected.FileHash))
            {
                try
                {
                    Clipboard.SetText(selected.FileHash);
                    StatusTextBlock.Text = "Hash value copied to clipboard.";
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Failed to copy hash to clipboard.";
                    Debug.WriteLine($"Clipboard error: {ex.Message}");
                }
            }
            else
            {
                StatusTextBlock.Text = "No hash available for this file.";
            }
            return;
        }

        // 5. Handle files: only open/decrypt if double clicked on the Name column
        if (!isNameColumn)
        {
            return;
        }

        // Decrypt and open file with default registered application
        if (string.IsNullOrEmpty(_encryptionPassword))
            return;

        string? currentFolder = await GetCurrentPhysicalPathAsync();
        if (string.IsNullOrEmpty(currentFolder))
            return;

        string sourceEncryptedPath = Path.Combine(currentFolder, selected.RawName);
        if (!File.Exists(sourceEncryptedPath))
        {
            MessageBox.Show("Source encrypted file not found on disk.", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Directory.CreateDirectory(_tempDirectory);
        string destinationTempPath = Path.Combine(_tempDirectory, selected.DisplayName);

        OperationProgressBar.Value = 0;
        OperationProgressBar.Visibility = Visibility.Visible;

        var progress = new Progress<double>(percent =>
        {
            OperationProgressBar.Value = percent;
        });

        try
        {
            StatusTextBlock.Text = $"Opening {selected.DisplayName}...";
            await CryptoService.DecryptFileAsync(sourceEncryptedPath, destinationTempPath, _encryptionPassword, progress);

            Process.Start(new ProcessStartInfo
            {
                FileName = destinationTempPath,
                UseShellExecute = true
            });

            StatusTextBlock.Text = $"Opened: {selected.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Failed to open file.";
            MessageBox.Show($"Failed to decrypt or launch file:\n{ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnVaultListViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(VaultListView, e.GetPosition(VaultListView));
        if (hit == null)
        {
            VaultListView.SelectedItems.Clear();
            return;
        }

        DependencyObject? current = hit.VisualHit;
        while (current != null && current != VaultListView)
        {
            if (current is ListViewItem ||
                current is GridViewColumnHeader ||
                current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        VaultListView.SelectedItems.Clear();
    }

    private void OnVaultListViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (VaultListView.View is GridView && TypeColumn != null && NameColumn != null && HashColumn != null)
        {
            // Subtract Type column width, Hash column width, vertical scrollbar allowance (~30px), and border/padding
            double fixedWidths = TypeColumn.ActualWidth + HashColumn.ActualWidth + 30;
            double remainingWidth = VaultListView.ActualWidth - fixedWidths;

            if (remainingWidth > 100)
            {
                NameColumn.Width = remainingWidth;
            }
        }
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (_repository == null || string.IsNullOrEmpty(_fileTableName) || string.IsNullOrEmpty(_fileTableUncPath) || string.IsNullOrEmpty(_encryptionPassword))
        {
            MessageBox.Show(
                "You must connect to a repository before dropping files.",
                "Repository Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        string[]? files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0)
            return;

        string? dropTargetLocator = _currentLocator;
        string? dropTargetDirectory = await GetCurrentPhysicalPathAsync();
        if (string.IsNullOrEmpty(dropTargetDirectory))
        {
            MessageBox.Show("Could not resolve current repository directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Interlocked.Increment(ref _activeImportBatches);

        try
        {
            OperationProgressBar.Visibility = Visibility.Visible;
            var progress = new Progress<VaultImportService.ImportProgressReport>(report =>
            {
                StatusTextBlock.Text = report.StatusMessage;
                OperationProgressBar.IsIndeterminate = report.IsIndeterminate;
                if (!report.IsIndeterminate)
                {
                    OperationProgressBar.Value = report.PercentComplete;
                }
            });

            var result = await VaultImportService.ImportFilesAsync(
                _repository,
                files,
                dropTargetLocator,
                dropTargetDirectory,
                _encryptionPassword,
                duplicateConfirmationCallback: (fileName, matches) =>
                {
                    var dlg = new DuplicateMatchDialog(fileName, matches) { Owner = this };
                    return Task.FromResult(dlg.ShowDialog() == true);
                },
                cachedItems: VaultListView.ItemsSource as IEnumerable<VaultItem>,
                progress: progress);

            OperationProgressBar.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = result.ErrorCount == 0
                ? $"Successfully processed {result.ProcessedCount} file(s)."
                : $"Processed {result.ProcessedCount} file(s) with {result.ErrorCount} error(s).";

            await LoadDirectoryContentsAsync();
        }
        finally
        {
            Interlocked.Decrement(ref _activeImportBatches);
        }
    }

    private void UpdateConnectionState(bool isConnected, string? databaseName = null)
    {
        ConnectMenuItem.IsEnabled = !isConnected;
        DisconnectMenuItem.IsEnabled = isConnected;

        StatusTextBlock.Text = isConnected
            ? $"Connected to SQL: {databaseName} (FileTable: {_fileTableUncPath})"
            : "Disconnected from repository.";
    }

    private void UpdateHeaderIndicators()
    {
        UpdateSingleHeader(TypeColumn, "Type");
        UpdateSingleHeader(NameColumn, "Name");
        UpdateSingleHeader(HashColumn, "Hash");
    }

    private void UpdateSingleHeader(GridViewColumn? column, string baseName)
    {
        if (column == null) return;
        if (string.Equals(_currentSortColumn, baseName, StringComparison.OrdinalIgnoreCase))
        {
            string arrow = _currentSortDirection == ListSortDirection.Ascending ? " ▲" : " ▼";
            column.Header = baseName + arrow;
        }
        else
        {
            column.Header = baseName;
        }
    }

    private sealed class VaultItemComparer : System.Collections.IComparer
    {
        private readonly string _column;
        private readonly ListSortDirection _direction;

        public VaultItemComparer(string column, ListSortDirection direction)
        {
            _column = column;
            _direction = direction;
        }

        public int Compare(object? x, object? y)
        {
            if (x is not VaultItem itemX || y is not VaultItem itemY)
                return 0;

            // ".." is always pinned to the very top
            if (itemX.DisplayName == "..") return -1;
            if (itemY.DisplayName == "..") return 1;

            // Folders always grouped before files
            if (itemX.IsDirectory && !itemY.IsDirectory) return -1;
            if (!itemX.IsDirectory && itemY.IsDirectory) return 1;

            int result = 0;
            if (_column.StartsWith("Type", StringComparison.OrdinalIgnoreCase))
            {
                result = string.Compare(itemX.ItemType, itemY.ItemType, StringComparison.OrdinalIgnoreCase);
            }
            else if (_column.StartsWith("Hash", StringComparison.OrdinalIgnoreCase))
            {
                result = string.Compare(itemX.FileHash ?? string.Empty, itemY.FileHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                result = string.Compare(itemX.DisplayName, itemY.DisplayName, StringComparison.OrdinalIgnoreCase);
            }

            // Secondary tie-breaker by display name
            if (result == 0)
            {
                result = string.Compare(itemX.DisplayName, itemY.DisplayName, StringComparison.OrdinalIgnoreCase);
            }

            if (_direction == ListSortDirection.Descending)
            {
                result = -result;
            }

            return result;
        }
    }
}
