using System.Windows;

namespace CryptoVault;

public partial class PasswordDialog : Window
{
    public string Password { get; private set; } = string.Empty;

    public PasswordDialog()
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);
        Loaded += (s, e) => RepoPasswordBox.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RepoPasswordBox.Password))
        {
            MessageBox.Show("Password cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Password = RepoPasswordBox.Password;
        DialogResult = true;
    }
}
