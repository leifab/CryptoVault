using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace CryptoVault;

public class MetadataEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public partial class MetadataDialog : Window
{
    public ObservableCollection<MetadataEntry> Entries { get; } = new();

    public MetadataDialog(string itemName, Dictionary<string, object> initialData)
    {
        InitializeComponent();
        WindowExtensions.RemoveIcon(this);
        ItemNameTextBlock.Text = $"Metadata: {itemName}";

        foreach (var kvp in initialData)
        {
            string displayVal;

            if (kvp.Value is JsonElement elem && elem.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in elem.EnumerateArray())
                {
                    items.Add(item.ToString());
                }
                displayVal = string.Join(", ", items);
            }
            else if (kvp.Value is IEnumerable<string> strList)
            {
                displayVal = string.Join(", ", strList);
            }
            else
            {
                displayVal = kvp.Value?.ToString() ?? string.Empty;
            }

            Entries.Add(new MetadataEntry { Key = kvp.Key, Value = displayVal });
        }

        MetadataGrid.ItemsSource = Entries;
    }

    public Dictionary<string, object> GetMetadataDictionary()
    {
        var result = new Dictionary<string, object>();

        foreach (var entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            string rawVal = entry.Value?.Trim() ?? string.Empty;

            // Check if user entered JSON array syntax or comma-separated items
            if (rawVal.StartsWith('[') && rawVal.EndsWith(']'))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(rawVal);
                    if (parsed != null)
                    {
                        result[entry.Key.Trim()] = parsed;
                        continue;
                    }
                }
                catch
                {
                    // Fall back to raw string if parsing fails
                }
            }

            // Treat comma-separated values as arrays for attributes named "tags" or multi-item entries
            if (rawVal.Contains(',') || string.Equals(entry.Key.Trim(), "tags", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = rawVal.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
                result[entry.Key.Trim()] = parts;
            }
            else
            {
                result[entry.Key.Trim()] = rawVal;
            }
        }

        return result;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
