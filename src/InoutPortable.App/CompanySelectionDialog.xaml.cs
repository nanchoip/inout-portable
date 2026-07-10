using System.Windows;
using System.Windows.Input;
using InoutPortable.Core.Database;

namespace InoutPortable.App;

public partial class CompanySelectionDialog : Window
{
    private readonly ConnectionSettings _settings;

    public A3ErpCompany? Selected { get; private set; }

    public CompanySelectionDialog(ConnectionSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Conectando a la base de datos de sistema de a3ERP…";
        Cursor = Cursors.Wait;
        try
        {
            var result = await new A3ErpCompanyProvider(_settings).ListCompaniesAsync();
            if (!result.Success)
            {
                StatusText.Text = result.Error ?? "No se pudieron obtener las empresas.";
                return;
            }

            CompaniesGrid.ItemsSource = result.Companies;
            if (result.Companies.Count > 0)
            {
                CompaniesGrid.SelectedIndex = 0;
                UseBtn.IsEnabled = true;
                StatusText.Text = $"{result.Companies.Count} empresa(s) en '{result.SystemDatabase}'.";
            }
            else
            {
                StatusText.Text = $"No hay empresas registradas en '{result.SystemDatabase}'.";
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

    private void Companies_DoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (CompaniesGrid.SelectedItem is A3ErpCompany company)
        {
            Selected = company;
            DialogResult = true;
        }
        else
        {
            StatusText.Text = "Selecciona una empresa de la lista.";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
