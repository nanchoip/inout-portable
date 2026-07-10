using System.Windows;
using System.Windows.Input;
using InoutPortable.Core.Database;

namespace InoutPortable.App;

public partial class InstanceScanDialog : Window
{
    public SqlInstanceInfo? Selected { get; private set; }

    private readonly SqlInstanceScanner _scanner = new();
    private bool _scanning;

    public InstanceScanDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunScanAsync();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        ScanBtn.IsEnabled = false;
        UseBtn.IsEnabled = false;
        StatusText.Text = "Buscando…";
        Cursor = Cursors.Wait;

        try
        {
            var extra = string.IsNullOrWhiteSpace(HostBox.Text) ? null : new[] { HostBox.Text.Trim() };
            var progress = new Progress<string>(m => StatusText.Text = m);
            var results = await _scanner.DiscoverAsync(extra, progress);

            ResultsGrid.ItemsSource = results;
            if (results.Count > 0)
            {
                ResultsGrid.SelectedIndex = 0;
                int a3 = results.Count(r => r.IsA3Erp);
                StatusText.Text = $"{results.Count} instancia(s) encontradas" + (a3 > 0 ? $" ({a3} de a3ERP)." : ".");
            }
            else
            {
                StatusText.Text = "No se encontraron instancias. Prueba escribiendo la IP del servidor y pulsa Buscar.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error al buscar: " + ex.Message;
        }
        finally
        {
            _scanning = false;
            ScanBtn.IsEnabled = true;
            UseBtn.IsEnabled = true;
            Cursor = null;
        }
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is SqlInstanceInfo info)
        {
            Selected = info;
            DialogResult = true;
        }
        else
        {
            StatusText.Text = "Selecciona una instancia de la lista.";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
