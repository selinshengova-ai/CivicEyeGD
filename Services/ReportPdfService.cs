using CivicEyeGD.Data;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CivicEyeGD.Services;

/// <summary>
/// PDF отчетът е документът, който службата оставя в папката си: заглавна
/// част с бройките, диаграми по категории и локации, топлинна матрица
/// категория × статус и списък на сигналите, които все още чакат.
/// Всички числа идват от ReportStatistics, затова документът не може да
/// каже нещо различно от това, което таблото показва.
/// </summary>
public sealed class ReportPdfService
{
    // Прагът е същият като в таблото: две различни числа за едно и също
    // „спешно" биха изглеждали като грешка в отчета.
    public const double HighPriorityFrom = 70;

    private const float MaxBarWidth = 210;

    private const string InkHex = "#1D3028";
    private const string MutedHex = "#5C6B61";
    private const string GreenHex = "#1B4D3E";
    private const string PaperHex = "#F6F8F5";

    static ReportPdfService()
    {
        // QuestPDF е със свободен Community лиценз за проекти като този,
        // но изисква той да бъде заявен изрично при зареждане на библиотеката.
        Settings.License = LicenseType.Community;
    }

    public byte[] Build(IReadOnlyList<ReportExportRow> rows, string listTitle, DateTime generatedAtUtc)
    {
        var open = ReportStatistics.Open(rows);
        var categories = ReportStatistics.ByCategory(rows);
        var locations = ReportStatistics.TopLocations(rows);
        var heat = ReportStatistics.HeatMatrix(rows);
        var heatCategories = ReportStatistics.HeatCategories(rows);
        var generatedAt = generatedAtUtc.ToSofia();
        var highCount = open.Count(row => row.Priority >= HighPriorityFrom);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(text => text.FontSize(9).FontColor(Color.FromHex(InkHex)));

                page.Header().Element(header => ComposeHeader(header, listTitle, rows.Count, generatedAt));

                page.Content().PaddingVertical(12).Column(content =>
                {
                    content.Spacing(6);

                    content.Item().Row(boxes =>
                    {
                        boxes.RelativeItem().Element(box => KpiBox(box, "Сигнали в отчета", rows.Count.ToString()));
                        boxes.RelativeItem().PaddingLeft(8).Element(box => KpiBox(box, "Чакат решение", open.Count.ToString()));
                        boxes.RelativeItem().PaddingLeft(8).Element(box => KpiBox(box, "Приключени", (rows.Count - open.Count).ToString()));
                        boxes.RelativeItem().PaddingLeft(8).Element(box => KpiBox(box, "Висок приоритет", highCount.ToString()));
                    });

                    content.Item().Element(section =>
                        BarsSection(section, "Сигнали по категория", categories, "брой сигнали · среден приоритет"));

                    content.Item().Element(section =>
                        BarsSection(section, "Най-натоварени локации", locations, "брой сигнали · среден приоритет"));

                    if (heat.Count > 0)
                    {
                        content.Item().Element(section => HeatSection(section, heatCategories, heat));
                    }

                    content.Item().Element(section => OpenReportsSection(section, open));

                    content.Item().PaddingTop(14).Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                        text.Span("Приоритетът се смята по формулата на платформата: 20×средна сериозност "
                            + "+ 3×независими потвърждения (до 10) + административна корекция (0–10), "
                            + "нормирани към 100 точки от максимум 140. Оценките на подателя за собствения "
                            + "му сигнал не участват в сметката.");
                    });
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                    text.Span("CivicEye GD · стр. ");
                    text.CurrentPageNumber();
                    text.Span(" от ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, string listTitle, int count, DateTime generatedAt)
    {
        container.Column(header =>
        {
            header.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("CIVICEYE GD").FontSize(9).Bold().FontColor(Color.FromHex(GreenHex));
                    text.Span(" / ОТЧЕТ ЗА СИГНАЛИТЕ").FontSize(9).FontColor(Color.FromHex(MutedHex));
                });

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                    text.Span($"Генериран {generatedAt:dd.MM.yyyy} в {generatedAt:HH:mm} ч.");
                });
            });

            header.Item().PaddingTop(6).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(20).Bold());
                text.Span(listTitle);
            });

            header.Item().PaddingTop(2).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Color.FromHex(MutedHex)));
                text.Span($"{count} сигнала в документа");
            });

            header.Item().PaddingTop(10).Height(2).Background(Color.FromHex("#D7F36B"));
        });
    }

    private static void KpiBox(IContainer container, string label, string value)
    {
        container.Background(Color.FromHex("#F0F6F1"))
            .Border(1).BorderColor(Color.FromHex("#DCE5D6"))
            .CornerRadius(6)
            .Padding(10)
            .Column(box =>
            {
                box.Item().Text(label).FontSize(8).FontColor(Color.FromHex(MutedHex));
                box.Item().PaddingTop(2).Text(value).FontSize(18).Bold();
            });
    }

    private static void BarsSection(
        IContainer container,
        string title,
        IReadOnlyList<StatisticBar> bars,
        string caption)
    {
        container.Column(section =>
        {
            section.Item().PaddingTop(10).PaddingBottom(6).Text(title).FontSize(12).Bold();

            if (bars.Count == 0)
            {
                section.Item().Text("Няма данни за тази група.").FontSize(9);
                return;
            }

            var widest = Math.Max(1, bars.Max(bar => bar.Count));

            foreach (var bar in bars)
            {
                section.Item().PaddingVertical(2).Row(row =>
                {
                    row.ConstantItem(120).AlignMiddle().Text(bar.Label).FontSize(8);

                    row.RelativeItem().AlignLeft().AlignMiddle()
                        .Width(Math.Max(4, MaxBarWidth * bar.Count / widest))
                        .Height(11)
                        .Background(Color.FromHex(HeatHex(bar.Score)));

                    row.ConstantItem(84).AlignRight().AlignMiddle().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8));
                        text.Span($"{bar.Count} ");
                        text.Span($"· {bar.Score:0.0}").FontColor(Color.FromHex(MutedHex));
                    });
                });
            }

            section.Item().PaddingTop(2).Text(caption).FontSize(7).FontColor(Color.FromHex(MutedHex));
        });
    }

    private static void HeatSection(
        IContainer container,
        IReadOnlyList<string> categories,
        IReadOnlyList<HeatCell> heat)
    {
        var densest = Math.Max(1, heat.Max(cell => cell.Count));

        container.Column(section =>
        {
            section.Item().PaddingTop(12).PaddingBottom(6).Text("Топлинна карта: категории по статус").FontSize(12).Bold();

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(110);

                    foreach (var _ in ReportStatuses.All)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeatHeaderCell).AlignLeft().Text("Категория");

                    foreach (var status in ReportStatuses.All)
                    {
                        header.Cell().Element(HeatHeaderCell).AlignCenter().Text(status);
                    }
                });

                foreach (var category in categories)
                {
                    table.Cell().Element(HeatLabelCell).Text(category);

                    foreach (var status in ReportStatuses.All)
                    {
                        var cell = heat.First(heatCell =>
                            heatCell.Category == category && heatCell.Status == status);

                        table.Cell()
                            .Background(Color.FromHex(DensityHex(cell.Count, densest)))
                            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
                            .PaddingVertical(6)
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.DefaultTextStyle(style => style
                                    .FontSize(8)
                                    .FontColor(Color.FromHex(cell.Count == 0 ? "#9AA69E" : "#12261F")));
                                text.Span(cell.Count == 0 ? "—" : cell.Count.ToString());
                            });
                    }
                }
            });

            section.Item().PaddingTop(3).Text("Колкото по-тъмна клетката, толкова повече сигнали има в нея.").FontSize(7);
        });
    }

    private static IContainer HeatHeaderCell(IContainer container) =>
        container.Background(Color.FromHex(GreenHex))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4);

    private static IContainer HeatLabelCell(IContainer container) =>
        container.Background(Color.FromHex("#F0F6F1"))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4);

    private static void OpenReportsSection(IContainer container, IReadOnlyList<ReportExportRow> open)
    {
        container.Column(section =>
        {
            section.Item().PaddingTop(14).PaddingBottom(6).Text("Сигнали, които чакат решение").FontSize(12).Bold();

            if (open.Count == 0)
            {
                section.Item().Text("Няма активни сигнали в тази група.").FontSize(9);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(42);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(56);
                });

                table.Header(header =>
                {
                    foreach (var label in new[] { "№", "Заглавие", "Категория", "Статус", "Приоритет", "Локация", "Дата" })
                    {
                        header.Cell().Element(ListHeaderCell).Text(label);
                    }
                });

                var index = 0;

                foreach (var row in open)
                {
                    var shade = index % 2 == 1 ? "#EFF4F0" : "#FFFFFF";

                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text($"#{row.Id}");
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.Title);
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.Category);
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.Status);
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(text =>
                    {
                        text.DefaultTextStyle(style => style.Bold().FontColor(Color.FromHex(HeatHex(row.Priority))));
                        text.Span(row.Priority.ToString("0.0"));
                    });
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(ReportStatistics.LocationOf(row.Address));
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.CreatedAt.ToSofia().ToString("dd.MM.yyyy"));

                    index++;
                }
            });
        });
    }

    private static IContainer ListHeaderCell(IContainer container) =>
        container.Background(Color.FromHex(GreenHex))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4);

    private static IContainer ListBodyCell(IContainer container, string hex) =>
        container.Background(Color.FromHex(hex))
            .BorderBottom(1).BorderColor(Color.FromHex("#C9D6CD"))
            .PaddingVertical(5).PaddingHorizontal(4);

    // Същите цветове като маркерите на картата: документът и сайтът
    // не бива да описват един сигнал с различни багри.
    private static string HeatHex(double score) => score switch
    {
        >= HighPriorityFrom => "#C2413B",
        >= 40 => "#D9A406",
        > 0 => "#2E7D5B",
        _ => "#9AA69E"
    };

    // Наситеността се сравнява с най-пълната клетка, а не с фиксирано
    // число: един отчет с три сигнала изглежда също като един с треста.
    private static string DensityHex(int count, int densest)
    {
        if (count == 0) return "#F4F7F4";
        if (count >= densest) return GreenHex;
        if (count >= 2) return "#4E8B6B";

        return "#B8D6C4";
    }
}
