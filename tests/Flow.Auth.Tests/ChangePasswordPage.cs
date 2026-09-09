using System.Net;
using System.Text.RegularExpressions;

namespace Flow.Auth.Tests;

/// <summary>Страница /account/change-password как браузер: antiforgery из формы + POST. Нужна cookie входа.</summary>
public static partial class ChangePasswordPage
{
    public const string Path = "/account/change-password";

    /// <summary>LocalRedirect отдаёт относительный Location — AbsolutePath для него бросает.</summary>
    public static string PathOf(Uri location) =>
        location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];

    public static string QueryOf(Uri location) =>
        location.IsAbsoluteUri ? location.Query : (location.OriginalString.Contains('?') ? location.OriginalString[location.OriginalString.IndexOf('?')..] : string.Empty);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string current, string next, string confirm, string? returnUrl = null)
    {
        var url = Path + (returnUrl is null ? string.Empty : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        using var page = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = AntiforgeryToken().Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "Antiforgery token not found on the change-password page.");

        var form = new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = current,
            ["Input.NewPassword"] = next,
            ["Input.ConfirmPassword"] = confirm,
            ["__RequestVerificationToken"] = token
        };
        if (returnUrl is not null)
            form["ReturnUrl"] = returnUrl;

        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }
}
