using System.Net;
using System.Text.RegularExpressions;

namespace Flow.Auth.Tests;

/// <summary>Работа со страницей /account/login как браузер: забрать antiforgery-токен из формы и отправить POST.</summary>
public static partial class LoginPage
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string login, string password, string? returnUrl = null)
    {
        var url = "/account/login" + (returnUrl is null ? string.Empty : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        using var page = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = AntiforgeryToken().Match(html).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "Antiforgery token not found on the login page.");

        var form = new Dictionary<string, string>
        {
            ["Input.Login"] = login,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        };
        if (returnUrl is not null)
            form["ReturnUrl"] = returnUrl;

        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }
}
