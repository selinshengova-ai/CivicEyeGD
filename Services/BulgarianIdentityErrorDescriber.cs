using Microsoft.AspNetCore.Identity;

namespace CivicEyeGD.Services;

// Identity връща съобщенията си на английски, а всички останали текстове
// в приложението са български. Класът превежда само реално възможните
// грешки при нашата политика за пароли (8 знака, цифра, малка буква).
public sealed class BulgarianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new()
    {
        Code = "DefaultError",
        Description = "Възникна неочаквана грешка. Опитай отново.",
    };

    public override IdentityError InvalidUserName(string? userName) => new()
    {
        Code = "InvalidUserName",
        Description = $"„{userName}“ не е валидно потребителско име.",
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = "InvalidEmail",
        Description = $"„{email}“ не е валиден имейл адрес.",
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = "DuplicateEmail",
        Description = $"Имейл адресът {email} вече се използва.",
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = "DuplicateUserName",
        Description = $"Потребителското име {userName} вече се използва.",
    };

    public override IdentityError DuplicateRoleName(string role) => new()
    {
        Code = "DuplicateRoleName",
        Description = $"Ролята {role} вече съществува.",
    };

    public override IdentityError UserAlreadyHasPassword() => new()
    {
        Code = "UserAlreadyHasPassword",
        Description = "Потребителят вече има зададена парола.",
    };

    public override IdentityError UserAlreadyInRole(string role) => new()
    {
        Code = "UserAlreadyInRole",
        Description = $"Потребителят вече е с роля {role}.",
    };

    public override IdentityError UserNotInRole(string role) => new()
    {
        Code = "UserNotInRole",
        Description = $"Потребителят не е с роля {role}.",
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = "PasswordMismatch",
        Description = "Невалидна парола.",
    };

    public override IdentityError InvalidToken() => new()
    {
        Code = "InvalidToken",
        Description = "Този токен е невалиден.",
    };

    public override IdentityError RecoveryCodeRedemptionFailed() => new()
    {
        Code = "RecoveryCodeRedemptionFailed",
        Description = "Възстановяващият код не може да бъде използван.",
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = "PasswordRequiresDigit",
        Description = "Паролата трябва да съдържа поне една цифра („0“–„9“).",
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = "PasswordRequiresLower",
        Description = "Паролата трябва да съдържа поне една малка буква („a“–„z“).",
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = "PasswordRequiresUniqueChars",
        Description = $"Паролата трябва да използва поне {uniqueChars} различни знака.",
    };

    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = "PasswordTooShort",
        Description = $"Паролата трябва да е поне {length} знака.",
    };

    public override IdentityError UserLockoutNotEnabled() => new()
    {
        Code = "UserLockoutNotEnabled",
        Description = "За този потребител не е включено заключване.",
    };
}
