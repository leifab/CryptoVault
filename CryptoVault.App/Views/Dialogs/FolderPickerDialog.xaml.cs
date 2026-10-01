using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace CryptoVault;

public class FolderPickerItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PathLocator { get; set; }
    public string RawName { get; set; } = string.Empty;
}

public partial class FolderPickerDialog : Window
{
    private readonly string _connectionString;
    private string? _currentLocator;
    private string _currentPhysicalPath;
    private string _currentVirtualPath = "/";
    private readonly string _encryptionPassword;
    private readonly HashSet<string> _excludedLocators;
    private readonly string _fileTableName;
    private readonly Stack<(string? Locator, string VirtualPath, string PhysicalPath)> _navHistory = new();

    public string? SelectedLocator { get; private set; }
    public string SelectedPhysicalPath { get; private set; } = string.Empty;
    public string SelectedVirtualPath { get; private set; } = "/";

    public FolderPickerDialog(
        string connectionString,
        string fileTableName,
        string rootUncPath,
        string encryptionPassword,
        HashSet<string>? excludedLocators = null)
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);

        _connectionString = connectionString;
        _fileTableName = fileTableName;
        _currentPhysicalPath = rootUncPath;
        _encryptionPassword = encryptionPassword;
        _excludedLocators = excludedLocators != null
            ? new HashSet<string>(excludedLocators, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Loaded += async (_, _) => await LoadFoldersAsync();
    }

    private string DecryptFolderName(string rawName)
    {
        try
        {
            // Revert URL-safe base64 ('-' and '_' back to '+' and '/') and restore padding
            string base64 = rawName.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return CryptoService.DecryptString(base64, _encryptionPassword);
        }
        catch
        {
            return rawName;
        }
    }

    private async Task LoadFoldersAsync()
    {
        CurrentPathTextBlock.Text = $"Target: {_currentVirtualPath}";
        UpButton.IsEnabled = _navHistory.Count > 0;

        var items = new List<FolderPickerItem>();

        if (_navHistory.Count > 0)
        {
            items.Add(new FolderPickerItem
            {
                DisplayName = "..",
                PathLocator = null,
                RawName = ".."
            });
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = new SqlCommand("dbo.GetVaultFolders", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ParentLocator", (object?)_currentLocator ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string rawName = reader.GetString(0);
                string locator = reader.GetString(1);

                // Skip any folder that is the folder being moved or its descendant
                if (_excludedLocators.Contains(locator))
                    continue;

                string displayName = DecryptFolderName(rawName);
                items.Add(new FolderPickerItem
                {
                    DisplayName = displayName,
                    PathLocator = locator,
                    RawName = rawName
                });
            }

            FolderListView.ItemsSource = items;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to list folders:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task NavigateUpAsync()
    {
        if (_navHistory.Count > 0)
        {
            var prev = _navHistory.Pop();
            _currentLocator = prev.Locator;
            _currentVirtualPath = prev.VirtualPath;
            _currentPhysicalPath = prev.PhysicalPath;
            await LoadFoldersAsync();
        }
    }

    private async void OnFolderListViewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderListView.SelectedItem is not FolderPickerItem selected)
            return;

        if (selected.DisplayName == "..")
        {
            await NavigateUpAsync();
            return;
        }

        _navHistory.Push((_currentLocator, _currentVirtualPath, _currentPhysicalPath));
        _currentLocator = selected.PathLocator;
        _currentVirtualPath = $"{_currentVirtualPath.TrimEnd('/')}/{selected.DisplayName}/";
        _currentPhysicalPath = Path.Combine(_currentPhysicalPath, selected.RawName);

        await LoadFoldersAsync();
    }

    private void OnMoveHereClick(object sender, RoutedEventArgs e)
    {
        // If a valid child folder is highlighted, user chooses to move inside that child folder
        if (FolderListView.SelectedItem is FolderPickerItem selected && selected.DisplayName != "..")
        {
            SelectedLocator = selected.PathLocator;
            SelectedVirtualPath = $"{_currentVirtualPath.TrimEnd('/')}/{selected.DisplayName}/";
            SelectedPhysicalPath = Path.Combine(_currentPhysicalPath, selected.RawName);
        }
        else
        {
            // Otherwise, target is the currently navigated directory
            SelectedLocator = _currentLocator;
            SelectedVirtualPath = _currentVirtualPath;
            SelectedPhysicalPath = _currentPhysicalPath;
        }

        DialogResult = true;
        Close();
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        var inputDialog = new InputDialog("New Folder", "Enter new folder name:") { Owner = this };
        if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText))
            return;

        try
        {
            string plainFolderName = inputDialog.InputText.Trim();
            string encNameBase64 = CryptoService.EncryptString(plainFolderName, _encryptionPassword);
            // URL-safe base64 encoding avoids illegal characters ('/' and '+') in Windows directory names
            string safeEncFolderName = encNameBase64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

            string newPhysicalPath = Path.Combine(_currentPhysicalPath, safeEncFolderName);
            Directory.CreateDirectory(newPhysicalPath);

            await LoadFoldersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create folder:\n{ex.Message}", "New Folder Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnUpButtonClick(object sender, RoutedEventArgs e)
    {
        await NavigateUpAsync();
    }
}

