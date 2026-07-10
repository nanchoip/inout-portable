using System.Text;
using ExcelDataReader;
using InoutPortable.Core.Models;

namespace InoutPortable.Core.Excel;

/// <summary>
/// Reads a workbook (.xlsx or legacy .xls, detected by content — not by extension) into
/// <see cref="RawSheet"/> objects with every row and cell. Interpreting which row is the header
/// is done separately by <see cref="SheetInterpreter"/>.
/// </summary>
public sealed class ExcelWorkbookReader
{
    static ExcelWorkbookReader()
    {
        // Required by ExcelDataReader to decode legacy .xls text (code pages).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyList<RawSheet> ReadRaw(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        return ReadRaw(stream);
    }

    public IReadOnlyList<RawSheet> ReadRaw(Stream stream)
    {
        // CreateReader sniffs the stream header, so .xls renamed to .xlsx (and vice-versa) still works.
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<RawSheet>();

        do
        {
            var rows = new List<IReadOnlyList<CellValue>>();
            while (reader.Read())
            {
                var cells = new CellValue[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    cells[i] = Convert(reader.GetValue(i));
                rows.Add(cells);
            }

            sheets.Add(new RawSheet { Name = (reader.Name ?? "").Trim(), Rows = rows });
        }
        while (reader.NextResult());

        return sheets;
    }

    private static CellValue Convert(object? value) => value switch
    {
        null => CellValue.Empty,
        DBNull => CellValue.Empty,
        string s => new CellValue(s),
        double d => new CellValue(d),
        bool b => new CellValue(b),
        DateTime dt => new CellValue(dt),
        TimeSpan ts => new CellValue(ts),
        int i => new CellValue((double)i),
        long l => new CellValue((double)l),
        decimal m => new CellValue((double)m),
        _ => new CellValue(value.ToString()),
    };
}
