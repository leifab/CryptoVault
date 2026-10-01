using System.Windows;

namespace CryptoVault;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

