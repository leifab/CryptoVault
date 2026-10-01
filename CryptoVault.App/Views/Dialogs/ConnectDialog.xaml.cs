using System.Data.SqlClient;
using System.Windows;

namespace CryptoVault;

public partial class ConnectDialog : Window
{
    public string ConnectionString { get; private set; } = string.Empty;

    public ConnectDialog()
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);
        WindowsAuthCheckBox.IsChecked = true;
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        bool isWindowsAuth = WindowsAuthCheckBox.IsChecked == true;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = ServerTextBox.Text.Trim(),
            InitialCatalog = DatabaseTextBox.Text.Trim(),
            IntegratedSecurity = isWindowsAuth,
            TrustServerCertificate = true
        };

        if (!isWindowsAuth)
        {
            builder.UserID = UsernameTextBox.Text.Trim();
            builder.Password = PasswordBox.Password;
        }

        ConnectionString = builder.ConnectionString;
        DialogResult = true;
    }

    private void OnWindowsAuthCheckBoxToggle(object sender, RoutedEventArgs e)
    {
        bool isWindowsAuth = WindowsAuthCheckBox.IsChecked == true;

        UsernameTextBox.IsEnabled = !isWindowsAuth;
        PasswordBox.IsEnabled = !isWindowsAuth;
    }

}
