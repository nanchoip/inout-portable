using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InoutPortable.Core.Database;
using InoutPortable.Core.Import;
using InoutPortable.Core.Logging;
using InoutPortable.Core.Models;
using InoutPortable.Core.Settings;
using Microsoft.Win32;

namespace InoutPortable.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ImportLogStore _logStore = new();
    private ImportPreview? _preview;
    private readonly Dictionary<string, TableImportPlan> _plansBySheet = new();
    private List<SummaryRow> _summaryRows = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _keyOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush Err = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        var s = _settingsStore.Load();
        HostBox.Text = s.Host;
        InstanceBox.Text = s.Instance ?? "";
        PortBox.Text = s.Port?.ToString() ?? "";
        DbBox.Text = s.Database;
        IntegratedBox.IsChecked = s.IntegratedSecurity;
        UserBox.Text = s.Username;
        PassBox.Password = s.Password;
        EncryptBox.IsChecked = s.Encrypt;
        TrustBox.IsChecked = s.TrustServerCertificate;
        UpdateIntegratedState();
        UpdateConnStatus();
        RefreshHistory_Click(this, new RoutedEventArgs());

        // Diagnostic: auto-open the instance scanner to reproduce issues headlessly.
        if (Environment.GetCommandLineArgs().Contains("--selftest-scan"))
            Dispatcher.BeginInvoke(() => new InstanceScanDialog { Owner = this }.Show());
    }

    // ---------- Connection tab ----------

    private ConnectionSettings ReadForm() => new()
    {
        Host = HostBox.Text.Trim(),
        Instance = string.IsNullOrWhiteSpace(InstanceBox.Text) ? null : InstanceBox.Text.Trim(),
        Port = int.TryParse(PortBox.Text.Trim(), out var p) ? p : null,
        Database = DbBox.Text.Trim(),
        IntegratedSecurity = IntegratedBox.IsChecked == true,
        Username = UserBox.Text.Trim(),
        Password = PassBox.Password,
        Encrypt = EncryptBox.IsChecked == true,
        TrustServerCertificate = TrustBox.IsChecked == true,
    };

    private void ScanInstances_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InstanceScanDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected is { } info)
        {
            HostBox.Text = info.Server;
            InstanceBox.Text = info.Instance;
            // Prefer the discovered TCP port so connecting doesn't depend on SQL Browser.
            PortBox.Text = info.TcpPort?.ToString() ?? "";
            if (info.TcpPort is not null)
                InstanceBox.Text = ""; // host + port is enough and more robust than a named instance
            ConnResult.Foreground = Muted;
            ConnResult.Text = $"Instancia seleccionada: {info.DisplayName}" +
                              (info.TcpPort is not null ? $" (puerto {info.TcpPort})." : ".");
        }
    }

    private void ChooseCompany_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadForm();
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            ConnResult.Foreground = Err;
            ConnResult.Text = "Introduce primero el servidor (y usuario/contraseña) para leer las empresas de a3ERP.";
            return;
        }

        var dlg = new CompanySelectionDialog(settings) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected is { } company)
        {
            DbBox.Text = company.DatabaseName;

            // If the company points to a specific SQL Server, adopt it (server\instance).
            if (!string.IsNullOrWhiteSpace(company.ServerName))
            {
                var parts = company.ServerName.Split('\\', 2);
                HostBox.Text = parts[0];
                InstanceBox.Text = parts.Length > 1 ? parts[1] : "";
                PortBox.Text = "";
            }

            ConnResult.Foreground = Muted;
            ConnResult.Text = $"Empresa seleccionada: {company.Description} → base de datos '{company.DatabaseName}'.";
        }
    }

    private void Integrated_Changed(object sender, RoutedEventArgs e) => UpdateIntegratedState();

    private void UpdateIntegratedState()
    {
        bool integrated = IntegratedBox.IsChecked == true;
        UserBox.IsEnabled = !integrated;
        PassBox.IsEnabled = !integrated;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadForm();
        SetBusy(true, ConnResult, "Probando conexión…");
        try
        {
            var result = await new ConnectionTester().TestAsync(settings);
            ConnResult.Foreground = result.Success ? Ok : Err;
            ConnResult.Text = result.Success
                ? $"OK - {result.Message}\n{result.ServerVersion}"
                : $"Error - {result.Message}";
        }
        catch (Exception ex)
        {
            ConnResult.Foreground = Err;
            ConnResult.Text = "Error inesperado: " + ex.Message;
        }
        finally
        {
            SetBusy(false, null, null);
        }
    }

    private void SaveConnection_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadForm();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            ConnResult.Foreground = Err;
            ConnResult.Text = string.Join("\n", errors);
            return;
        }

        try
        {
            _settingsStore.Save(settings);
            ConnResult.Foreground = Ok;
            ConnResult.Text = "Configuración guardada correctamente.";
            UpdateConnStatus();
        }
        catch (Exception ex)
        {
            ConnResult.Foreground = Err;
            ConnResult.Text = "No se pudo guardar: " + ex.Message;
        }
    }

    private void UpdateConnStatus()
    {
        var s = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(s.Host) || string.IsNullOrWhiteSpace(s.Database))
        {
            ConnStatus.Text = "● Sin conexión configurada";
            ConnStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
        }
        else
        {
            ConnStatus.Text = $"● {s.BuildDataSource()} / {s.Database}";
            ConnStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
    }

    // ---------- Import tab ----------

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Libros de Excel (*.xlsx)|*.xlsx|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar archivo Excel",
        };
        if (dlg.ShowDialog() == true)
        {
            FilePathBox.Text = dlg.FileName;
            _keyOverrides.Clear(); // a new file starts with no manual key overrides
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePathBox.Text) || !File.Exists(FilePathBox.Text))
        {
            SetStatus(ImportStatus, "Seleccione primero un archivo Excel.", Err);
            return;
        }

        var settings = _settingsStore.Load();
        if (settings.Validate().Count > 0)
        {
            SetStatus(ImportStatus, "Configure y guarde primero la conexión en la pestaña 'Conexión'.", Err);
            Tabs.SelectedIndex = 0;
            return;
        }

        ConfirmBtn.IsEnabled = false;
        SetBusy(true, ImportStatus, "Analizando el archivo y validando contra la base de datos…");
        try
        {
            var orchestrator = new ImportOrchestrator(settings);
            _preview = await orchestrator.BuildPreviewAsync(FilePathBox.Text, _keyOverrides);
            PopulatePreview(_preview);
        }
        catch (Exception ex)
        {
            SetStatus(ImportStatus, "Error al analizar: " + ex.Message, Err);
        }
        finally
        {
            SetBusy(false, null, null);
        }
    }

    private void PopulatePreview(ImportPreview preview)
    {
        _plansBySheet.Clear();

        var summary = new List<SummaryRow>();
        var issues = new List<IssueRow>();

        foreach (var g in preview.GlobalIssues)
            issues.Add(new IssueRow { Location = g.Location, Severity = SeverityText(g.Severity), Message = g.Message });

        foreach (var plan in preview.Tables)
        {
            _plansBySheet[plan.Sheet] = plan;
            summary.Add(new SummaryRow
            {
                Include = plan.IsImportable,
                Sheet = plan.Sheet,
                Table = plan.Table.FullName,
                HeaderRow = plan.HeaderRowNumber,
                Insert = plan.InsertCount,
                Update = plan.UpdateCount,
                Errors = plan.ErrorCount,
                NoKey = plan.KeyColumns.Count == 0,
                Status = StatusText(plan),
                Plan = plan,
            });

            foreach (var i in plan.Issues)
                issues.Add(new IssueRow { Location = i.Location, Severity = SeverityText(i.Severity), Message = i.Message });
        }

        _summaryRows = summary;
        SummaryGrid.ItemsSource = summary;
        IssuesGrid.ItemsSource = issues;

        PreviewSheetCombo.ItemsSource = preview.Tables.Select(t => t.Sheet).ToList();
        if (PreviewSheetCombo.Items.Count > 0)
            PreviewSheetCombo.SelectedIndex = 0;
        else
            PreviewGrid.ItemsSource = null;

        int inserts = preview.TotalInserts, updates = preview.TotalUpdates, errors = preview.TotalErrors;
        ConfirmBtn.IsEnabled = preview.CanImportAnything;

        var msg = $"Se importarán {inserts} inserciones y {updates} actualizaciones. " +
                  $"{errors} problema(s) detectado(s).";
        if (!preview.CanImportAnything)
            msg = "No hay filas válidas para importar. Revise los problemas detectados. " +
                  (errors == 0 ? "" : $"({errors} problema(s))");
        SetStatus(ImportStatus, msg, preview.CanImportAnything ? Muted : Err);
    }

    private static string StatusText(TableImportPlan plan)
    {
        if (plan.IsBlocked && plan.KeyColumns.Count == 0) return "Sin clave primaria";
        if (plan.IsBlocked) return "Bloqueada (errores)";
        if (plan.ErrorCount > 0) return "Se importarán las válidas";
        return "Lista";
    }

    private static string SeverityText(ValidationSeverity s) => s == ValidationSeverity.Error ? "Error" : "Aviso";

    private void SummaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SummaryGrid.SelectedItem is SummaryRow row)
            PreviewSheetCombo.SelectedItem = row.Sheet;
    }

    private void DefineKey_Click(object sender, RoutedEventArgs e)
    {
        if (SummaryGrid.SelectedItem is not SummaryRow row || row.Plan is null)
        {
            SetStatus(ImportStatus, "Selecciona una hoja en el resumen para definir su clave.", Err);
            return;
        }

        var plan = row.Plan;
        if (plan.MappedColumns.Count == 0)
        {
            SetStatus(ImportStatus,
                $"La hoja '{plan.Sheet}' no tiene columnas utilizables como clave (revisa los problemas detectados).", Err);
            return;
        }

        var dlg = new KeySelectionDialog(plan.Sheet, plan.MappedColumns, plan.KeyColumns) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedKey is { Count: > 0 } key)
        {
            _keyOverrides[plan.Sheet] = key;
            Analyze_Click(this, new RoutedEventArgs()); // re-analyze applying the chosen key
        }
    }

    private void PreviewSheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewSheetCombo.SelectedItem is not string sheet || !_plansBySheet.TryGetValue(sheet, out var plan))
        {
            PreviewGrid.ItemsSource = null;
            PreviewNote.Text = "";
            return;
        }

        BuildPreviewTable(plan);
    }

    private void BuildPreviewTable(TableImportPlan plan)
    {
        var columns = plan.KeyColumns
            .Concat(plan.MappedColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dt = new DataTable();
        dt.Columns.Add("Fila");
        dt.Columns.Add("Operación");
        foreach (var c in columns)
            if (!dt.Columns.Contains(c))
                dt.Columns.Add(c);

        const int maxRows = 200;
        foreach (var rp in plan.Rows.Take(maxRows))
        {
            var dr = dt.NewRow();
            dr["Fila"] = rp.Row.ExcelRowNumber;
            dr["Operación"] = OperationText(rp.Operation);
            foreach (var c in columns)
                dr[c] = rp.Row.Get(c).AsDisplayString();
            dt.Rows.Add(dr);
        }

        PreviewGrid.ItemsSource = dt.DefaultView;
        PreviewNote.Text = plan.Rows.Count > maxRows
            ? $"Mostrando las primeras {maxRows} de {plan.Rows.Count} filas."
            : $"{plan.Rows.Count} fila(s).";
    }

    private static string OperationText(RowOperation op) => op switch
    {
        RowOperation.Insert => "Insertar",
        RowOperation.Update => "Actualizar",
        _ => "Error",
    };

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null) return;

        SummaryGrid.CommitEdit(DataGridEditingUnit.Row, true);

        // Build a preview containing only the sheets the user ticked (and that are importable).
        var selected = _summaryRows
            .Where(r => r.Include && r.Plan is { IsImportable: true })
            .Select(r => r.Plan!)
            .ToList();

        if (selected.Count == 0)
        {
            SetStatus(ImportStatus, "Marque al menos una hoja importable en la columna 'Importar'.", Err);
            return;
        }

        var execPreview = new ImportPreview { FileName = _preview.FileName };
        execPreview.Tables.AddRange(selected);

        var confirm = MessageBox.Show(
            $"Se importarán {execPreview.TotalInserts} inserciones y {execPreview.TotalUpdates} actualizaciones " +
            $"en {selected.Count} tabla(s).\n\n" +
            "La operación se ejecuta en una transacción: si algo falla, se revierte todo.\n\n¿Desea continuar?",
            "Confirmar importación", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var settings = _settingsStore.Load();
        SetBusy(true, ImportStatus, "Importando…");
        ConfirmBtn.IsEnabled = false;
        try
        {
            var orchestrator = new ImportOrchestrator(settings);
            var progress = new Progress<string>(msg => ImportStatus.Text = msg);
            var result = await orchestrator.ExecuteAsync(execPreview, progress);

            _logStore.Append(result);
            RefreshHistory_Click(this, new RoutedEventArgs());

            SetStatus(ImportStatus, result.Message ?? "", result.Success ? Ok : Err);
            MessageBox.Show(result.Message,
                result.Success ? "Importación completada" : "Importación revertida",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

            // Re-analyze so counts reflect the new state (inserts become updates).
            if (result.Success)
                Analyze_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            SetStatus(ImportStatus, "Error al importar: " + ex.Message, Err);
        }
        finally
        {
            SetBusy(false, null, null);
        }
    }

    // ---------- History tab ----------

    private void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        var rows = _logStore.ReadAll().Select(x => new HistoryRow
        {
            When = x.Timestamp.ToString("yyyy-MM-dd HH:mm"),
            FileName = x.FileName,
            Inserted = x.TotalInserted,
            Updated = x.TotalUpdated,
            Skipped = x.TotalSkipped,
            Result = x.Success ? "OK" : (x.RolledBack ? "Rollback" : "Error"),
            Message = x.Message ?? "",
        }).ToList();
        HistoryGrid.ItemsSource = rows;
    }

    // ---------- helpers ----------

    private void SetBusy(bool busy, TextBlock? status, string? message)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        TestBtn.IsEnabled = !busy;
        SaveBtn.IsEnabled = !busy;
        AnalyzeBtn.IsEnabled = !busy;
        BrowseBtn.IsEnabled = !busy;
        if (status is not null && message is not null)
            SetStatus(status, message, Muted);
    }

    private static void SetStatus(TextBlock target, string message, Brush brush)
    {
        target.Foreground = brush;
        target.Text = message;
    }
}
