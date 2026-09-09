using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Инициалы и цвет аватара-заглушки. Цвет детерминирован по Id, палитра — приглушённые тона Flow
/// (как в макете Profile.dc.html: у каждого участника свой тинт), чтобы одного человека узнавать по цвету во всех списках.
/// </summary>
public static class Avatars
{
    private static readonly (string Bg, string Fg)[] Palette =
    [
        ("rgba(109, 151, 115, 0.55)", "#FFFFFF"),   // sage
        ("rgba(255, 186, 0, 0.55)", "#0C3B2E"),     // accent
        ("rgba(187, 138, 82, 0.6)", "#FFFFFF"),     // tan
        ("rgba(120, 160, 220, 0.5)", "#FFFFFF"),    // sky
        ("rgba(200, 120, 160, 0.5)", "#FFFFFF"),    // rose
        ("rgba(140, 120, 210, 0.5)", "#FFFFFF"),    // violet
        ("rgba(90, 180, 180, 0.5)", "#FFFFFF"),     // teal
        ("rgba(255, 255, 255, 0.22)", "#FFFFFF")    // neutral
    ];

    public static string Initials(UserResponse user) => Initials(user.FirstName, user.LastName);

    public static string Initials(string firstName, string lastName)
    {
        var f = firstName.Trim();
        var l = lastName.Trim();
        var s = (f.Length > 0 ? f[..1] : "") + (l.Length > 0 ? l[..1] : "");
        return s.Length > 0 ? s.ToUpperInvariant() : "?";
    }

    public static (string Bg, string Fg) Colors(Guid id)
    {
        // Первые 4 байта GUID — достаточно равномерны для выбора из 8 цветов.
        var bytes = id.ToByteArray();
        var n = (bytes[0] ^ bytes[1] ^ bytes[2] ^ bytes[3]) % Palette.Length;
        return Palette[n];
    }
}
