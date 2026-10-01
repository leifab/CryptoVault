using System.Windows;

namespace CryptoVault;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public InputDialog(string title = "New Folder", string prompt = "Enter folder name:", string defaultText = "")
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);
        Title = title;
        PromptTextBlock.Text = prompt;
        InputTextBox.Text = defaultText;
        Loaded += (s, e) =>
        {
            InputTextBox.Focus();
            if (!string.IsNullOrEmpty(InputTextBox.Text))
            {
                InputTextBox.SelectAll();
            }
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            MessageBox.Show("Name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        InputText = InputTextBox.Text.Trim();
        DialogResult = true;
    }
}
