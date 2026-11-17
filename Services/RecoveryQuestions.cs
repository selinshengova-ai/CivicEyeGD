using System.Security.Cryptography;
using System.Text;

namespace CivicEyeGD.Services;

// Въпросите и отговорите, с които се доказва самоличност при загубена
// парола. Класът е статичен и без състояние, защото правилата са едни
// и същи за трите места, които ги ползват: профила, регистрацията и
// страницата за възстановяване. Ако формулировките или нормализацията се
// намерят на две места, на едното рано или късно ще са други — и човекът
// няма да познае собствения си отговор.
public static class RecoveryQuestions
{
    // Какво се предлага като въпрос. Ключовете са стабилни: текстът може
    // да се препише, без да се променя нито един запис в базата.
    //
    // Подбрани са такива, чийто отговор не се намира в социалните мрежи и
    // не сочи къде живее човек. В приложение, в което гражданите качват
    // адреси на проблеми, въпрос от рода „на коя улица израсна“ или
    // „каква е рождената дата на майка ти“ би бил подарък за някого, който
    // иска да влезе в профила.
    public static readonly (string Key, string Text)[] Options =
    [
        ("first-pet", "Как се казваше първият ти домашен любимец?"),
        ("primary-school", "Как се казваше началното ти училище?"),
        ("favorite-book", "Коя е книгата, която си чел най-много пъти?"),
        ("favorite-film", "Кой е филмът, който си гледал най-много пъти?"),
        ("childhood-nickname", "Какъв прякор ти бяха дали близките?"),
        ("favorite-teacher", "Как се казваше любимият ти учител?")
    ];

    // Долната и горната граница на отговора. Еднакви са навсякъде, където
    // се попълва отговор, за да не излезе, че профилът е приел отговор,
    // който страницата за възстановяване после отказва да види.
    public const int MinAnswerLength = 2;
    public const int MaxAnswerLength = 60;

    // 100 000 кръга. Целта не е криптиране, а забавяне: този, който е
    // докопал файла с базата, трябва да си позволи най-много няколко
    // хиляди проверки в секунда, не милиони.
    private const int HashIterations = 100_000;

    // 16 случайни байта сол и 32 байта результат — размерите, с които
    // работи и самата библиотека на ASP.NET за паролите.
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Текст на въпроса по ключ; празен низ, ако ключът го няма.</summary>
    public static string TextFor(string? key)
        => Options.FirstOrDefault(option => option.Key == key).Text ?? string.Empty;

    /// <summary>Ключът е от посочените тук, а не написан на ръка в заявката.</summary>
    public static bool IsKnown(string? key)
        => Options.Any(option => option.Key == key);

    // Отговорът се сравнява без значение от главни и малки букви и без
    // излишните интервали: „Карабайчо  “, „карабайчо“ и „Карабайчо“ са
    // един и същ отговор за човека, а за хеша биха били три различни.
    public static string Normalize(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var words = answer
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToLowerInvariant());

        return string.Join(' ', words);
    }

    public static string NewSalt()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes));

    /// <summary>Бавният хеш, който влиза в базата вместо самия отговор.</summary>
    public static string Hash(string? answer, string salt)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(Normalize(answer)),
            Convert.FromBase64String(salt),
            HashIterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return Convert.ToBase64String(bytes);
    }

    // Сравнението е с FixedTimeEquals, а не с ==: броят на съвпадащите
    // начални символи не трябва да се усеща в отговорното време.
    public static bool Verify(string? answer, string salt, string expectedHash)
    {
        byte[] actual;
        byte[] expected;

        try
        {
            actual = Convert.FromBase64String(Hash(answer, salt));
            expected = Convert.FromBase64String(expectedHash);
        }
        catch (FormatException)
        {
            // Повреден или ръчно наместен запис не се смята за познаване.
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
