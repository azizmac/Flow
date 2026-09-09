using System.Security.Cryptography;

namespace Flow.Client.Services;

/// <summary>Начальный пароль для нового человека: 14 символов без похожих глифов (0/O, 1/l/I), чтобы читался с экрана.</summary>
public static class Passwords
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int MinLength = 8;

    public static string Generate(int length = 14)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
