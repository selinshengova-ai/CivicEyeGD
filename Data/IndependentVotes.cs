using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Data;

/// <summary>
/// Правилото „гласът на подателя не е независим“ живее тук и никъде
/// другаде. То решава кой брои за сериозност (S) и кой за потвърждения
/// (C) във формулата за приоритет, затова всяко място, което ги чете,
/// минава през един и същи код: страниците, таблото, експортите и
/// картата.
///
/// Формите са две за една и съща причина: при зареден сигнал правилото
/// се прилага върху колекциите в паметта, а картата брои в самата база,
/// защото иначе тегли всички гласове на всички сигнали. EF Core не може
/// да превърне извикването на CLR метод в SQL, затова двете заявки са
/// тук, до правилото, а не по страниците — за да се вижда на едно
/// място, че казват едно и също.
/// </summary>
public static class IndependentVotes
{
    /// <summary>
    /// Истинно, когато гласуващият не е човекът, подал сигнала. Два
    /// null-a (потребител, изтрит от базата) си съответстват и затова
    /// не минават за независим глас; null срещу реален номер е различен
    /// и се брои.
    /// </summary>
    public static bool IsIndependent(string? voterUserId, string? reportUserId) =>
        voterUserId != reportUserId;

    /// <summary>Независимите оценки на един зареден сигнал.</summary>
    public static IEnumerable<SeverityRating> RatingsOf(Report report) =>
        report.SeverityRatings.Where(rating => IsIndependent(rating.UserId, report.UserId));

    /// <summary>
    /// Средната сериозност само от независимите оценки, закръглена до
    /// два знака. Без оцени дава 0 — ReportPriorityService тълкува нулата
    /// като най-ниска сериозност, затова сигнал не става спешен от
    /// липсата на гласове.
    /// </summary>
    public static double AverageSeverityOf(Report report)
    {
        var ratings = RatingsOf(report).ToList();

        return ratings.Count > 0
            ? Math.Round(ratings.Average(rating => rating.Rating), 2)
            : 0;
    }

    /// <summary>Броят на независимите оценки — колко малка е извадката.</summary>
    public static int RaterCountOf(Report report) => RatingsOf(report).Count();

    /// <summary>Броят на независимите потвърждения на зареден сигнал.</summary>
    public static int ConfirmationsOf(Report report) =>
        report.Confirmations.Count(confirmation => IsIndependent(confirmation.UserId, report.UserId));

    /// <summary>
    /// Същото правило, но изчислено от базата: ключът е номера на
    /// сигнала, а сигнал без нито една независима оценка липсва в
    /// речника, вместо да носи нула.
    /// </summary>
    public static async Task<Dictionary<int, double>> AverageSeverityByReportAsync(
        ApplicationDbContext context)
    {
        var rows = await (
            from rating in context.SeverityRatings.AsNoTracking()
            join report in context.Reports.AsNoTracking() on rating.ReportId equals report.Id
            // Със сравнение на местата, а не с IsIndependent отгоре: EF
            // Core превръща в SQL само изрази, не извиквания на CLR
            // метод. Промяната в правилото затова се прави на двете
            // места в този файл — те са един до друг точно за това.
            where rating.UserId != report.UserId
            group rating by rating.ReportId into grouped
            select new
            {
                ReportId = grouped.Key,
                Average = grouped.Average(rating => rating.Rating)
            })
            .ToListAsync();

        return rows.ToDictionary(row => row.ReportId, row => Math.Round(row.Average, 2));
    }

    /// <summary>Броят на независимите потвърждения по сигнал — същият
    /// филтър като средната сериозност, за да не дава картата по-висок
    /// приоритет от таблото.</summary>
    public static async Task<Dictionary<int, int>> ConfirmationCountByReportAsync(
        ApplicationDbContext context)
    {
        var rows = await (
            from confirmation in context.Confirmations.AsNoTracking()
            join report in context.Reports.AsNoTracking() on confirmation.ReportId equals report.Id
            // Пак сравнение на местата, не IsIndependent: заявката трябва
            // да остане преводима в SQL.
            where confirmation.UserId != report.UserId
            group confirmation by confirmation.ReportId into grouped
            select new
            {
                ReportId = grouped.Key,
                Count = grouped.Count()
            })
            .ToListAsync();

        return rows.ToDictionary(row => row.ReportId, row => row.Count);
    }
}
