using System.Reflection;
using CivicEyeGD.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;

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
    // картата, списъците, сигнала, политиката за данните и кредитите.
    // Всяка нова страница зад FallbackPolicy се хваща от теста, ако не е
    // тук и не носи атрибута.
    private static readonly Type[] PublicPages =
    [
        typeof(CreditsModel),
        typeof(DetailsModel),
        typeof(IndexModel),
        typeof(MapModel),
        typeof(PrivacyModel),
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
    public void PersonalPages_AreNotAnonymous(Type pageModel) =>
        Assert.Null(pageModel.GetCustomAttribute<AllowAnonymousAttribute>());

    public static readonly TheoryData<Type> PublicPagesData =
        new(PublicPages);
}
