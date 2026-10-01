using System.Windows;

namespace CryptoVault;

public partial class DuplicateMatchDialog : Window
{
    public DuplicateMatchDialog(string incomingFileName, IEnumerable<FingerprintMatchResult> matches)
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);

        IncomingFileTextBlock.Text = incomingFileName;
        MatchesGrid.ItemsSource = matches;
    }

    private void OnImportAnywayClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
