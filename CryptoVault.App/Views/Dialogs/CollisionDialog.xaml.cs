using System.Windows;

namespace CryptoVault;

public enum CollisionResolution
{
    AutoRename,
    CustomRename,
    Overwrite,
    Skip,
    Cancel
}

public partial class CollisionDialog : Window
{
    private readonly bool _isDirectory;
    private readonly string _originalName;
    private readonly string _suggestedAutoName;

    public bool ApplyToAll { get; private set; }
    public string NewName { get; private set; } = string.Empty;
    public CollisionResolution Resolution { get; private set; } = CollisionResolution.Cancel;

    public CollisionDialog(string displayName, string suggestedAutoName, bool isDirectory, bool hasMoreItems = false)
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);

        _originalName = displayName;
        _suggestedAutoName = suggestedAutoName;
        _isDirectory = isDirectory;

        ConflictItemTextBlock.Text = displayName;
        AutoRenameButton.Content = $"🔄 Auto-Rename to '{suggestedAutoName}'";

        if (isDirectory)
        {
            OverwriteButton.IsEnabled = false;
            OverwriteButton.Opacity = 0.5;
            OverwriteButton.ToolTip = "Overwriting an entire directory is not supported. Please rename or skip.";
        }

        if (!hasMoreItems)
        {
            ApplyToAllCheckBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAutoRenameClick(object sender, RoutedEventArgs e)
    {
        Resolution = CollisionResolution.AutoRename;
        NewName = _suggestedAutoName;
        ApplyToAll = ApplyToAllCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Resolution = CollisionResolution.Cancel;
        DialogResult = false;
        Close();
    }

    private void OnCustomRenameClick(object sender, RoutedEventArgs e)
    {
        var inputDialog = new InputDialog("Rename Item", "Enter new name for the item being moved:", _suggestedAutoName)
        {
            Owner = this
        };

        if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
        {
            Resolution = CollisionResolution.CustomRename;
            NewName = inputDialog.InputText.Trim();
            ApplyToAll = ApplyToAllCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }

    private void OnOverwriteClick(object sender, RoutedEventArgs e)
    {
        Resolution = CollisionResolution.Overwrite;
        ApplyToAll = ApplyToAllCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        Resolution = CollisionResolution.Skip;
        ApplyToAll = ApplyToAllCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}

