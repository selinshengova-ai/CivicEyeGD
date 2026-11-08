using System.Globalization;
using CivicEyeGD.Data;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CivicEyeGD.Services;

/// <summary>
/// PDF отчетът е документът, който службата оставя в папката си: резюме с
/// бройките и разпределението по статус, две диаграми, топлинна матрица
/// категория × статус и списък на сигналите, които все още чакат.
/// Всички числа идват от ReportStatistics, затова документът не може да
/// каже нещо различно от това, което таблото показва.
/// </summary>
public sealed class ReportPdfService
{
    // Прагът е взет от услугата, а не преписан: две различни числа за
    // едно и също „спешно“ биха изглеждали като грешка в отчета.
    public const double HighPriorityFrom = ReportPriorityService.UrgentScoreFrom;

    // Таванът на лентата е по-малък от ширината на съдържанието на A4
    // без полетата: редът пази място и за етикета вляво, и за числата
    // вдясно, така най-дългата лента никога не избутва данните извън
    // страницата.
    private const float MaxBarWidth = 300;

    // Процентната лента се оразмерява в точки, а не в дялове, защото
    // числото вътре в нея трябва да съвпада с мащаба ѝ: 520 от
    // възможните 543 точки оставят въздух и документът не се разпада,
    // ако полетата се променят.
    private const float MixBarWidth = 520;

    // Багрите са константи, а не числа в средата на реда: същото
    // мастило трябва да се повтаря във всяка секция, и само на едно
    // място може да бъде поправено, ако се смени марката на сайта.
    private const string InkHex = "#1D3028";
    private const string MutedHex = "#5C6B61";
    private const string GreenHex = "#1B4D3E";

    // Лимоненото оцветява само дебели линии и ръбове: като текст върху
    // бяло то е нечетимо, затова номерата на секциите ползват по-тъмната
    // му версия, а се вижда същата марка.
    private const string LimeHex = "#D7F36B";
    private const string LimeInkHex = "#7E9420";

    // Светлите полета са една и съща хартия навсякъде: кутиите, редуваните
    // редове и легендите трябва да си приличат, иначе документът изглежда
    // сглобен от три различни шаблона.
    private const string PanelHex = "#F0F6F1";
    private const string TrackHex = "#EDF2EE";
    private const string LineHex = "#DCE5D6";

    static ReportPdfService()
    {
        // QuestPDF е със свободен Community лиценз за проекти като този,
        // но изисква той да бъде заявен изрично при зареждане на библиотеката.
        Settings.License = LicenseType.Community;
    }

    // Документът се сглобява ред по ред с код, а не с готов шаблон:
    // на сървъра няма офис програма, която да запълни файл, а отделен
    // шаблон би се разминавал с речника от статуси и категории.
    // Когато и двете са в една сборка, нов статус сам получава колона.
    public byte[] Build(IReadOnlyList<ReportExportRow> rows, string listTitle, DateTime generatedAtUtc)
    {
        // Числата се събират преди рисуването: PDF не изпълнява код в
        // машината, която го отваря, затова всяка лента и всяка клетка
        // трябва да стигнат до страницата вече като готово число.
        var open = ReportStatistics.Open(rows);
        // Работната опашка идва вече наредена от статистиката: редът на
        // хартия е същият като този, по който списъкът се сортира на
        // екрана, а правилото се проверява с тест, не с око.
        var queue = ReportStatistics.ByUrgency(rows);
        var categories = ReportStatistics.ByCategory(rows);
        var locations = ReportStatistics.TopLocations(rows);
        var heat = ReportStatistics.HeatMatrix(rows);
        var heatCategories = ReportStatistics.HeatCategories(rows);
        // Часът идва в UTC, но се отпечатва в местното време: на хартия
        // службата чете часове, както ги вижда и в сайта, а
        // съхранението на сигнала остава в UTC за всеки случай.
        var generatedAt = generatedAtUtc.ToSofia();
        // Бройката се смята по същия праг като в таблото, а не с нова
        // формула: документът и екранът трябва да сочат едно число.
        var highCount = open.Count(row => row.Priority >= HighPriorityFrom);

        // Времента на службата се броят веднъж тук, от същите редове, от
        // които се броят и парите в кутиите: документ, в който числата за
        // реакцията идват от друго измерване, би се различал от таблото.
        // Дните на изчакване се мерят спрямо часа на документа, а не
        // спрямо сега: хартията се препрочита и месец по-късно и трябва
        // да казва същото, каквото е казала на деня, на който е пусната.
        var response = new ResponseSummary(
            ReportResponse.Average(rows.Select(row => row.ResponseDays)),
            ReportResponse.Longest(rows.Select(row => row.ResponseDays)),
            open.Count(row => row.ResponseDays is null),
            open.Count == 0
                ? null
                : open.Max(row => ReportResponse.DaysBetween(row.CreatedAt, generatedAt)));

        // Едно броене по статус за цялата секция: лентата, легендата и
        // числата в кутиите четат този списък, и ако всяко си брои само,
        // процентите на лентата няма да съвпадат с бройките над тях.
        var byStatus = ReportStatuses.All
            .Select(status => (Status: status, Count: rows.Count(row => row.Status == status)))
            .ToList();

        // Готовият документ се връща като байтове: нищо не се пише по
        // диска на сървъра, затова няма временен файл, който друга
        // заявка би могла да отвори, и няма нищо за изтриване след това.
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                // Базовият шрифт е обявен веднъж за цялата страница:
                // промяната на размера се прави на едно място. Дребният
                // шрифт е нужен, защото списъкът има осем колони и
                // трябва да остане в ширината на A4.
                page.DefaultTextStyle(text => text.FontSize(9).FontColor(Color.FromHex(InkHex)));

                page.Header().Element(header => ComposeHeader(header, listTitle, rows.Count, generatedAt));

                page.Content().PaddingVertical(12).Column(content =>
                {
                    // EqualSpacing заделя място между секциите, без всяка
                    // да го иска от своята рамка: разделителната линия на
                    // заглавието трябва да се вижда, а не да липва в
                    // лентите над нея.
                    content.Spacing(14);

                    content.Item().Column(summary =>
                    {
                        summary.Spacing(8);

                        summary.Item().Element(head => SectionHeading(
                            head, "01", "Резюме",
                            $"{open.Count} чакат · {rows.Count - open.Count} приключени"));

                        summary.Item().Row(boxes =>
                        {
                            // Четирите полета са равни, защото деленето на
                            // ширината е зададено, а не изчислено спрямо
                            // текста: кутиите не се разместват при дълго
                            // заглавие на списъка.
                            // „Приключени“ е общият брой минус чакащите,
                            // затова двете числа винаги дават точния сбор.
                            boxes.RelativeItem().Element(box =>
                                KpiBox(box, "Сигнали в отчета", rows.Count.ToString(), "избраният филтър"));
                            boxes.RelativeItem().PaddingLeft(8).Element(box =>
                                KpiBox(box, "Чакат решение", open.Count.ToString(),
                                    Share(open.Count, rows.Count) + " от общо"));
                            boxes.RelativeItem().PaddingLeft(8).Element(box =>
                                KpiBox(box, "Приключени", (rows.Count - open.Count).ToString(),
                                    "Решени и отхвърлени"));
                            boxes.RelativeItem().PaddingLeft(8).Element(box =>
                                KpiBox(box, "Висок приоритет", highCount.ToString(),
                                    $"над {HighPriorityFrom:0} точки"));
                        });

                        summary.Item().Element(box => ResponseLine(box, response));

                        summary.Item().Element(section => StatusMixSection(section, byStatus, rows.Count));
                    });

                    content.Item().Element(section =>
                        BarsSection(section, "02", "Сигнали по категория", categories, rows.Count));

                    content.Item().Element(section =>
                        BarsSection(section, "03", "Най-натоварени места", locations, rows.Count));

                    if (heat.Count > 0)
                    {
                        // Матрицата се пропуска, когато няма какво да
                        // се брои: празна таблица би заела място над
                        // списъка и би приличала на сгрешени данни.
                        content.Item().Element(section => HeatSection(section, heatCategories, heat));
                    }

                    content.Item().Element(section => OpenReportsSection(section, queue, generatedAt));

                    content.Item().Element(section => NotesSection(section, rows.Count));
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    // Заглавната част е една и съща при всеки филтър, за да се разпознава
    // отпечатаният лист и без корица. Името на списъка и бройката идват
    // отвън: служителят трябва да вижда на хартия същия филтър, който е
    // избрал на екрана, а не заглавие, еднакво за всички филтри.
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
                // Бройката е дописана до заглавието, а не оставена само в
                // първата кутия: който цитира документа по телефона, казва
                // „тези стотина сигнала“, и числото трябва да е на
                // видимото място, а не под четирите полета.
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Color.FromHex(MutedHex)));
                text.Span($"{count} сигнала в документа · числата са към момента на генерирането");
            });

            header.Item().PaddingTop(10).Height(2).Background(Color.FromHex(LimeHex));
        });
    }

    // Секциите са номерирани, защото хартиеният документ се чете на части
    // и се цитира: „върни се на точка 3“ е възможно, „върни се на
    // диаграмата“ е два въпроса, когато диаграмите са две.
    private static void SectionHeading(IContainer container, string number, string title, string? note = null)
    {
        container.Column(head =>
        {
            head.Item().Row(row =>
            {
                row.ConstantItem(20).Text(number).FontSize(9).Bold().FontColor(Color.FromHex(LimeInkHex));
                row.RelativeItem().Text(title).FontSize(13).Bold();

                // Пояснението е отдясно на същия ред: то казва по каква
                // мярка се чете секцията, а не отваря нов параграф, и не
                // бива да бута заглавието на следващия ред.
                if (note != null)
                {
                    row.ConstantItem(190).AlignRight().Text(note).FontSize(8).FontColor(Color.FromHex(MutedHex));
                }
            });

            head.Item().PaddingTop(4).Height(1).Background(Color.FromHex(LineHex));
        });
    }

    // Кутията показва число, неговото обяснение и с какво се мери:
    // погледът спира върху едрия брой, надписът се чете при втори поглед,
    // а третият ред пази числото от погрешно тълкуване.
    // Лимоненият акцент е отделен елемент, а не оцветен ръб: библиотеката
    // оцветва всички ръбове на кутията с едно и също мастило, така че
    // маркираща ивица само отляво може да се сложи единствено като блок.
    private static void KpiBox(IContainer container, string label, string value, string note)
    {
        container.Row(box =>
        {
            box.ConstantItem(3).Background(Color.FromHex(LimeHex));

            box.RelativeItem()
                .Background(Color.FromHex(PanelHex))
                .Border(1).BorderColor(Color.FromHex(LineHex))
                .CornerRadius(6)
                .Padding(10)
                .Column(content =>
                {
                    content.Item().Text(label).FontSize(8).FontColor(Color.FromHex(MutedHex));
                    content.Item().PaddingTop(2).Text(value).FontSize(18).Bold();
                    content.Item().PaddingTop(2).Text(note).FontSize(7).FontColor(Color.FromHex(MutedHex));
                });
        });
    }

    // Трите числа за реакцията са на едно място, защото всяко от тях
    // може да липсва: документ, по който няма нито една намеса, не
    // трябва да печате „средно 0 дни“, а документ без чакащи не трябва
    // да брои изчакване, което няма.
    private sealed record ResponseSummary(
        double? AverageDays,
        int? LongestDays,
        int AwaitingCount,
        int? OldestWaitDays)
    {
        // Изречението се сглобява тук, а не при рисуването: кое число
        // кога се пропуска е правило за съдържанието, не за хартията.
        public string Wording() => AverageDays is null
            ? "по нито един от тези сигнали няма записана намеса"
            : $"средно {ReportResponse.Days(AverageDays)} до първата намеса, "
              + $"най-бавна {ReportResponse.Days(LongestDays)}, "
              + (AwaitingCount == 0
                    ? "всички чакащи сигнали вече имат такава"
                    : $"{AwaitingCount} {BulgarianPlural.Of(AwaitingCount, "сигнал", "сигнала")} още чакат"
                      + (OldestWaitDays is { } oldest
                            ? $" (най-старият — {ReportResponse.Days(oldest)})"
                            : string.Empty));
    }

    // Лентата за реакцията е един ред, а не четвърта кутия: трите ѝ
    // числа се отнасят за времето, докато кутиите броят сигнали, и
    // смесени в еднаква визия биха се чели като еднаква мярка.
    // Ивица отляво е блок, а не оцветен ръб, по същата причина като
    // в кутиите — библиотеката оцветява всички ръбове наведнъж.
    private static void ResponseLine(IContainer container, ResponseSummary response)
    {
        container.Row(line =>
        {
            line.ConstantItem(3).Background(Color.FromHex(LimeHex));

            line.RelativeItem()
                .Background(Color.FromHex(PanelHex))
                .Border(1).BorderColor(Color.FromHex(LineHex))
                .CornerRadius(6)
                .PaddingVertical(8).PaddingHorizontal(10)
                .Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8.5f).FontColor(Color.FromHex(InkHex)));

                    text.Span("Реакция на общината · ").Bold();
                    text.Span(response.Wording());
                });
        });
    }

    // Лентата отговаря на първия въпрос на службата — „какво е станало със
    // сигналите“ — и затова е първата графика в документа. Пропорциите са
    // истински: един статус заема толкова от ширината, колкото е дялът му,
    // а не толкова, колкото е удобно за оформлението.
    private static void StatusMixSection(
        IContainer container,
        IReadOnlyList<(string Status, int Count)> byStatus,
        int total)
    {
        container.Column(section =>
        {
            if (total == 0)
            {
                section.Item().Text("Няма сигнали за тази група.").FontSize(9);
                return;
            }

            section.Item().Row(bar =>
            {
                foreach (var (status, count) in byStatus)
                {
                    // Статус без сигнал не получава ивица, но остава в
                    // легендата: парче от нула точки няма какво да покаже,
                    // а липсващият статус трябва да се вижда.
                    if (count == 0) continue;

                    var width = MixBarWidth * count / total;

                    // Числото се печата вътре в ивицата, само когато тя е
                    // достатъчно широка: на тясна ивица текстът би се
                    // отрязал, а бройката присъства в легендата и без това.
                    // Подравняването е след фона, а не преди него:
                    // преди — кутията се свива до ширината на числото и от
                    // ивицата остава тънка черта; след — ивицата запълва
                    // мярката си, а числото стои в средата ѝ.
                    bar.ConstantItem(width).AlignMiddle()
                        .Height(16)
                        .Background(Color.FromHex(StatusHex(status)))
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(8).Bold().FontColor(Colors.White));
                            if (width >= 26) text.Span(count.ToString());
                        });
                }
            });

            section.Item().PaddingTop(6).Row(legend =>
            {
                foreach (var (status, count) in byStatus)
                {
                    legend.RelativeItem().Row(dot =>
                    {
                        dot.ConstantItem(11).AlignMiddle().Height(9)
                            .Background(Color.FromHex(StatusHex(status)))
                            .Border(1).BorderColor(Color.FromHex(LineHex));

                        // Статусът се подписва с две числа — бройката и
                        // дялът: без второто служителят не знае дали 12
                        // сигнала са половината работа, или нейната десета.
                        dot.RelativeItem().AlignMiddle().PaddingLeft(4).Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(8));
                            text.Span($"{status} · {count} ");
                            text.Span(Share(count, total)).FontColor(Color.FromHex(MutedHex));
                        });
                    });
                }
            });

            section.Item().PaddingTop(4)
                .Text("Лентата е 100% от сигналите в документа.")
                .FontSize(7).FontColor(Color.FromHex(MutedHex));
        });
    }

    // Двете диаграми споделят една функция: категорията и мястото са
    // различни по съдържание, но не и по форма, а две копия на реда
    // биха се разминали при първата промяна на цвета или на шрифта.
    private static void BarsSection(
        IContainer container,
        string number,
        string title,
        IReadOnlyList<StatisticBar> bars,
        int total)
    {
        container.Column(section =>
        {
            section.Item().Element(head => SectionHeading(head, number, title));

            if (bars.Count == 0)
            {
                // Празен филтър е нормално състояние, а не грешка:
                // без този ред документът би показал секция без редове
                // и служителят не би разбрал дали данни липсват или са
                // изтрити.
                section.Item().PaddingTop(6).Text("Няма данни за тази група.").FontSize(9);
                return;
            }

            var widest = Math.Max(1, bars.Max(bar => bar.Count));

            foreach (var bar in bars)
            {
                section.Item().PaddingVertical(3).Row(row =>
                {
                    // Редът има две постоянни колони и една свободна:
                    // етикетите започват от лявата граница, а числата — от
                    // дясната, затова лентите могат да се сравняват по
                    // дължина, без никой ред да им пречи.
                    row.ConstantItem(118).AlignMiddle().Text(bar.Label).FontSize(8);

                    // Светлата шарка зад лентата е самият мащаб: тя показва
                    // колко остава до най-натоварения ред, вместо късата
                    // лента да изглежда като сгрешена мярка.
                    // Най-дългата лента в групата поема тавана, а
                    // останалите се оразмеряват спрямо нея, затова и
                    // малка група се чете също така ясно като голяма.
                    // Границата от 6 пази лентата видима и когато в нея
                    // има само един сигнал, а числото вътре излиза, едва
                    // когато мястото стига за трите цифри.
                    var filled = Math.Max(6, MaxBarWidth * bar.Count / widest);

                    row.ConstantItem(MaxBarWidth).AlignMiddle().Row(track =>
                    {
                        track.ConstantItem(filled).AlignMiddle().Height(12)
                            .Background(Color.FromHex(HeatHex(bar.Score)))
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.DefaultTextStyle(style => style.FontSize(7).Bold().FontColor(Color.FromHex(HeatInkHex(bar.Score))));
                                if (filled >= 22) text.Span(bar.Count.ToString());
                            });

                        track.RelativeItem().Height(12).Background(Color.FromHex(TrackHex));
                    });

                    row.RelativeItem().AlignRight().AlignMiddle().Text(text =>
                    {
                        // До лентата стоят трите числа, от които тя
                        // произлиза: бройката, средният приоритет и дялът
                        // от документа. Без тях цветът би бил само
                        // украшение, а отчетът не се защитава с украшение.
                        text.DefaultTextStyle(style => style.FontSize(8));
                        text.Span($"{bar.Count} ");
                        text.Span($"· {NumberText.Score(bar.Score)} · {Share(bar.Count, total)}")
                            .FontColor(Color.FromHex(MutedHex));
                    });
                });
            }

            section.Item().PaddingTop(4)
                .Text($"брой сигнали · среден приоритет · дял от {total} сигнала")
                .FontSize(7).FontColor(Color.FromHex(MutedHex));
        });
    }

    // Матрицата отговаря на въпроса „къде закъсва работата“:
    // категорията показва натоварването, а статусът набелязва
    // групите, които още не са приключени.
    private static void HeatSection(
        IContainer container,
        IReadOnlyList<string> categories,
        IReadOnlyList<HeatCell> heat)
    {
        var densest = Math.Max(1, heat.Max(cell => cell.Count));

        container.Column(section =>
        {
            section.Item().Element(head => SectionHeading(
                head, "04", "Категории по статус", $"най-натоварена клетка: {densest}"));

            section.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(110);

                    foreach (var _ in ReportStatuses.All)
                    {
                        // Една колона за всеки статус от речника, а не
                        // само за тези, които филтрираният списък
                        // съдържа: липсващият статус остава видим като
                        // празна колона, вместо да изчезне от таблицата.
                        columns.RelativeColumn();
                    }

                    // Колоната „Общо“ сваля реда наведнъж: без нея
                    // служителят събира четири клетки на ум, а първата
                    // грешка в такава сметка попада в протокола.
                    columns.ConstantColumn(46);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeatHeaderCell).AlignLeft().Text("Категория");

                    foreach (var status in ReportStatuses.All)
                    {
                        header.Cell().Element(HeatHeaderCell).AlignCenter().Text(status);
                    }

                    header.Cell().Element(HeatHeaderCell).AlignCenter().Text("Общо");
                });

                foreach (var category in categories)
                {
                    table.Cell().Element(HeatLabelCell).Text(category);

                    var rowTotal = 0;

                    foreach (var status in ReportStatuses.All)
                    {
                        // Клетката се търси по двойката категория и
                        // статус, защото матрицата съдържа и нулевите
                        // стойности: липсата на сигнал е резултат, а
                        // не пропуснат ред, затова се печата с тире.
                        var cell = heat.First(heatCell =>
                            heatCell.Category == category && heatCell.Status == status);

                        rowTotal += cell.Count;

                        table.Cell()
                            .Background(Color.FromHex(DensityHex(cell.Count, densest)))
                            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
                            .PaddingVertical(6)
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.DefaultTextStyle(style => style
                                    .FontSize(8)
                                    .FontColor(Color.FromHex(cell.Count == 0 ? "#9AA69E" : DensityInkHex(cell.Count, densest))));
                                text.Span(cell.Count == 0 ? "—" : cell.Count.ToString());
                            });
                    }

                    // Сборът е в по-светла колона, за да не се мисли, че е
                    // още една клетка от матрицата: той е друга мярка и
                    // трябва да изглежда като такава.
                    table.Cell()
                        .Background(Color.FromHex(PanelHex))
                        .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
                        .PaddingVertical(6)
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.DefaultTextStyle(style => style.Bold().FontSize(8));
                            text.Span(rowTotal.ToString());
                        });
                }
            });

            // Легендата е с квадратчета, а не с изречение: читателят
            // съпоставя знака с клетката за миг, докато при текст първо
            // трябва да разбере какво значат думите „по-тъмна“.
            section.Item().PaddingTop(6).Row(legend =>
            {
                foreach (var count in new[] { 0, 1, densest }.Distinct())
                {
                    legend.RelativeItem().Row(dot =>
                    {
                        dot.ConstantItem(11).AlignMiddle().Height(9)
                            .Background(Color.FromHex(DensityHex(count, densest)))
                            .Border(1).BorderColor(Color.FromHex(LineHex));

                        dot.RelativeItem().AlignMiddle().PaddingLeft(4)
                            .Text(count == densest && densest > 1 ? $"{count} · макс." : count.ToString())
                            .FontSize(7).FontColor(Color.FromHex(MutedHex));
                    });
                }
            });
        });
    }

    // Заглавният ред на матрицата е тъмнозелен, затова надписите по него
    // трябва да са светли: с мастилото от страницата те биха излезли тъмни
    // върху тъмно и колоните щяха да останат без имена.
    private static IContainer HeatHeaderCell(IContainer container) =>
        container.Background(Color.FromHex(GreenHex))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4)
            .DefaultTextStyle(style => style.FontSize(8).Bold().FontColor(Colors.White));

    private static IContainer HeatLabelCell(IContainer container) =>
        container.Background(Color.FromHex(PanelHex))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4);

    // Списъкът включва само чакащите сигнали: приключените са вече
    // обобщени в матрицата, а на служителя трябва да му е видно с
    // какво остава да се занимава.
    // Редът идва отвън, вече по спешност: на хартия служителят решава с
    // какво започва, и най-спешното трябва да е първият ред, когото
    // вижда, но самото правило е на статистиката, не на документа.
    private static void OpenReportsSection(
        IContainer container,
        IReadOnlyList<ReportExportRow> queue,
        DateTime generatedAt)
    {
        container.Column(section =>
        {
            section.Item().Element(head => SectionHeading(
                head, "05", "Сигнали, които чакат решение", $"{queue.Count} реда · подредени по приоритет"));

            if (queue.Count == 0)
            {
                // Празният списък е добрата новина, а не пропуснатата
                // секция: с този ред документът не изглежда непълен.
                section.Item().PaddingTop(6).Text("Няма активни сигнали в тази група.").FontSize(9);
                return;
            }

            section.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    // Номерът, приоритетът и датата са с постоянна ширина, а
                    // текстовите колони се делят съразмерно: заглавието носи
                    // съдържанието, затова получава най-голям дял.
                    // № е толкова широко, че „#125“ да остане на един ред —
                    // счупен номер се чете като два различни сигнала.
                    columns.ConstantColumn(38);
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(50);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(52);
                    // Колоната „Чака“ е с постоянна ширина: в нея винаги
                    // стои едно малко число с думата за дни, а мястото му
                    // се вади от текстовите колони, чието съдържание се
                    // пречупва и на две реда без вреда.
                    columns.ConstantColumn(44);
                });

                table.Header(header =>
                {
                    foreach (var label in new[] { "№", "Заглавие", "Категория", "Статус", "Приоритет", "Място", "Дата", "Чака" })
                    {
                        header.Cell().Element(ListHeaderCell).Text(label);
                    }
                });

                var index = 0;

                foreach (var row in queue)
                {
                    // Редуването на фона е само за четене: очите следват
                    // реда през осем колони, без да го преброяват.
                    var shade = index % 2 == 1 ? "#EFF4F0" : "#FFFFFF";

                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text($"#{row.Id}");
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.Title);
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(row.Category);

                    // Статусът е оцветен с багрите на етикетите в
                    // публичния списък, за да може човекът с разпечатания
                    // лист да сочи с пръст и да каже „това е синьото“.
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(text =>
                    {
                        text.DefaultTextStyle(style => style
                            .FontSize(8)
                            .Bold()
                            .FontColor(Color.FromHex(StatusHex(row.Status))));
                        text.Span(row.Status);
                    });

                    table.Cell().Element(cell => ListBodyCell(cell, shade))
                        .AlignLeft().AlignMiddle()
                        .Element(cell => PriorityChip(cell, row.Priority));

                    // Мястото се вади по същия начин като в диаграмата,
                    // затова двете части на документа сочат една улица.
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(ReportStatistics.LocationOf(row.Address));
                    // Редът вече носи местното време: таблото го превръща
                    // веднъж. Повторно превръщане би изместило датата с още
                    // един часов пояс, а при късен сигнал би показал и грешен
                    // ден.
                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                        text.Span(row.CreatedAt.ToString("dd.MM.yyyy"));
                    });

                    // Дните на изчакване се мерят до часа на документа, а
                    // не до днес: препрочет след месец, той трябва да
                    // казва същото, каквото е казал на деня си.
                    var waited = ReportResponse.DaysBetween(row.CreatedAt, generatedAt);

                    // Бавното се различава с удебелен червен текст, а не с
                    // трети фон: в ред, който вече носи оцветен статус и
                    // оцветен приоритет, още една светлина зад цифрите би
                    // креснала, а текстът остава четим и на сивия принтер.
                    var slow = waited >= ReportResponse.SlowWaitDays;

                    table.Cell().Element(cell => ListBodyCell(cell, shade)).Text(text =>
                    {
                        text.DefaultTextStyle(style =>
                            slow
                                ? style.FontSize(8).Bold().FontColor(Color.FromHex("#A8322C"))
                                : style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                        text.Span(ReportResponse.Days(waited));
                    });

                    index++;
                }
            });
        });
    }

    // Приоритетът се печата като етикет със светъл фон и багра от скалата:
    // самото число се сравнява трудно с поглед, а една и съща светлина за
    // „висок“ в диаграмите, в лентите и в този списък прави документа
    // четим без измервателна уредба.
    private static void PriorityChip(IContainer container, double priority)
    {
        var (background, foreground) = PriorityChipHex(priority);

        container
            .Background(Color.FromHex(background))
            .Border(1).BorderColor(Color.FromHex(foreground))
            .CornerRadius(8)
            .PaddingVertical(1).PaddingHorizontal(5)
            .Text(text =>
            {
                text.DefaultTextStyle(style => style.Bold().FontColor(Color.FromHex(foreground)));
                text.Span(NumberText.Score(priority));
            });
    }

    private static (string Background, string Text) PriorityChipHex(double score) => score switch
    {
        >= HighPriorityFrom => ("#FBE7E6", "#A8322C"),
        >= 40 => ("#FFF2D6", "#8A6410"),
        > 0 => ("#E4F2E9", "#1F5B3E"),
        _ => ("#F1F4F1", "#6B7A72")
    };

    private static IContainer ListHeaderCell(IContainer container) =>
        container.Background(Color.FromHex(GreenHex))
            .Border(1).BorderColor(Color.FromHex("#FFFFFF"))
            .PaddingVertical(5).PaddingHorizontal(4)
            .DefaultTextStyle(style => style.FontSize(8).Bold().FontColor(Colors.White));

    private static IContainer ListBodyCell(IContainer container, string hex) =>
        container.Background(Color.FromHex(hex))
            .BorderBottom(1).BorderColor(Color.FromHex("#C9D6CD"))
            .PaddingVertical(5).PaddingHorizontal(4);

    // Формулата и праговете са в рамка, а не в дребен шрифт накрая:
    // службата преизчислява приоритета по тях, и това е частта, която се
    // търси с пръст, когато някой оспори реда на списъка.
    private static void NotesSection(IContainer container, int total)
    {
        container.Column(notes =>
        {
            notes.Item().Element(head => SectionHeading(head, "06", "Как се смята приоритетът"));

            notes.Item().PaddingTop(6)
                .Background(Color.FromHex(PanelHex))
                .Border(1).BorderColor(Color.FromHex(LineHex))
                .CornerRadius(6)
                .Padding(12)
                .Column(box =>
                {
                    box.Item().Text(text =>
                    {
                        // Таванът от 10 за потвържденията е написан изрично,
                        // защото без него сборът от надписите не дава 140.
                        text.DefaultTextStyle(style => style.FontSize(8));
                        text.Span("Приоритет = (20×средна сериозност + 3×независими потвърждения, до 10, "
                            + "+ административна корекция 0–10) ÷ 140 × 100. ");
                        text.Span("Оценките на подателя за собствения му сигнал не участват в сметката.")
                            .FontColor(Color.FromHex(MutedHex));
                    });

                    box.Item().PaddingTop(8).Row(scale =>
                    {
                        foreach (var (label, score) in new (string Label, double Score)[]
                        {
                            ($"Висок · над {HighPriorityFrom:0} точки", HighPriorityFrom),
                            ("Среден · 40–69 точки", 55d),
                            ("Нисък · под 40 точки", 20d)
                        })
                        {
                            scale.RelativeItem().Row(dot =>
                            {
                                dot.ConstantItem(11).AlignMiddle().Height(9)
                                    .Background(Color.FromHex(HeatHex(score)));

                                dot.RelativeItem().AlignMiddle().PaddingLeft(4)
                                    .Text(label).FontSize(7).FontColor(Color.FromHex(MutedHex));
                            });
                        }
                    });
                });

            notes.Item().PaddingTop(6).Text(text =>
            {
                // Единицата на меренето е посочвана, защото числата без нея
                // се четат като проценти: „78“ в приоритет и „78%“ в
                // разпределението са две различни неща, а стоят на една
                // страница.
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Color.FromHex(MutedHex)));
                text.Span($"Всички бройки в този документ са спрямо {total} ");
                text.Span(total == 1 ? "сигнал" : "сигнала");
                text.Span(". Приоритетът е точка от 0 до 100, не процент.");
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(foot =>
        {
            foot.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                text.Span("CivicEye GD · civiceye.runasp.net · документ, генериран от платформата");
            });

            // Хартиеният документ се цитира по страница, затова
            // номерът и общият брой се изчисляват при самото
            // разбиване и не изостават, ако секциите се преместят.
            foot.ConstantItem(90).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Color.FromHex(MutedHex)));
                text.Span("стр. ");
                text.CurrentPageNumber();
                text.Span(" от ");
                text.TotalPages();
            });
        });
    }

    // Скалата на приоритета е в този файл, защото документът и сайтът
    // описват един сигнал с едни и същи багри.
    // Границите 70 и 40 съвпадат с етикетите на приоритета: думите
    // „Висок“ и „Среден“ и цветовете трябва да описват един сигнал по
    // един и същ начин, иначе списъкът противоречи на диаграмата.
    private static string HeatHex(double score) => score switch
    {
        >= HighPriorityFrom => "#C2413B",
        >= 40 => "#D9A406",
        > 0 => "#2E7D5B",
        _ => "#9AA69E"
    };

    // Мастилото вътре в лентата се избира по багата на самата лента:
    // върху кехлибарения „среден“ и върху сивото нулевият надписът изчезва,
    // затова там числото се печата с тъмно, а по-ситните червено и зелено
    // си остават с бяло.
    private static string HeatInkHex(double score) => score switch
    {
        >= HighPriorityFrom => "#FFFFFF",
        >= 40 => "#3A2A04",
        > 0 => "#FFFFFF",
        _ => "#25332B"
    };

    // Статусите ползват багрите на етикетите в публичния списък.
    // Отхвърленият е неутрално сив: червеното е запазено за спешност по
    // скалата на приоритета, а отказан сигнал не е спешен — той е
    // приключен и не търси действие.
    private static string StatusHex(string status) => status switch
    {
        ReportStatuses.Resolved => "#347447",
        ReportStatuses.InProgress => "#365F9B",
        ReportStatuses.New => "#94631F",
        ReportStatuses.Rejected => "#5E6A63",
        _ => MutedHex
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

    // Клетка, равна на най-натоварената, е тъмнозелена: с мастилото на
    // страницата числото в нея изчезва, затова там се печата на бяло.
    private static string DensityInkHex(int count, int densest) =>
        count >= densest ? "#FFFFFF" : "#12261F";

    // Дялът се закръгля до цяло число: втората десетична цифра е по-малка
    // от ширината на един знак в лентата, а службата сравнява части, не
    // стойности с калкулатор. Форматът е инвариантен, за да не излезе
    // „33,3%“ на машина с българска култура и „33.3%“ на друга.
    private static string Share(int count, int total) =>
        total == 0
            ? "0%"
            : (100.0 * count / total).ToString("0", CultureInfo.InvariantCulture) + "%";
}
