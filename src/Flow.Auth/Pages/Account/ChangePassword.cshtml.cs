using System.ComponentModel.DataAnnotations;
using Flow.Auth.Data;
using Flow.Auth.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flow.Auth.Pages.Account;

/// <summary>
/// Обязательная смена пароля: сюда ведут /account/login и /connect/authorize, пока стоит
/// <see cref="ApplicationUser.MustChangePassword"/>. Требует cookie входа (иначе — на /account/login).
/// После смены — обратно на ReturnUrl (обычно /connect/authorize), и клиент получает токен.
/// Вместо смены можно «Оставить как есть» (<see cref="OnPostKeepAsync"/>): сначала предупреждение, что риск на человеке,
/// и только второе, явное подтверждение снимает флаг и запоминает согласие в <see cref="ApplicationUser.PasswordRiskAcceptedAt"/>.
/// </summary>
[Authorize(Policy = AuthConstants.CookiePolicy)]
public sealed class ChangePasswordModel(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public bool Forced { get; private set; }

    /// <summary>Нажато «Оставить как есть» — поверх формы показывается предупреждение с подтверждением.</summary>
    public bool ConfirmKeep { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        Forced = user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        Forced = user.MustChangePassword;

        if (!ModelState.IsValid)
        {
            Error = "Заполните все поля.";
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            Error = "Пароли не совпадают.";
            return Page();
        }

        if (Input.NewPassword == Input.CurrentPassword)
        {
            Error = "Новый пароль должен отличаться от текущего.";
            return Page();
        }

        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            Error = Translate(result);
            return Page();
        }

        user.MustChangePassword = false;
        user.PasswordRiskAcceptedAt = null;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);

        return LocalRedirect(SafeReturnUrl);
    }

    /// <summary>
    /// «Оставить как есть». Без <paramref name="confirm"/> — только показать предупреждение (флаг не трогаем:
    /// случайный клик не должен снимать защиту). С подтверждением — снять флаг, записать время согласия и продолжить вход.
    /// Поля формы здесь не нужны, поэтому ModelState (Required у Input) не проверяется. Текущий пароль заново не спрашиваем:
    /// человек только что ввёл его на странице входа, а cookie уже выдана.
    /// </summary>
    public async Task<IActionResult> OnPostKeepAsync(bool confirm)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        // Своей волей зашёл сменить пароль — оставлять нечего, просто уходим.
        if (!user.MustChangePassword)
            return LocalRedirect(SafeReturnUrl);

        Forced = true;
        if (!confirm)
        {
            ModelState.Clear();
            ConfirmKeep = true;
            return Page();
        }

        user.MustChangePassword = false;
        user.PasswordRiskAcceptedAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);

        logger.LogWarning("User {Username} ({Id}) kept the password assigned to them and accepted the risk", user.UserName, user.Id);
        return LocalRedirect(SafeReturnUrl);
    }

    private string SafeReturnUrl => string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl) ? "/" : ReturnUrl;

    /// <summary>Коды IdentityErrorDescriber → русский текст; неизвестные — как есть.</summary>
    private static string Translate(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordMismatch) => "Неверный текущий пароль.",
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Новый пароль должен быть не короче 8 символов.",
        _ => e.Description
    }));
}
