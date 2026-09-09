using System.ComponentModel.DataAnnotations;
using Flow.Auth.Controllers;
using Flow.Auth.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flow.Auth.Pages.Account;

/// <summary>
/// Единственная страница сервиса: логин (username или email) + пароль. Сюда редиректит cookie-схема
/// из /connect/authorize; после входа — обратно на ReturnUrl (тот же authorize), дальше OpenIddict выдаёт код.
/// Что именно неверно — логин или пароль — не сообщаем.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) : PageModel
{
    private const string InvalidCredentials = "Неверный логин или пароль.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Login { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Введите логин и пароль.";
            return Page();
        }

        var login = Input.Login.Trim().ToLowerInvariant();
        var user = login.Contains('@')
            ? await users.FindByEmailAsync(login)
            : await users.FindByNameAsync(login);

        if (user is null)
        {
            Error = InvalidCredentials;
            return Page();
        }

        // lockoutOnFailure: 5 неудач → 5 минут (IdentityOptions.Lockout). Деактивированный — LockoutEnd «навсегда».
        var result = await signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            Error = AccountsController.IsDisabled(user)
                ? "Доступ закрыт. Обратитесь к основателю workspace."
                : "Слишком много попыток. Подождите 5 минут и попробуйте снова.";
            return Page();
        }

        if (!result.Succeeded)
        {
            Error = InvalidCredentials;
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: false);

        return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl) ? "/" : ReturnUrl);
    }
}
