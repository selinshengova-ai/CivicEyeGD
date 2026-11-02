using System.Globalization;

namespace CivicEyeGD.Services;

// Сигналите се пазят в UTC, но гражданите гледат часове от Гоце Делчев.
// Конвертация със сървърния часовник показва грешен час, когато
// приложението работи на машина в друга зона, затова зоната е явна.
public static class CivicTime
{
    // Зоната се открива веднъж при стартиране и се помни: тя не се
    // променя по време на работа, а всяко форматиране на дата би я
    // търсило из системата отново и отново без нужда.
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        // Един и същи часовник, но с две имена: IANA-идентификаторът
        // Europe/Sofia го ползват Linux и macOS (обикновено хостингът),
        // а „Eastern Standard Time“ е името при Windows (машината за
        // разработка); въпреки „Standard“ в името то носи и правилата
        // за лятното време. Пробваме ги последователно, за да не пада
        // приложението само заради разликата в именуванията.
        foreach (var id in new[] { "Europe/Sofia", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            // И двете грешки се поглъщат тук, защото след тях остава
            // още един кандидат, а после — безопасен завършек в UTC.
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Ако и двете имена липсват (счупена система), падаме към UTC:
        // часът ще е различен от местния, но страницата ще живее —
        // по-добре объркан часовник, отколкото сринато приложение.
        return TimeZoneInfo.Utc;
    }

    // Разширение, а не статичен метод: извикването се чете като верига
    // от думи — report.CreatedAt.ToSofia() — без повтаряне на класа.
    public static DateTime ToSofia(this DateTime value)
    {
        // Стойността идва от базата без информация за зона, затова
        // Kind обикновено е Unspecified. Такъв се приема за UTC —
        // защото и е записан като UTC; SpecifyKind само слага етикета
        // и не мести самото време.
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    // Инвариантният формат не зависи от това дали сървърът е с
    // българска или английска култура — датите не се разместват.
    public static string ToDayText(this DateTime value) =>
        value.ToSofia().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    // Отделен формат с час: списъците се различават по ден, а дневникът
    // на сигнала — по момент, затова са две отделни имена вместо един
    // метод с два режима.
    public static string ToMomentText(this DateTime value) =>
        value.ToSofia().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
