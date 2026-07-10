namespace InoutPortable.Core.Models;

public enum ValidationSeverity
{
    /// <summary>Blocks the import of the affected sheet/row.</summary>
    Error,
    /// <summary>Informational; import can proceed.</summary>
    Warning,
}

/// <summary>
/// A structured validation problem, reported to the user with enough context to locate it
/// (sheet, row, column) and a plain-language reason.
/// </summary>
public sealed class ValidationIssue
{
    public required string Sheet { get; init; }

    /// <summary>1-based Excel row, or <c>null</c> for structural (sheet/column-level) issues.</summary>
    public int? Row { get; init; }

    public string? Column { get; init; }
    public required string Message { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    public static ValidationIssue Structural(string sheet, string message, string? column = null) =>
        new() { Sheet = sheet, Column = column, Message = message, Severity = ValidationSeverity.Error };

    public static ValidationIssue Cell(string sheet, int row, string column, string message,
        ValidationSeverity severity = ValidationSeverity.Error) =>
        new() { Sheet = sheet, Row = row, Column = column, Message = message, Severity = severity };

    public string Location =>
        (Row, Column) switch
        {
            (null, null) => $"Hoja '{Sheet}'",
            (null, not null) => $"Hoja '{Sheet}', columna '{Column}'",
            (not null, null) => $"Hoja '{Sheet}', fila {Row}",
            (not null, not null) => $"Hoja '{Sheet}', fila {Row}, columna '{Column}'",
        };
}
