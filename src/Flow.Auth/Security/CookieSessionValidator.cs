using Flow.Auth.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flow.Auth.Security;

/// <summary>
/// Сверка cookie-сессии с состоянием учётной записи на каждом запросе.
///
/// Зачем: <see cref="AuthAccountManager.DisableAsync"/> ставит вечный lockout и обновляет
/// SecurityStamp, но без этой проверки stamp никто не сравнивал — cookie деактивированного жила
/// до истечения своих 8 часов. С прямыми ссылками на вложения стало хуже: подзапросы картинок
/// браузер шлёт сам, а SlidingExpiration продлевает сессию на каждом из них, то есть потолок
/// в 8 часов переставал быть потолком вовсе.
///
/// Почему не штатный SecurityStampValidator: он на расхождении зовёт SignInManager.SignOutAsync(),
/// а тот гасит три схемы — Application, External и TwoFactorUserId. В Flow нет ни внешних входов,
/// ни второго фактора, зарегистрирована одна схема, и стандартный валидатор упал бы на
/// «No sign-out authentication handler is registered».
///
/// Интервала намеренно нет: проверка — это выборка по первичному ключу, а немедленный отзыв доступа
/// дороже экономии на ней. Оговорка: для уже открытого circuit'а Blazor проверка проходит один раз,
/// на рукопожатии WebSocket — принудительно разорвать живое соединение она не может.
/// </summary>
internal static class CookieSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var stampClaim = services.GetRequiredService<IOptions<IdentityOptions>>()
            .Value.ClaimsIdentity.SecurityStampClaimType;

        var id = context.Principal?.FindFirst(users.Options.ClaimsIdentity.UserIdClaimType)?.Value;
        var user = id is null ? null : await users.FindByIdAsync(id);

        var valid = user is not null
                    && !await users.IsLockedOutAsync(user)
                    && context.Principal?.FindFirst(stampClaim)?.Value == await users.GetSecurityStampAsync(user);

        if (valid)
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
