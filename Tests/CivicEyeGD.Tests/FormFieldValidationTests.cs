using System.Reflection;
using CivicEyeGD.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CivicEyeGD.Tests;

/// <summary>
/// Двете правила върху полетата, които идват от форма.
///
/// Първо: всяко поле, което може да остане празно, трябва да е обявено за
/// nullable. При включени nullable препратки невъзможният за null string
/// се тълкува от връзката на моделите като задължително, а съобщението за
/// това идва от библиотеката и е на английски. Така полето „по желание“
/// при регистрация спря всеки, който го остави празно.
///
/// Второ: всеки атрибут, който може да покаже текст на човека, го носи
/// сам и на български. Приложение, което говори само български, не бива
/// да издава стандартните английски текстове на библиотеката.
/// </summary>
public sealed class FormFieldValidationTests
{
    // Атрибути, чийто текст се показва под полето. DataType и подобните
    // му не са тук: те описват полето, но не решават дали е валидно.
    private static readonly Type[] MessageAttributes =
    [
        typeof(RequiredAttribute),
        typeof(StringLengthAttribute),
        typeof(MinLengthAttribute),
        typeof(MaxLengthAttribute),
        typeof(LengthAttribute),
        typeof(RangeAttribute),
        typeof(RegularExpressionAttribute),
        typeof(EmailAddressAttribute),
        typeof(CompareAttribute)
    ];

    [Fact]
    public void EveryOptionalTextField_IsDeclaredNullable()
    {
        foreach (var field in BoundFields())
        {
            if (field.Property.PropertyType != typeof(string)) continue;
            if (field.Property.GetCustomAttribute<RequiredAttribute>() is not null) continue;
            if (field.Property.GetCustomAttribute<BindNeverAttribute>() is not null) continue;

            var writes = new NullabilityInfoContext()
                .Create(field.Property)
                .WriteState;

            Assert.True(
                writes == NullabilityState.Nullable,
                $"{field.Model.Name}.{field.Property.Name} няма Required, тоест е по желание, "
                + "но е декларирано като невъзможно за null: връзката на моделите го изисква "
                + "и отговаря с английско съобщение.");
        }
    }

    [Fact]
    public void EveryFrameworkMessage_IsWrittenInBulgarian()
    {
        foreach (var field in BoundFields())
        {
            foreach (var attribute in field.Property
                         .GetCustomAttributes<ValidationAttribute>()
                         .Where(a => MessageAttributes.Contains(a.GetType())))
            {
                var name = $"{field.Model.Name}.{field.Property.Name}: {attribute.GetType().Name}";

                Assert.False(
                    string.IsNullOrWhiteSpace(attribute.ErrorMessage),
                    name + " разчита на текста по подразбиране на библиотеката, а той е английски.");

                Assert.True(
                    attribute.ErrorMessage!.Any(IsCyrillic),
                    name + " носи съобщение без кирилица: " + attribute.ErrorMessage);
            }
        }
    }

    // Проверката минава през всичко, което страниците приемат като готов
    // модел: тестът трябва да спре нов проблем, а не само този, който вече
    // е поправен, затова нищо конкретно от него не е изписано тук.
    private static IEnumerable<(Type Model, PropertyInfo Property)> BoundFields()
    {
        var pages = typeof(RegisterModel).Assembly
            .GetTypes()
            .Where(t => typeof(PageModel).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var page in pages)
        {
            foreach (var bound in page.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (bound.GetCustomAttribute<BindPropertyAttribute>() is null) continue;

                var model = bound.PropertyType;

                // Проверяват се само класовете, написани в този проект:
                // отделните параметри (id, статус, оценка) нямат собствени
                // атрибути, а типове от библиотеките не са наш избор.
                if (!model.IsClass || model.Assembly != typeof(RegisterModel).Assembly) continue;

                foreach (var property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;

                    yield return (model, property);
                }
            }
        }
    }

    private static bool IsCyrillic(char c) => c >= '\u0400' && c <= '\u04FF';
}
