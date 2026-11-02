using Microsoft.AspNetCore.Identity;

namespace CivicEyeGD.Data;

// Едно потвърждение: „виждам същия проблем на място“ от конкретен
// гражданин. Пази се като отделен ред, а не като броене в Report,
// защото приоритетът изисква само НЕЗАВИСИМИ потвърждения — брояч
// не може да покаже кой е гласувал и дали това е самият подател.
public class Confirmation
{
    public int Id { get; set; }

    // Сигналът, който се потвърждава. Уникалният индекс (ReportId, UserId),
    // дефиниран в DbContext-а, прави второто потвърждение от същия
    // гражданин невъзможно още на ниво база от данни.
    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    // Кой потвърждава. Идентичността се пази, защото при изчисляване
    // на приоритета гласът на самия подател се изключва — това прави
    // Report.IndependentConfirmationCount.
    public string UserId { get; set; } = string.Empty;
    public IdentityUser User { get; set; } = null!;

    // UTC, защото местното време скача при смяна на лятното часово
    // време и историята на потвържденията би се наредила наопаки.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}