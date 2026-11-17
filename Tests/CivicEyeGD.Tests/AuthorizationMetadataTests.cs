using System.Reflection;
using CivicEyeGD.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;
using ProfilePage = CivicEyeGD.Pages.Account.ProfileModel;
using DeletePage = CivicEyeGD.Pages.Account.DeleteModel;
using ForgotPasswordPage = CivicEyeGD.Pages.Account.ForgotPasswordModel;
using LoginPage = CivicEyeGD.Pages.Account.LoginModel;
using RegisterPage = CivicEyeGD.Pages.Account.RegisterModel;
using SecurityQuestionPage = CivicEyeGD.Pages.Account.SecurityQuestionModel;

namespace CivicEyeGD.Tests;

/// <summary>
/// В Razor Pages атрибутите за вход се четат от класа на PageModel-а:
/// поставени върху OnGet или OnPost метод, те не стигат до крайната
/// точка и глобалният FallbackPolicy продължава да изисква вход.
/// Грешката се вижда едва при заявка, затова е тествана тук, а не
/// в браузъра.
/// </summary>
public sealed class AuthorizationMetadataTests
{
    // Публичните страници са точно тези, които комисията отваря без парола:
    // картата, списъците, сигнала, политиката за данните и кредитите, плюс
    // трите страници зад входа — те изрично съществуват за човек, който
    // още не е влязъл.
    // Всяка нова страница зад FallbackPolicy се хваща от теста, ако не е
    // тук и не носи атрибута.
    private static readonly Type[] PublicPages =
    [
        typeof(CreditsModel),
        typeof(DetailsModel),
        typeof(ForgotPasswordPage),
        typeof(IndexModel),
        typeof(LoginPage),
        typeof(MapModel),
        typeof(PrivacyModel),
        typeof(RegisterPage),
        typeof(ReportsModel)
    ];

    [Theory]
    [MemberData(nameof(PublicPagesData))]
    public void PublicPage_AllowsAnonymousOnItsClass(Type pageModel) =>
        Assert.NotNull(pageModel.GetCustomAttribute<AllowAnonymousAttribute>());

    [Fact]
    public void NoHandlerMethodCarriesAuthorizationAttributes()
    {
        var pageModels = typeof(DetailsModel).Assembly
            .GetTypes()
            .Where(type => typeof(PageModel).IsAssignableFrom(type) && !type.IsAbstract);

        var offenders = pageModels
            .Where(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(method => method.GetCustomAttributes(inherit: false)
                    .Any(attribute => attribute is IAuthorizeData or IAllowAnonymous)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AdminDashboard_RequiresTheAdministratorRole()
    {
        var attribute = typeof(AdminDashboard).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Administrator", attribute!.Roles);
    }

    [Theory]
    [InlineData(typeof(CreateModel))]
    [InlineData(typeof(MyReportsModel))]
    [InlineData(typeof(ProfilePage))]

    // Страницата с въпроса няма AllowAnonymous и не трябва да има: той
    // подменя сегашната парола като доказателство, а гост няма как да знае
    // парола, която още не е влязла.
    [InlineData(typeof(SecurityQuestionPage))]
    [InlineData(typeof(DeletePage))]
    public void PersonalPages_AreNotAnonymous(Type pageModel) =>
        Assert.Null(pageModel.GetCustomAttribute<AllowAnonymousAttribute>());

    public static readonly TheoryData<Type> PublicPagesData =
        new(PublicPages);
}
