namespace CivicEyeGD.Services;

/// <summary>
/// Премахва метаданните от качена снимка. Телефонът записва в EXIF-а
/// точните GPS-координати, модела на устройството и часа на снимането —
/// нищо от това не трябва да стига до публичната страница на сигнала.
/// Файлът се пренарежда като структура, без да се пипат пикселите,
/// затова не е нужна външна библиотека за изображения.
///
/// Единствената стойност, която оцелява, е ориентация на кадъра. Без нея
/// портретна снимка от телефон се завърта на 90 градуса, защото пикселите
/// се съхраняват хоризонтално, а браузърът разчита на числото, за да ги
/// изправи. То не съдържа нищо лично: единайсет възможни стойности,
/// описващи единствено как се държи телефонът.
/// </summary>
public static class ImageMetadataScrubber
{
    /// <summary>
    /// Връща прочистения файл или null, когато структурата не може да се
    /// разчете докрай. Null е отказ, не „остави оригинала“: по-добре
    /// снимката да не се качи, отколкото да се качи с координатите.
    /// </summary>
    public static byte[]? Scrub(byte[] data, string contentType) => contentType switch
    {
        "image/jpeg" => ScrubJpeg(data),
        "image/png" => ScrubPng(data),
        "image/webp" => ScrubWebp(data),
        _ => null,
    };

    // JPEG е редица от сегменти: маркер FF xx, после 2 байта дължина
    // (големи байтове, включително самите те) и съдържанието.
    // EXIF-ът и XMP-то живеят в APP1..APP15, описанието — в COM.
    private static byte[]? ScrubJpeg(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        var kept = new List<byte>(data.Length) { 0xFF, 0xD8 };
        var position = 2;
        var orientation = 0;

        while (position < data.Length)
        {
            if (position + 1 >= data.Length)
            {
                return null;
            }

            // Маркерът винаги започва с FF; повторните FF са запълване.
            if (data[position] != 0xFF)
            {
                return null;
            }

            var marker = data[position + 1];

            if (marker == 0xD9)
            {
                return WithOrientation(kept, orientation);
            }

            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            // Самостоятелни маркери без дължина (RSTn и TEMP).
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                kept.Add(0xFF);
                kept.Add(marker);
                position += 2;
                continue;
            }

            if (position + 4 > data.Length)
            {
                return null;
            }

            var length = (data[position + 2] << 8) | data[position + 3];
            var segmentEnd = position + 2 + length;

            if (length < 2 || segmentEnd > data.Length)
            {
                return null;
            }

            // APP1 (EXIF), APP2..APP15 и COM се изхвърлят; APP0 (JFIF)
            // остава, защото в него няма лични данни и без него някои
            // декодери губят съотношението на пикселите.
            var isMetadata = marker == 0xFE || (marker >= 0xE1 && marker <= 0xEF);

            if (marker == 0xE1 && orientation == 0)
            {
                orientation = ReadJpegOrientation(data, position + 4, segmentEnd);
            }

            if (!isMetadata)
            {
                kept.AddRange(data[position..segmentEnd]);
            }

            position = segmentEnd;

            // След Start Of Scan идва компресираният поток, който не е
            // разбиван на сегменти с дължина. Копираме го байт по байт
            // до следващия истински маркер.
            if (marker == 0xDA)
            {
                while (position < data.Length && !IsScanMarker(data, position))
                {
                    kept.Add(data[position]);
                    position++;
                }
            }
        }

        // Без EOI файлът е недовършен — отказваме, вместо да запишем
        // половин снимка.
        return null;
    }

    // В компресирания поток FF 00 е „набит“ нула байт, а FF D0..FF D7 са
    // подновяващи маркери; останалите FF xx са началото на следващ сегмент.
    private static bool IsScanMarker(byte[] data, int position)
    {
        if (data[position] != 0xFF || position + 1 >= data.Length)
        {
            return false;
        }

        var next = data[position + 1];

        return next != 0x00
            && next != 0xFF
            && !(next >= 0xD0 && next <= 0xD7);
    }

    // Числото за ориентация се чете от TIFF-хедъра вътре в APP1, преди самия
    // APP1 да бъде изхвърлен. Нула означава „няма какво да се запази“: липсващ,
    // непознат или повреден EXIF не спира прочистването.
    private static int ReadJpegOrientation(byte[] data, int payloadStart, int payloadEnd)
    {
        const int exifHeaderLength = 6;
        const int orientationTag = 0x0112;

        if (payloadEnd - payloadStart < exifHeaderLength + 8
            || ReadType(data, payloadStart) != "Exif")
        {
            return 0;
        }

        var tiff = payloadStart + exifHeaderLength;
        var bigEndian = data[tiff] == 'M' && data[tiff + 1] == 'M';

        if (!bigEndian && (data[tiff] != 'I' || data[tiff + 1] != 'I'))
        {
            return 0;
        }

        var ifdStart = tiff + ReadTiffInt(data, tiff + 4, 4, bigEndian);

        if (ifdStart < tiff + 8 || ifdStart + 2 > payloadEnd)
        {
            return 0;
        }

        var entries = ReadTiffInt(data, ifdStart, 2, bigEndian);

        if (entries > 1000 || ifdStart + 2 + entries * 12 > payloadEnd)
        {
            return 0;
        }

        for (var entry = 0; entry < entries; entry++)
        {
            var start = ifdStart + 2 + entry * 12;

            if (ReadTiffInt(data, start, 2, bigEndian) == orientationTag)
            {
                var value = ReadTiffInt(data, start + 8, 2, bigEndian);

                return value is >= 2 and <= 8 ? value : 0;
            }
        }

        return 0;
    }

    // Сегментът, който замества изхвърления EXIF: TIFF с едно-единствено
    // число. Подравнен е след APP0, защото повечето декодери очакват
    // JFIF-описанието да е първото.
    private static byte[] WithOrientation(List<byte> kept, int orientation)
    {
        kept.Add(0xFF);
        kept.Add(0xD9);

        if (orientation == 0)
        {
            return kept.ToArray();
        }

        var insertAt = 2;

        if (kept.Count >= 6 && kept[2] == 0xFF && kept[3] == 0xE0)
        {
            var app0Length = (kept[4] << 8) | kept[5];
            insertAt = 4 + app0Length;
        }

        var segment = new List<byte>
        {
            0xFF, 0xE1, 0x00, 0x22,
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xFF), 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        kept.InsertRange(insertAt, segment);

        return kept.ToArray();
    }

    private static int ReadTiffInt(byte[] data, int offset, int length, bool bigEndian)
    {
        if (offset < 0 || offset + length > data.Length)
        {
            return 0;
        }

        var value = 0;

        for (var i = 0; i < length; i++)
        {
            var shift = bigEndian ? length - 1 - i : i;
            value |= data[offset + i] << (shift * 8);
        }

        return value;
    }

    // PNG: подпис + верига от блокове [дължина][тип][данни][CRC].
    private static byte[]? ScrubPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(signature))
        {
            return null;
        }

        var kept = new List<byte>(data.Length);
        kept.AddRange(signature);

        var position = 8;
        var sawEnd = false;

        while (position + 12 <= data.Length)
        {
            var length = ReadBigEndianInt32(data, position);
            var type = ReadType(data, position + 4);

            if (length < 0 || position + 12 + (long)length > data.Length)
            {
                return null;
            }

            var blockEnd = position + 12 + length;

            // eXIf е стандартният блок за EXIF; текстовите блокове също
            // могат да съдържат местоположение, затова са навън.
            var isMetadata = type is "eXIf" or "tEXt" or "zTXt" or "iTXt";

            if (!isMetadata)
            {
                kept.AddRange(data[position..blockEnd]);
            }

            position = blockEnd;

            if (type == "IEND")
            {
                sawEnd = true;
                break;
            }
        }

        return sawEnd ? kept.ToArray() : null;
    }

    // WEBP: RIFF <дължина> "WEBP" после блокове [тип][дължина][данни],
    // подравнени към четен брой байтове.
    private static byte[]? ScrubWebp(byte[] data)
    {
        if (data.Length < 12
            || ReadType(data, 0) != "RIFF"
            || ReadType(data, 8) != "WEBP")
        {
            return null;
        }

        var declaredEnd = 8 + ReadLittleEndianInt32(data, 4);
        var end = Math.Min(declaredEnd, data.Length);

        if (declaredEnd < 12 || end < 12)
        {
            return null;
        }

        var body = new List<byte>();
        var position = 12;

        while (position + 8 <= end)
        {
            var type = ReadType(data, position);
            var length = ReadLittleEndianInt32(data, position + 4);
            var blockEnd = position + 8 + length + (length % 2);

            if (length < 0 || blockEnd > end)
            {
                return null;
            }

            // EXIF и XMP са единствените блокове с лични данни; кадровите
            // блокове (VP8, VP8L, VP8C) и цветовият профил остават.
            if (type is not ("EXIF" or "XMP "))
            {
                body.AddRange(data[position..blockEnd]);
            }

            position = blockEnd;
        }

        if (position != end)
        {
            return null;
        }

        var result = new List<byte>(body.Count + 12);
        result.AddRange([0x52, 0x49, 0x46, 0x46]);
        WriteLittleEndianInt32(result, body.Count + 4);
        result.AddRange([0x57, 0x45, 0x42, 0x50]);
        result.AddRange(body);

        return result.ToArray();
    }

    private static string ReadType(byte[] data, int offset) =>
        data.Length < offset + 4
            ? string.Empty
            : System.Text.Encoding.ASCII.GetString(data, offset, 4);

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static int ReadLittleEndianInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static void WriteLittleEndianInt32(List<byte> target, int value)
    {
        target.Add((byte)(value & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
        target.Add((byte)((value >> 16) & 0xFF));
        target.Add((byte)((value >> 24) & 0xFF));
    }
}
