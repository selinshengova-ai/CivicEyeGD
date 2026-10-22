using System.Text;
using ClosedXML.Excel;

namespace CivicEyeGD.Services;

/// <summary>
/// Един ред от експорта. Сервизът не познава базата данни, затова
/// приоритетът и средната сериозност идват вече изчислени —
/// файлът не може да се различава от това, което служителят вижда.
/// </summary>
public sealed record ReportExportRow(
    int Id,
    string Title,
    string Description,
    string Category,
    string Status,
    string Address,
    DateTime CreatedAt,
    double AverageSeverity,
    int Confirmations,
    double Priority,
    string PriorityLabel);

/// <summary>
/// Приготвя сигналите за експорт: работна книга .xlsx за Excel и
/// разделен текст за програми, които четат CSV.
/// </summary>
public sealed class ReportExportService
{
    private static readonly string[] Headers =
    {
        "№", "Заглавие", "Описание", "Категория", "Статус", "Адрес",
        "Дата на подаване", "Средна сериозност", "Потвърждения",
        "Приоритет", "Етикет"
    };

    // Ширините са подбрани спрямо съдържанието: описанието е най-дългото
    // поле, а номерата и етикетите не заслужават празно пространство.
    private static readonly double[] ColumnWidths =
    {
        6, 30, 52, 18, 14, 24, 18, 12, 13, 11, 17
    };

    public byte[] BuildWorkbook(IReadOnlyList<ReportExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Сигнали");

        for (var column = 0; column < Headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = Headers[column];
        }

        var headerRange = sheet.Range(1, 1, 1, Headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.FromHtml("#F6F8F5");
        headerRange.Style.Fill.PatternType = XLFillPatternValues.Solid;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B4D3E");
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 26;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var line = index + 2;

            // Стойностите се записват като текст и числа, никога като формула:
            // заглавие "=1+1" остава текст в клетката и Excel го изобразява
            // буквално, вместо да го изпълни.
            sheet.Cell(line, 1).Value = row.Id;
            sheet.Cell(line, 2).Value = row.Title;
            sheet.Cell(line, 3).Value = row.Description;
            sheet.Cell(line, 4).Value = row.Category;
            sheet.Cell(line, 5).Value = row.Status;
            sheet.Cell(line, 6).Value = row.Address;
            sheet.Cell(line, 7).Value = row.CreatedAt;
            sheet.Cell(line, 8).Value = row.AverageSeverity;
            sheet.Cell(line, 9).Value = row.Confirmations;
            sheet.Cell(line, 10).Value = row.Priority;
            sheet.Cell(line, 11).Value = row.PriorityLabel;

            sheet.Cell(line, 7).Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
            sheet.Cell(line, 8).Style.NumberFormat.Format = "0.0";
            sheet.Cell(line, 10).Style.NumberFormat.Format = "0.0";

            sheet.Cell(line, 2).Style.Alignment.WrapText = true;
            sheet.Cell(line, 3).Style.Alignment.WrapText = true;

            for (var column = 1; column <= Headers.Length; column++)
            {
                var cell = sheet.Cell(line, column);
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#C9D6CD");
            }

            // Редовете се редуват, за да се следи дългото описание.
            if (index % 2 == 1)
            {
                sheet.Range(line, 1, line, Headers.Length).Style.Fill.PatternType =
                    XLFillPatternValues.Solid;
                sheet.Range(line, 1, line, Headers.Length).Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#EFF4F0");
            }

            // Етикетът носи цвета на приоритета: служителят вижда
            // подредбата, без да чете числа.
            var labelCell = sheet.Cell(line, 11);
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Fill.PatternType = XLFillPatternValues.Solid;
            labelCell.Style.Fill.BackgroundColor = row.PriorityLabel switch
            {
                "Висок приоритет" => XLColor.FromHtml("#F7D9D7"),
                "Среден приоритет" => XLColor.FromHtml("#FAEBC8"),
                _ => XLColor.FromHtml("#DCEDE2")
            };
            labelCell.Style.Font.FontColor = row.PriorityLabel switch
            {
                "Висок приоритет" => XLColor.FromHtml("#8E2B26"),
                "Среден приоритет" => XLColor.FromHtml("#8A6503"),
                _ => XLColor.FromHtml("#1B4D3E")
            };
        }

        for (var column = 0; column < ColumnWidths.Length; column++)
        {
            sheet.Column(column + 1).Width = ColumnWidths[column];
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(rows.Count + 1, 1), Headers.Length).SetAutoFilter(true);

        // Таблица, която се и отпечатва: хоризонтална ориентация,
        // с колкото страници трябва, но винаги с повторен заглавен ред.
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // CSV остава резервният формат: разделител по RFC 4180 е запетаята,
    // а BOM-ът е причината българските букви да се четат правилно.
    public byte[] BuildCsv(IReadOnlyList<ReportExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Headers.Select(Cell))).Append("\r\n");

        foreach (var row in rows)
        {
            builder.Append(string.Join(',', new[]
            {
                row.Id.ToString(),
                Cell(row.Title),
                Cell(row.Description),
                Cell(row.Category),
                Cell(row.Status),
                Cell(row.Address),
                Cell(row.CreatedAt.ToString("dd.MM.yyyy HH:mm")),
                Cell(row.AverageSeverity.ToString("0.0")),
                Cell(row.Confirmations.ToString()),
                Cell(row.Priority.ToString("0.0")),
                Cell(row.PriorityLabel)
            })).Append("\r\n");
        }

        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();
    }

    // Кавички с удвояване на стойностите вътре; водещите =, +, - и @ се
    // неутрализират, защото Excel иначе изпълнява текста като формула.
    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@'))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
