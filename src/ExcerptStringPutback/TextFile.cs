using System.Text;

namespace ExcerptStringPutback;

public sealed record TextFile(string Text, bool HasUtf8Bom)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TextFile Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            throw new ToolException($"ファイルが見つかりません: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ToolException($"フォルダが見つかりません: {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"ファイルを読めません: {path} ({exception.Message})");
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;
        try
        {
            var text = Utf8.GetString(bytes, offset, bytes.Length - offset);
            return new TextFile(text, hasBom);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException($"UTF-8 として読めません: {path}");
        }
    }

    public static void Write(string path, TextFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new ToolException($"出力先のフォルダが見つかりません: {path}");
        }

        var body = Utf8.GetBytes(file.Text);
        var bytes = file.HasUtf8Bom ? WithBom(body) : body;
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("n") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"ファイルを書けません: {path} ({exception.Message})");
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The original write error is more useful than a failed temp cleanup.
            }
        }
    }

    private static byte[] WithBom(byte[] body)
    {
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);
        return bytes;
    }
}
