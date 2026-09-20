namespace Flow.Application.Features.Attachments;

/// <summary>
/// Размеры картинки из заголовка файла. Нужны ради двух чисел в разметке: получив width и height,
/// браузер резервирует место под картинку по соотношению сторон ещё до загрузки. Без них
/// loading="lazy" почти не работает — незагруженная картинка занимает нулевую высоту, вся лента
/// попадает в область видимости на первом проходе и запрашивается скопом, а при доезде прыгает вёрстка.
///
/// Разбор свой, а не ImageSharp/SkiaSharp: декодер тянет в ядро графическую библиотеку с нативными
/// зависимостями и собственным потоком CVE — дорогая цена за два целых числа из первых байт файла.
/// Здесь не декодируется ни один пиксель, читается только заголовок. Поэтому функции чистые и
/// проверяются юнит-тестами, как и соседний FileTypes.
///
/// Исключений наружу нет по построению: каждое обращение к head закрыто проверкой длины, срезов с
/// вычисляемой длиной нет, BinaryPrimitives не используется намеренно (он бросает на коротком span).
/// Общего catch тоже нет: он превратил бы ошибку разбора в тихо пропавшие размеры — ровно тот баг,
/// который никто не заметит. Пусть лучше падает юнит-тест на обрезанном файле.
///
/// Типы сверяются точным сравнением строк, как в FileTypes.MatchesSignature: сюда приходит тип,
/// который уже определил сервер, а не то, что прислал браузер.
/// </summary>
public static class ImageDimensions
{
    /// <summary>
    /// Сколько байт начала файла имеет смысл передавать в TryRead.
    ///
    /// PNG, GIF и WEBP укладываются в 32 байта — у них размеры лежат на фиксированных смещениях.
    /// Весь остальной запас нужен JPEG: до SOF идут APPn, и APP1 с EXIF бывает почти на 64 КБ,
    /// потому что внутри лежит миниатюра; за ним ещё XMP и ICC-профиль.
    ///
    /// 64 КиБ — компромисс: покрывают подавляющее большинство снимков с телефона, а 128-256 КиБ
    /// добирают уже редкие случаи с большими ICC-профилями. Буфер такого размера стоит арендовать
    /// (ArrayPool), а не выделять на каждую загрузку; если всё же выделять — 65 536 байт ещё не
    /// уходят в LOH, порог которого 85 000.
    ///
    /// Если SOF в буфер не попал — размеров нет, и это штатный исход, см. комментарий к TryRead.
    /// </summary>
    public const int HeadLength = 64 * 1024;

    /// <summary>
    /// Потолок правдоподобия. Нужен только PNG: там размер занимает 4 байта и в битом заголовке
    /// разворачивается в миллиарды или в отрицательное число. У остальных форматов разрядность поля
    /// сама по себе не даёт выйти за разумное.
    /// </summary>
    private const int MaxDimension = 100_000;

    /// <summary>
    /// Потолок по площади. Без него 29-байтный PNG с валидной сигнатурой легально объявляет
    /// 100 000 x 100 000 — десять миллиардов пикселей, и оба значения по отдельности проходят
    /// MaxDimension. Отсекать надо здесь, а не в домене: сюда приходит ввод, который полностью
    /// контролирует загружающий, а домен обязан остаться недостижимым assert'ом.
    /// Сто мегапикселей с запасом перекрывают любую реальную камеру.
    /// </summary>
    private const long MaxPixels = 100_000_000;

    /// <summary>
    /// Размеры из заголовка. false — размеров нет: незнакомый тип, обрезанный буфер, битый заголовок
    /// или (для JPEG) не поместившийся в head SOF. Во всех этих случаях width и height остаются нулями.
    ///
    /// Отсутствие размеров — допустимый исход, и обрабатывать его обязаны выше по течению: у вложений,
    /// загруженных до появления этого кода, размеров нет вовсе. Поэтому поля хранятся nullable,
    /// а в разметку атрибуты пишутся только парой: одинокий width браузеру бесполезен, соотношение
    /// сторон он считает из обоих.
    /// </summary>
    public static bool TryRead(string contentType, ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        int parsedWidth = 0, parsedHeight = 0;

        var known = contentType switch
        {
            "image/png" => TryReadPng(head, out parsedWidth, out parsedHeight),
            "image/jpeg" => TryReadJpeg(head, out parsedWidth, out parsedHeight),
            "image/gif" => TryReadGif(head, out parsedWidth, out parsedHeight),
            "image/webp" => TryReadWebp(head, out parsedWidth, out parsedHeight),
            _ => false
        };

        if (!known || !Plausible(parsedWidth) || !Plausible(parsedHeight))
            return false;

        if ((long)parsedWidth * parsedHeight > MaxPixels)
            return false;

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private static bool Plausible(int value) => value is > 0 and <= MaxDimension;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// PNG: сигнатура(8), длина чанка(4), "IHDR"(4), ширина(4), высота(4) — всё big-endian.
    /// Проверяется не только метка чанка, но и его длина: по спецификации у IHDR она ровно 13,
    /// и если там другое число, то первым чанком лежит не IHDR и читать по этим смещениям нечего.
    /// </summary>
    private static bool TryReadPng(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (head.Length < 24 || !head.StartsWith(PngSignature))
            return false;

        if (BigEndian32(head, 8) != 13 || !head.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;

        width = BigEndian32(head, 16);
        height = BigEndian32(head, 20);
        return true;
    }

    /// <summary>
    /// GIF: "GIF87a"/"GIF89a"(6) и дескриптор логического экрана — ширина и высота по два байта
    /// little-endian. Берётся именно экран, а не первый кадр: кадр бывает меньше, но холст
    /// браузеру задаёт экран. У APNG и анимированных GIF это же поле описывает всю анимацию.
    /// </summary>
    private static bool TryReadGif(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (head.Length < 10 || (!head.StartsWith("GIF87a"u8) && !head.StartsWith("GIF89a"u8)))
            return false;

        width = LittleEndian16(head, 6);
        height = LittleEndian16(head, 8);
        return true;
    }

    /// <summary>
    /// WEBP — это контейнер RIFF: "RIFF"(4), размер файла(4), "WEBP"(4), дальше чанки, у каждого
    /// метка(4) и длина(4). Размеры лежат в первом чанке, но упакованы в трёх вариантах по-разному,
    /// и смещения у них тоже разные — поэтому разбирается каждый.
    ///
    /// Первый чанк начинается на 12, его тело — на 20.
    /// </summary>
    private static bool TryReadWebp(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (head.Length < 16 || !head.StartsWith("RIFF"u8) || !head.Slice(8, 4).SequenceEqual("WEBP"u8))
            return false;

        var chunk = head.Slice(12, 4);

        // VP8X (extended): флаги(1), резерв(3), «ширина-1»(3), «высота-1»(3) — по 24 бита
        // little-endian. Этот чанк идёт первым у анимации, прозрачности и файлов с ICC, и несёт
        // размер холста, то есть ровно то, что нам нужно. Единица прибавляется, потому что
        // нулевого размера в формате не бывает и поле хранит значение на единицу меньше.
        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (head.Length < 30)
                return false;

            width = LittleEndian24(head, 24) + 1;
            height = LittleEndian24(head, 27) + 1;
            return true;
        }

        // VP8L (lossless): сигнатура 0x2F(1), дальше размеры упакованы битами в четыре байта
        // little-endian — 14 бит «ширина-1», следом 14 бит «высота-1». По границам байт это не
        // ложится, поэтому число читается целиком и режется сдвигами.
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (head.Length < 25 || head[20] != 0x2F)
                return false;

            var bits = LittleEndian32(head, 21);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }

        // VP8 (lossy): тег кадра(3), стартовый код 0x9D 0x01 0x2A(3), дальше по два байта
        // little-endian на измерение. Старшие два бита каждого — масштаб отрисовки, сам размер
        // лежит в младших четырнадцати, поэтому маска обязательна.
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (head.Length < 30 || head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A)
                return false;

            width = LittleEndian16(head, 26) & 0x3FFF;
            height = LittleEndian16(head, 28) & 0x3FFF;
            return true;
        }

        return false;
    }

    /// <summary>
    /// JPEG: SOI (FF D8), дальше цепочка сегментов. Нужный — SOF, но он не первый и не на
    /// фиксированном месте: перед ним лежат APPn, DQT, DHT и прочее, и каждый надо перешагнуть
    /// по его собственной длине.
    ///
    /// Именно перешагнуть, а не искать FF C0 поиском по буферу: в APP1 с EXIF лежит миниатюра —
    /// полноценный JPEG со своими SOI и SOF, и поиск нашёл бы её размеры (обычно 160x120) вместо
    /// настоящих. Соотношение сторон при этом совпало бы, так что ошибка была бы незаметной.
    ///
    /// SOF бывает разный: C0 — базовый, C1 — расширенный, C2 — прогрессивный, дальше варианты
    /// с арифметическим кодированием и иерархические. Из диапазона C0..CF выпадают ровно три
    /// маркера, которые кадр не начинают: C4 (DHT), C8 (зарезервирован), CC (DAC).
    ///
    /// ВАЖНО: в SOF лежат размеры ДО поворота. Браузер поворачивает снимок сам по тегу Orientation
    /// из EXIF (image-orientation: from-image — значение по умолчанию), и при Orientation 5..8
    /// показанные размеры оказываются переставленными местами. Разбора EXIF здесь нет.
    /// </summary>
    private static bool TryReadJpeg(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (head.Length < 4 || head[0] != 0xFF || head[1] != 0xD8)
            return false;

        var offset = 2;

        // Ориентация из EXIF. Собирается по пути: APP1 по спецификации лежит ДО SOF, поэтому к моменту
        // встречи с SOF она уже известна — второго прохода и лишних байт заголовка не нужно.
        var orientation = 1;

        // Условие цикла держит запас на маркер(2) и длину(2). Зависнуть цикл не может: offset
        // строго растёт, а сегмент нулевой длины отвергается проверкой length < 2 ниже.
        while (offset + 4 <= head.Length)
        {
            if (head[offset] != 0xFF)
                return false;

            var marker = head[offset + 1];

            // Перед маркером разрешено сколько угодно байт-заполнителей 0xFF — это выравнивание,
            // а не сегмент, и длины за ними нет.
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            offset += 2;

            // Маркеры без тела: SOI, TEM и восемь RSTn. Длины за ними нет, сразу следующий маркер.
            // SOI здесь не для красоты: вложенный FF D8 без него читался бы как длина сегмента,
            // и разбор уходил бы за буфер — возвращая false по случайности, а не по логике.
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
                continue;

            // SOS — дальше энтропийно-кодированные данные, разбирать их мы не умеем и не собираемся.
            // По структуре файла SOF всегда раньше SOS, так что искать больше нечего. D9 — конец файла.
            if (marker is 0xDA or 0xD9)
                return false;

            // Длина big-endian и включает сами эти два байта — отсюда и минимум в 2.
            var length = (head[offset] << 8) | head[offset + 1];
            if (length < 2)
                return false;

            // APP1 с EXIF: оттуда берётся ориентация. Браузер поворачивает снимок сам
            // (image-orientation: from-image — значение по умолчанию), поэтому при повороте на 90°
            // отображаемые размеры — это размеры из SOF, поменянные местами.
            if (marker == 0xE1 && offset + length <= head.Length && length > 2)
                orientation = ReadExifOrientation(head.Slice(offset + 2, length - 2));

            if (IsStartOfFrame(marker))
            {
                // Тело SOF: длина(2), точность(1), высота(2), ширина(2). Высота идёт раньше ширины —
                // это единственный из четырёх форматов, где порядок такой.
                if (length < 7 || offset + 7 > head.Length)
                    return false;

                height = (head[offset + 3] << 8) | head[offset + 4];
                width = (head[offset + 5] << 8) | head[offset + 6];

                // 5-8 — это повороты на 90 градусов. Наружу отдаются ОТОБРАЖАЕМЫЕ размеры: хранить
                // сырые значило бы делать поправку в каждом месте, где ими пользуются, и повторять
                // её в бэкофилле. Вертикальный снимок с телефона (Orientation 6) иначе получил бы
                // разметку боком, и при нынешнем CSS остался бы сплющенным навсегда, а не на миг.
                if (orientation is >= 5 and <= 8)
                    (width, height) = (height, width);

                return true;
            }

            offset += length;
        }

        // Заголовок кончился раньше SOF. Размеров нет — штатный исход, см. HeadLength.
        return false;
    }

    /// <summary>
    /// Тег Orientation (0x0112) из APP1. Возвращает 1 на всём, что не разобралось: неизвестный
    /// порядок байт, обрезанный сегмент, отсутствующий тег. Единица — «поворота нет», то есть
    /// поведение ровно как без EXIF.
    ///
    /// Структура: "Exif\u0000\u0000"(6), дальше TIFF — порядок байт II/MM(2), контрольное 42(2),
    /// смещение IFD0(4), в IFD число записей(2) и сами записи по 12 байт. У записи: тег(2),
    /// тип(2), количество(4), значение(4). Orientation — SHORT, значение лежит в первых двух
    /// байтах поля значения, в том же порядке байт, что и весь TIFF.
    /// </summary>
    private static int ReadExifOrientation(ReadOnlySpan<byte> segment)
    {
        const int None = 1;

        if (segment.Length < 6 || !segment.StartsWith("Exif\u0000\u0000"u8))
            return None;

        var tiff = segment[6..];
        if (tiff.Length < 8)
            return None;

        bool little;
        if (tiff.StartsWith("II"u8))
            little = true;
        else if (tiff.StartsWith("MM"u8))
            little = false;
        else
            return None;

        if (Read16(tiff, 2, little) != 42)
            return None;

        var ifd = (int)Read32(tiff, 4, little);
        // Смещение отсчитывается от начала TIFF-заголовка, и до первой записи там минимум 8 байт.
        if (ifd < 8 || ifd + 2 > tiff.Length)
            return None;

        var count = Read16(tiff, ifd, little);
        for (var i = 0; i < count; i++)
        {
            var entry = ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                return None;

            if (Read16(tiff, entry, little) != 0x0112)
                continue;

            var value = Read16(tiff, entry + 8, little);
            return value is >= 1 and <= 8 ? value : None;
        }

        return None;
    }

    private static int Read16(ReadOnlySpan<byte> span, int offset, bool little) =>
        little ? span[offset] | (span[offset + 1] << 8)
               : (span[offset] << 8) | span[offset + 1];

    private static uint Read32(ReadOnlySpan<byte> span, int offset, bool little) =>
        little
            ? (uint)(span[offset] | (span[offset + 1] << 8) | (span[offset + 2] << 16) | (span[offset + 3] << 24))
            : (uint)((span[offset] << 24) | (span[offset + 1] << 16) | (span[offset + 2] << 8) | span[offset + 3]);

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    private static int BigEndian32(ReadOnlySpan<byte> head, int offset) =>
        (head[offset] << 24) | (head[offset + 1] << 16) | (head[offset + 2] << 8) | head[offset + 3];

    private static int LittleEndian16(ReadOnlySpan<byte> head, int offset) =>
        head[offset] | (head[offset + 1] << 8);

    private static int LittleEndian24(ReadOnlySpan<byte> head, int offset) =>
        head[offset] | (head[offset + 1] << 8) | (head[offset + 2] << 16);

    private static uint LittleEndian32(ReadOnlySpan<byte> head, int offset) =>
        (uint)(head[offset] | (head[offset + 1] << 8) | (head[offset + 2] << 16) | (head[offset + 3] << 24));
}
