using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace InoutPortable.App;

public partial class KeySelectionDialog : Window
{
    public sealed class ColumnItem : INotifyPropertyChanged
    {
        public required string Name { get; init; }
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<ColumnItem> _items = new();

    /// <summary>The columns the user selected as the key (in list order). Null if cancelled.</summary>
    public IReadOnlyList<string>? SelectedKey { get; private set; }

    public KeySelectionDialog(string sheetName, IEnumerable<string> availableColumns, IReadOnlyList<string> currentKey)
    {
        InitializeComponent();
        Intro.Text = $"Hoja '{sheetName}': marca la columna (o columnas) que identifican de forma única " +
                     "cada fila. Se usará para decidir si cada fila se inserta o se actualiza.";

        var current = new HashSet<string>(currentKey, StringComparer.OrdinalIgnoreCase);
        foreach (var col in availableColumns)
            _items.Add(new ColumnItem { Name = col, Checked = current.Contains(col) });

        ColumnsList.ItemsSource = _items;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _items.Where(i => i.Checked).Select(i => i.Name).ToList();
        if (chosen.Count == 0)
        {
            Warn.Text = "Selecciona al menos una columna.";
            return;
        }
        SelectedKey = chosen;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
