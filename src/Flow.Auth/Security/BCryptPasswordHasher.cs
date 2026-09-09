using Flow.Auth.Data;
using Flow.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Flow.Auth.Security;

/// <summary>
/// BCrypt вместо PBKDF2 Identity. Enhanced* — SHA-384 перед BCrypt, снимает лимит 72 байта на пароль.
/// Если work factor в хеше ниже текущего, возвращает SuccessRehashNeeded — Identity перехеширует при входе.
/// Регистрируется после AddIdentityCore, чтобы перекрыть PasswordHasher по умолчанию.
/// </summary>
public sealed class BCryptPasswordHasher(IOptions<AuthOptions> options) : IPasswordHasher<ApplicationUser>
{
    private int WorkFactor => options.Value.BCrypt.WorkFactor;

    public string HashPassword(ApplicationUser user, string password)
        => BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (!BCrypt.Net.BCrypt.EnhancedVerify(providedPassword, hashedPassword))
            return PasswordVerificationResult.Failed;

        return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, WorkFactor)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }
}
