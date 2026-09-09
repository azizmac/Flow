using System.ComponentModel.DataAnnotations;
using Flow.Auth.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flow.Auth.Pages.Account;

/// <summary>
/// Обязательная смена пароля: сюда ведут /account/login и /connect/authorize, пока стоит
/// <see cref="ApplicationUser.MustChangePassword"/>. Требует cookie входа (иначе — на /account/login).
/// После смены — обратно на ReturnUrl (обычно /connect/authorize), и клиент получает токен.
/// </summary>
[Authorize]
public sealed class ChangePasswordModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public bool Forced { get; private set; }

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
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);

        return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl) ? "/" : ReturnUrl);
    }

    /// <summary>Коды IdentityErrorDescriber → русский текст; неизвестные — как есть.</summary>
    private static string Translate(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordMismatch) => "Неверный текущий пароль.",
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Новый пароль должен быть не короче 8 символов.",
        _ => e.Description
    }));
}
