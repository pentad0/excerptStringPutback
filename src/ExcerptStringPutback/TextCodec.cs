using System.Text;

namespace ExcerptStringPutback;

internal sealed class DecodedText
{
    public required byte[] Bytes { get; init; }

    public required string Text { get; init; }

    public required string EncodingName { get; init; }

    public required bool Bom { get; init; }

    public required Encoding Encoding { get; init; }

    public int BomLength => Bom ? EncodingName == "utf-8" ? 3 : 2 : 0;

    public int ByteIndex(int charIndex)
    {
        return BomLength + Encoding.GetByteCount(Text.AsSpan(0, charIndex));
    }

    public int ByteLength(int charIndex, int charLength)
    {
        return Encoding.GetByteCount(Text.AsSpan(charIndex, charLength));
    }
}

internal static class TextCodec
{
    public static DecodedText Read(string path, string? requested, TextWriter stderr)
    {
        var bytes = ReadBytes(path);
        if (IsUtf32Bom(bytes))
        {
            throw new ToolException("UTF-32 の BOM は扱えません。");
        }

        string? bomName = null;
        var bomLength = 0;
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            bomName = "utf-8";
            bomLength = 3;
        }
        else if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            bomName = "utf-16le";
            bomLength = 2;
        }
        else if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            bomName = "utf-16be";
            bomLength = 2;
        }

        string encodingName;
        var bom = bomName is not null;
        if (bom)
        {
            encodingName = bomName!;
            if (!string.IsNullOrWhiteSpace(requested))
            {
                var asked = Canonical(requested);
                if (!string.Equals(asked, encodingName, StringComparison.Ordinal))
                {
                    stderr.WriteLine($"警告: -encoding は {asked} ですが、BOM を優先して {encodingName} として読みます。");
                }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                throw new ToolException("BOM が無いファイルには -encoding が必要です。");
            }

            encodingName = Canonical(requested);
        }

        var encoding = Create(encodingName);
        var bodyLength = bytes.Length - bomLength;
        if (encodingName is "utf-16le" or "utf-16be")
        {
            if (bodyLength % 2 != 0)
            {
                throw new ToolException("UTF-16 のバイト数が奇数です。");
            }

            if (HasUnpairedUtf16(bytes.AsSpan(bomLength, bodyLength), encodingName == "utf-16be"))
            {
                throw new ToolException("対になっていないサロゲートがあります。");
            }
        }

        string text;
        try
        {
            text = encoding.GetString(bytes, bomLength, bodyLength);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException(encodingName == "utf-8"
                ? "UTF-8 として読めません。"
                : "テキストを復号できません。");
        }

        if (HasUnpairedSurrogate(text))
        {
            throw new ToolException("対になっていないサロゲートがあります。");
        }

        if (encoding.GetByteCount(text) != bodyLength)
        {
            throw new ToolException("符号化し直すとバイト数が変わります。");
        }

        return new DecodedText
        {
            Bytes = bytes,
            Text = text,
            EncodingName = encodingName,
            Bom = bom,
            Encoding = encoding,
        };
    }

    public static string Canonical(string name)
    {
        var compact = name.Trim().ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return compact switch
        {
            "utf8" => "utf-8",
            "utf16le" => "utf-16le",
            "utf16be" => "utf-16be",
            _ => throw new ToolException($"符号化が不正です: {name}"),
        };
    }

    public static Encoding Create(string encodingName)
    {
        return encodingName switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
            _ => throw new ToolException($"符号化が不正です: {encodingName}"),
        };
    }

    public static void WriteBytes(string path, byte[] data)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new ToolException($"フォルダが見つかりません: {directory}");
        }

        var temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, full, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"ファイルを書けません: {path}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static string ReadPatternFile(string path)
    {
        var bytes = ReadBytes(path);
        var offset = 0;
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            offset = 3;
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException("パターンファイルを UTF-8 として読めません。");
        }

        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        if (text.EndsWith('\n') || text.EndsWith('\r'))
        {
            return text[..^1];
        }

        return text;
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            throw new ToolException($"ファイルが見つかりません: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ToolException($"ファイルが見つかりません: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"ファイルを読めません: {path}");
        }
    }

    private static bool IsUtf32Bom(byte[] bytes)
    {
        return HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00) || HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUnpairedUtf16(ReadOnlySpan<byte> body, bool bigEndian)
    {
        for (var i = 0; i < body.Length; i += 2)
        {
            var unit = bigEndian
                ? (char)((body[i] << 8) | body[i + 1])
                : (char)(body[i] | (body[i + 1] << 8));
            if (char.IsHighSurrogate(unit))
            {
                if (i + 3 >= body.Length)
                {
                    return true;
                }

                var next = bigEndian
                    ? (char)((body[i + 2] << 8) | body[i + 3])
                    : (char)(body[i + 2] | (body[i + 3] << 8));
                if (!char.IsLowSurrogate(next))
                {
                    return true;
                }

                i += 2;
            }
            else if (char.IsLowSurrogate(unit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }
}
