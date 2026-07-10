using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using InoutPortable.Core.Database;

namespace InoutPortable.App;

public partial class CompanySelectionDialog : Window
{
    /// <summary>Grid row wrapping a company plus its decoded logo (best-effort).</summary>
    public sealed class CompanyRow
    {
        public required A3ErpCompany Company { get; init; }
        public string Description => Company.Description;
        public string DatabaseName => Company.DatabaseName;
        public string? ServerName => Company.ServerName;
        public BitmapImage? LogoImage { get; init; }
    }

    private readonly ConnectionSettings _settings;
    private readonly string? _systemDbOverride;
    private List<CompanyRow> _all = new();

    public A3ErpCompany? Selected { get; private set; }

    public CompanySelectionDialog(ConnectionSettings settings, string? initialUser = null, string? systemDbOverride = null)
    {
        InitializeComponent();
        _settings = settings;
        _systemDbOverride = systemDbOverride;
        if (!string.IsNullOrWhiteSpace(initialUser)) UserBox.Text = initialUser;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        StatusText.Text = "Conectando a la base de datos de sistema de a3ERP…";
        Cursor = Cursors.Wait;
        UseBtn.IsEnabled = false;
        try
        {
            var user = string.IsNullOrWhiteSpace(UserBox.Text) ? null : UserBox.Text.Trim();
            var result = await new A3ErpCompanyProvider(_settings).ListCompaniesAsync(user, _systemDbOverride);
            if (!result.Success)
            {
                StatusText.Text = result.Error ?? "No se pudieron obtener las empresas.";
                _all = new List<CompanyRow>();
                ApplyFilter();
                return;
            }

            _all = result.Companies.Select(c => new CompanyRow { Company = c, LogoImage = TryDecodeLogo(c.Logo) }).ToList();
            ApplyFilter();

            if (_all.Count > 0)
            {
                UseBtn.IsEnabled = true;
                StatusText.Text = $"{_all.Count} empresa(s) en '{result.SystemDatabase}'" +
                                  (user is not null ? $" (usuario {user})." : ".");
            }
            else
            {
                StatusText.Text = $"No hay empresas para mostrar en '{result.SystemDatabase}'.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
        finally
        {
            Cursor = null;
        }
    }

    private void ApplyFilter()
    {
        var text = SearchBox.Text?.Trim() ?? "";
        CompaniesGrid.ItemsSource = _all;
        var view = CollectionViewSource.GetDefaultView(CompaniesGrid.ItemsSource);
        if (view is null) return;

        view.Filter = text.Length == 0
            ? null
            : o =>
            {
                var r = (CompanyRow)o;
                return r.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || r.DatabaseName.Contains(text, StringComparison.OrdinalIgnoreCase);
            };
        view.Refresh();
        if (!view.IsEmpty)
            CompaniesGrid.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private static BitmapImage? TryDecodeLogo(byte[]? bytes)
    {
        if (bytes is not { Length: > 8 }) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null; // a3ERP logos may be EMF/OLE which WPF can't decode; ignore gracefully
        }
    }

    private void Companies_DoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (CompaniesGrid.SelectedItem is CompanyRow row)
        {
            Selected = row.Company;
            DialogResult = true;
        }
        else
        {
            StatusText.Text = "Selecciona una empresa de la lista.";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
