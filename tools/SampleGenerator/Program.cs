using ClosedXML.Excel;

// Generates the demo workbook: one worksheet per destination table, first row = column names.
// Usage: dotnet run --project tools/SampleGenerator -- <output.xlsx>

string output = args.Length > 0 ? args[0] : "ejemplo-import.xlsx";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

using var wb = new XLWorkbook();

// --- Sheet "Clientes" (PK = Id) ---
var clientes = wb.Worksheets.Add("Clientes");
string[] cHeaders = { "Id", "Nombre", "Email", "Saldo", "Alta", "Activo" };
for (int i = 0; i < cHeaders.Length; i++)
    clientes.Cell(1, i + 1).Value = cHeaders[i];

object[,] cData =
{
    { 1, "Ana López",   "ana@example.com",   150.50, new DateTime(2023, 1, 15), 1 },
    { 2, "Luis Pérez",  "luis@example.com",  0.00,   new DateTime(2023, 3, 2),  1 },
    { 3, "Marta Ruiz",  "marta@example.com", 1230.75,new DateTime(2024, 6, 20), 0 },
    { 4, "Juan Gómez",  "juan@example.com",  -50.00, new DateTime(2024, 11, 5), 1 },
};
WriteRows(clientes, cData);

// --- Sheet "Articulos" (PK = Codigo) ---
var articulos = wb.Worksheets.Add("Articulos");
string[] aHeaders = { "Codigo", "Descripcion", "Precio", "Stock" };
for (int i = 0; i < aHeaders.Length; i++)
    articulos.Cell(1, i + 1).Value = aHeaders[i];

object[,] aData =
{
    { "A100", "Teclado mecánico", 49.99, 120 },
    { "A101", "Ratón inalámbrico", 19.95, 300 },
    { "A102", "Monitor 27\"",     199.00, 45 },
    { "A103", "Webcam HD",        29.90, 0 },
};
WriteRows(articulos, aData);

foreach (var ws in wb.Worksheets)
    ws.Columns().AdjustToContents();

wb.SaveAs(output);
Console.WriteLine($"Generado: {Path.GetFullPath(output)}");

static void WriteRows(IXLWorksheet ws, object[,] data)
{
    int rows = data.GetLength(0), cols = data.GetLength(1);
    for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var cell = ws.Cell(r + 2, c + 1);
            switch (data[r, c])
            {
                case int i: cell.Value = i; break;
                case double d: cell.Value = d; break;
                case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                case string s: cell.Value = s; break;
            }
        }
}
