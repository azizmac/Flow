using System.Text;

namespace Flow.Domain.Ranking;

/// <summary>
/// Дробный индекс для ручного порядка задач (docs/TZ_task_model.md §7): ключ-строка, между любыми двумя ключами
/// всегда найдётся третий, поэтому перестановка одной задачи меняет одну строку, а не весь проект.
///
/// Порт алгоритма Дэвида Гринспена («Implementing Fractional Indexing»): ключ = целая часть переменной длины
/// (первый символ задаёт её длину: 'a' — 2 символа, 'b' — 3 … 'z' — 27; 'Z' — 2, 'Y' — 3 … 'A' — 27, отрицательные)
/// плюс дробная часть без хвостового нуля. Целая часть делает добавление в конец дешёвым (ключ растёт как
/// логарифм), дробная — вставку между соседями. Цифры base-62 в порядке ASCII, сравнивать ключи — только
/// ординально (в Postgres — колонка с <c>COLLATE "C"</c>).
/// </summary>
public static class FractionalIndex
{
    public const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Ключ пустого списка.</summary>
    public const string First = "a0";

    /// <summary>Длина ключа, после которой проект стоит перенумеровать (повторные вставки в одну щель).</summary>
    public const int RebalanceLength = 48;

    private static readonly string SmallestInteger = "A" + new string(Digits[0], 26);

    /// <summary>
    /// Ключ строго между <paramref name="a"/> и <paramref name="b"/>; null — край списка. Нарушенный порядок
    /// или невалидный ключ — <see cref="ArgumentException"/>.
    /// </summary>
    public static string Between(string? a, string? b)
    {
        if (a is not null) Validate(a);
        if (b is not null) Validate(b);
        if (a is not null && b is not null && string.CompareOrdinal(a, b) >= 0)
            throw new ArgumentException($"Rank '{a}' must be less than '{b}'.", nameof(a));

        if (a is null)
        {
            if (b is null)
                return First;

            var ib = IntegerPart(b);
            var fb = b[ib.Length..];
            if (ib == SmallestInteger)
                return ib + Midpoint("", fb);
            if (string.CompareOrdinal(ib, b) < 0)
                return ib;

            return DecrementInteger(ib) ?? throw new InvalidOperationException("Cannot generate a rank before the smallest key.");
        }

        if (b is null)
        {
            var ia = IntegerPart(a);
            var fa = a[ia.Length..];
            return IncrementInteger(ia) ?? ia + Midpoint(fa, null);
        }

        var intA = IntegerPart(a);
        var fracA = a[intA.Length..];
        var intB = IntegerPart(b);
        var fracB = b[intB.Length..];
        if (intA == intB)
            return intA + Midpoint(fracA, fracB);

        var next = IncrementInteger(intA) ?? throw new InvalidOperationException("Cannot increment the largest integer part.");
        return string.CompareOrdinal(next, b) < 0 ? next : intA + Midpoint(fracA, null);
    }

    /// <summary>
    /// <paramref name="count"/> ключей подряд после <paramref name="a"/> (null — с начала): для перенумерации
    /// и начальных рангов. Добавление в конец не удлиняет ключ быстрее логарифма.
    /// </summary>
    public static IReadOnlyList<string> Sequence(string? a, int count)
    {
        var keys = new List<string>(count);
        var previous = a;
        for (var i = 0; i < count; i++)
        {
            previous = Between(previous, null);
            keys.Add(previous);
        }

        return keys;
    }

    public static bool IsValid(string key)
    {
        try
        {
            Validate(key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static void Validate(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Rank must not be empty.", nameof(key));
        if (key == SmallestInteger)
            throw new ArgumentException($"Rank '{key}' is reserved.", nameof(key));
        if (key.Any(c => Digits.IndexOf(c) < 0))
            throw new ArgumentException($"Rank '{key}' contains characters outside base-62.", nameof(key));

        var integer = IntegerPart(key);
        if (key.Length > integer.Length && key[^1] == Digits[0])
            throw new ArgumentException($"Rank '{key}' must not end with '0' in its fractional part.", nameof(key));
    }

    private static int IntegerLength(char head) => head switch
    {
        >= 'a' and <= 'z' => head - 'a' + 2,
        >= 'A' and <= 'Z' => 'Z' - head + 2,
        _ => throw new ArgumentException($"Invalid rank head '{head}'.", nameof(head))
    };

    private static string IntegerPart(string key)
    {
        var length = IntegerLength(key[0]);
        if (length > key.Length)
            throw new ArgumentException($"Rank '{key}' is shorter than its integer part.", nameof(key));

        return key[..length];
    }

    /// <summary>Ключ между дробными частями a и b (b = null — «до конца»); ни одна не оканчивается на '0'.</summary>
    private static string Midpoint(string a, string? b)
    {
        if (b is not null)
        {
            // Общий префикс (a дополняется нулями) переносится как есть.
            var n = 0;
            while (n < b.Length && (n < a.Length ? a[n] : Digits[0]) == b[n])
                n++;

            if (n > 0)
                return b[..n] + Midpoint(n < a.Length ? a[n..] : "", b[n..]);
        }

        var digitA = a.Length > 0 ? Digits.IndexOf(a[0]) : 0;
        var digitB = b is not null ? Digits.IndexOf(b[0]) : Digits.Length;
        if (digitB - digitA > 1)
            return Digits[(int)Math.Round(0.5 * (digitA + digitB), MidpointRounding.AwayFromZero)].ToString();

        // Соседние цифры: либо обрезаем b (если он длиннее одной цифры), либо спускаемся на разряд ниже.
        if (b is { Length: > 1 })
            return b[..1];

        return Digits[digitA] + Midpoint(a.Length > 1 ? a[1..] : "", null);
    }

    private static string? IncrementInteger(string x)
    {
        var head = x[0];
        var digits = new StringBuilder(x[1..]);
        var carry = true;
        for (var i = digits.Length - 1; carry && i >= 0; i--)
        {
            var d = Digits.IndexOf(digits[i]) + 1;
            if (d == Digits.Length)
            {
                digits[i] = Digits[0];
            }
            else
            {
                digits[i] = Digits[d];
                carry = false;
            }
        }

        if (!carry)
            return head + digits.ToString();

        if (head == 'Z')
            return "a" + Digits[0];
        if (head == 'z')
            return null;

        var nextHead = (char)(head + 1);
        if (nextHead > 'a')
            digits.Append(Digits[0]);
        else
            digits.Length--;

        return nextHead + digits.ToString();
    }

    private static string? DecrementInteger(string x)
    {
        var head = x[0];
        var digits = new StringBuilder(x[1..]);
        var borrow = true;
        for (var i = digits.Length - 1; borrow && i >= 0; i--)
        {
            var d = Digits.IndexOf(digits[i]) - 1;
            if (d == -1)
            {
                digits[i] = Digits[^1];
            }
            else
            {
                digits[i] = Digits[d];
                borrow = false;
            }
        }

        if (!borrow)
            return head + digits.ToString();

        if (head == 'a')
            return "Z" + Digits[^1];
        if (head == 'A')
            return null;

        var previousHead = (char)(head - 1);
        if (previousHead < 'Z')
            digits.Append(Digits[^1]);
        else
            digits.Length--;

        return previousHead + digits.ToString();
    }
}
