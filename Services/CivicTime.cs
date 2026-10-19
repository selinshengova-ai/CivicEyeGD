using System.Globalization;

namespace CivicEyeGD.Services;

// Сигналите се пазят в UTC, но гражданите гледат часове от Гоце Делчев.
// Конвертация със сървърния часовник показва грешен час, когато
// приложението работи на машина в друга зона, затова зоната е явна.
public static class CivicTime
{
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Sofia", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    public static DateTime ToSofia(this DateTime value)
    {
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

    public static string ToMomentText(this DateTime value) =>
        value.ToSofia().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
