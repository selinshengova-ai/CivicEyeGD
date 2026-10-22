namespace CivicEyeGD.Services;

/// <summary>
/// Българското единствено число след число. След „1“ същественото
/// е в единствено число, а при нула и при всички останали стойности —
/// во множествено: „1 потвърждение“, но „0 потвърждения“ и „3 потвърждения“.
/// </summary>
public static class BulgarianPlural
{
    public static string Of(int count, string singular, string plural) =>
        count == 1 ? singular : plural;
}
