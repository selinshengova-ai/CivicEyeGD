using System.Globalization;
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
    // Датата идва вече в местно време: конвертирането е направено
    // веднъж при съставянето на реда, за да не го повтаря всеки формат
    // поотделно и да не се разминат CSV и книгата.
    DateTime CreatedAt,
    double AverageSeverity,
    int Confirmations,
    double Priority,
    // Етикетът пътува готов, заедно с числото: думите „Висок приоритет“
    // се определят по същите граници като в сайта, а не по нова
    // преценка на това място.
    string PriorityLabel,
    // Дните до първата намеса идват вече преброени. Липсват там, където
    // намеса все още няма: празната клетка е отговорът на въпроса
    // „реагира ли се“, а нулата би твърдяла обратното.
    int? ResponseDays = null);

/// <summary>
/// Приготвя сигналите за експорт: работна книга .xlsx за Excel и
/// разделен текст за програми, които четат CSV.
/// </summary>
// Двата формата отговарят на различни хора: служителят филтрира
// таблицата в Excel, а скрипт или стара програма очаква обикновен текст
// без стилове и заглавки. Затова редовете са едни и същи, а писането
// е два пъти.
public sealed class ReportExportService
{
    // Един масив за двете формати: колоните на работната книга и
    // заглавния ред на CSV вървят по един и същи ред, така файлът не
    // може да размени местата на две полета.
    // Новата колона застана последна, а не до датата: първите
    // единадесет имена са вече описани в документацията и служат на
    // таблици, които се сравняват по номер на колона.
    private static readonly string[] Headers =
    {
        "№", "Заглавие", "Описание", "Категория", "Статус", "Адрес",
        "Дата на подаване", "Средна сериозност", "Потвърждения",
        "Приоритет", "Етикет", "Реакция, дни"
    };

    // Ширините са подбрани спрямо съдържанието: описанието е най-дългото
    // поле, а номерата и етикетите не заслужават празно пространство.
    private static readonly double[] ColumnWidths =
    {
        6, 30, 52, 18, 14, 24, 18, 12, 13, 11, 17, 12
    };

    // Работната книга се пише чрез библиотека, защото .xlsx не е текст:
    // това е архив zip с няколко XML части (лист, стилове, общи
    // текстове и връзките между тях). Ръчно написана книга би счупила
    // някоя от връзките, а Excel би отказал целия файл.
    // На сървъра няма инсталиран Excel и не е нужен: се получават само
    // байтове, които отговорът на заявката носи.
    public byte[] BuildWorkbook(IReadOnlyList<ReportExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Сигнали");

        for (var column = 0; column < Headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = Headers[column];
        }

        var headerRange = sheet.Range(1, 1, 1, Headers.Length);
        // Заглавният ред е оцветен и удебелен, защото ще се използва
        // за филтриране: без ясна граница служителът не би разбрал
        // дали ред 1 е данни, или надписи на колоните.
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

            // Клетката за реакцията се пълни само където има число:
            // служителят търси „без намеса“ с празни полета във
            // филтъра, а нулата би се броила като дни и би излязла
            // по-бърза от всяка истинска реакция.
            if (row.ResponseDays is { } days)
            {
                sheet.Cell(line, 12).Value = days;
            }

            sheet.Cell(line, 7).Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
            sheet.Cell(line, 8).Style.NumberFormat.Format = "0.0";
            sheet.Cell(line, 10).Style.NumberFormat.Format = "0.0";
            // Форматът е само изображение: датата и приоритетът остават
            // число и момент от времето, затова Excel може да подрежда
            // по тях и да ги филтрира по диапазон.

            // Пречупването важи само за двете свободни полета —
            // заглавието и описанието са изречения на гражданина, а
            // адресът и статусът са кратки и се сравняват с поглед.
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

        // Заглавният ред остава неподвижен, а колоните получават филтър,
        // защото списъкът расте по-бързо от екрана: без тях служителят
        // би превъртял до дъното, за да разбере коя колона какво съдържа.
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(rows.Count + 1, 1), Headers.Length).SetAutoFilter(true);

        // Таблица, която се и отпечатва: хоризонтална ориентация,
        // с колкото страници трябва, но винаги с повторен заглавен ред.
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);

        // Книгата се сглобява в паметта и се връща като байтове: на
        // сървъра не остава временен файл, който друга заявка би могла
        // да отвори, и няма нищо за изтриване след изтеглянето.
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // CSV остава резервният формат: разделител по RFC 4180 е запетаята,
    // а BOM-ът е причината българските букви да се четат правилно.
    public byte[] BuildCsv(IReadOnlyList<ReportExportRow> rows)
    {
        var builder = new StringBuilder();
        // Краят на реда се слага изрично като \r\n, а не с разделителя
        // на операционната система: форматът го изисква еднакво,
        // независимо дали сървърът е Windows или Linux, иначе файлът
        // би се четял различно на друга машина.
        builder.Append(string.Join(',', Headers.Select(Cell))).Append("\r\n");

        foreach (var row in rows)
        {
            // Номерът на сигнала не минава през Cell: той е цяло число
            // без разделители и без водещ знак, затова не се нуждае
            // нито от кавички, нито от неутрализиране.
            builder.Append(string.Join(',', new[]
            {
                row.Id.ToString(),
                Cell(row.Title),
                Cell(row.Description),
                Cell(row.Category),
                Cell(row.Status),
                Cell(row.Address),
                Cell(row.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)),
                // Числата са с инвариантен разделител: на машина с български
                // език "0.0" би дало запетая, а тя е и разделителят на
                // колоните. Работната книга пази числа, затова и тук двете
                // числа изглеждат еднакво.
                Cell(row.AverageSeverity.ToString("0.0", CultureInfo.InvariantCulture)),
                Cell(row.Confirmations.ToString(CultureInfo.InvariantCulture)),
                Cell(row.Priority.ToString("0.0", CultureInfo.InvariantCulture)),
                Cell(row.PriorityLabel),
                // Празно поле, а не нула, където намеса още няма:
                // текстът „няма“ би се чел като стойност, а празното
                // поле се разбира от всяка програма като липса.
                Cell(row.ResponseDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            })).Append("\r\n");
        }

        // Текстът е UTF-8, а пред него седи BOM-ът: без тези три байта
        // Excel на Windows търси местната кодова страница и кирилицата
        // се изобразява като неразбираеми знаци.
        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();
    }

    // Кавички с удвояване на стойностите вътре; водещите =, +, - и @ се
    // неутрализират, защото Excel иначе изпълнява текста като формула.
    // Формула в клетка може да прочете съседни клетки и да изпрати
    // съдържанието им навън, а заглавията и описанията са свободен текст
    // на гражданите: един сигнал би стигнал, за да се изпълни чужд код
    // в компютъра на служителя. Този път е задължителен именно за CSV,
    // защото там няма тип на клетката, който да спре изчислението.
    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@'))
        {
            // Водещият знак се спасява с апостроф, а не се изтрива:
            // служителят трябва да вижда същия текст, какъвто е писал
            // подателят, иначе файлът би криел част от сигнала.
            text = "'" + text;
        }

        // Всяка стойност е в кавички, дори без запетая вътре, а
        // собствените ѝ кавички се удвояват: описание с нов ред или със
        // запетая не бива да разместява колоните на следващите редове.
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
