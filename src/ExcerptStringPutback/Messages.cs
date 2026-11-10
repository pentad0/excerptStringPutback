using System.Globalization;

namespace ExcerptStringPutback;

internal static class Text
{
    private static readonly AsyncLocal<Messages?> Slot = new();

    public static Messages Current => Slot.Value ?? Messages.English;

    public static void Use(Messages messages)
    {
        Slot.Value = messages;
    }
}

internal sealed class Messages
{
    public static readonly Messages English = new()
    {
        ErrorPrefix = "Error: ",
        Help =
            """
            Usage:
              excerptStringPutback extract -encoding <encoding> -pattern <regex> -o <out.json> <text file>
              excerptStringPutback extract -encoding <encoding> -pattern-file <file> -o <out.json> <text file>
              excerptStringPutback write -number <n> -o <outfile> [-json-out <json>] [-disable <methods>] <catalog.json> <text file>
              excerptStringPutback version
              -language en|ja    Switch messages between English and Japanese. The default follows the OS language when it is English or Japanese, and is English otherwise.
            """,
        MissingArguments = "Missing arguments.",
        PatternChoice = "Specify either -pattern or -pattern-file.",
        UnknownCommand = "Unknown command.",
        UnknownArgument = "Unknown argument.",
        DuplicateArgument = "The same argument is used twice.",
        InvalidLanguage = "Language must be en or ja.",
        Extracted = "Extracted {0} entries.",
        Wrote = "Wrote {0} entries.",
        WroteWithUnmatched = "Wrote {0} entries. {1} entries were unmatched.",
        KeepOriginalText = "The original text file is left unchanged. Choose a different output path.",
        JsonPathIsText = "The JSON output path is the same as a text file.",
        NumberRange = "-number must be an integer of 1 or more.",
        UsedZero = "-number is missing and used is 0.",
        Utf32Bom = "UTF-32 BOM is not supported.",
        BomOverridesEncoding = "Warning: -encoding is {0}, but the BOM selects {1}.",
        EncodingRequired = "A file without a BOM requires -encoding.",
        OddUtf16 = "The UTF-16 byte count is odd.",
        UnpairedSurrogate = "There is an unpaired surrogate.",
        InvalidUtf8 = "The file is not valid UTF-8.",
        CannotDecode = "The text cannot be decoded.",
        ReencodeChangesLength = "Re-encoding would change the byte count.",
        InvalidEncoding = "Invalid encoding: {0}",
        DirectoryNotFound = "Folder not found: {0}",
        CannotWrite = "Cannot write the file: {0}",
        PatternFileUtf8 = "The pattern file is not valid UTF-8.",
        FileNotFound = "File not found: {0}",
        CannotRead = "Cannot read the file: {0}",
        InvalidPattern = "The regular expression is invalid.",
        Lookaround = "Lookahead, lookbehind, and backreferences are not allowed.",
        DuplicateCaptureName = "The same capture name is used twice.",
        MatchTimeout = "The regular expression match did not finish in time.",
        PatternMissing = "The pattern is missing.",
        KeyNameMissing = "Warning: A catalog key name is missing from the current pattern. The key method will not be used.",
        OverlappingSource = "Source ranges overlap.",
        CannotNormalize = "The replacement cannot be normalized to NFC.",
        UnknownMethod = "Unknown method name: {0}",
        JsonBom = "JSON must be UTF-8 without a BOM.",
        JsonUtf8 = "The JSON is not valid UTF-8.",
        JsonShape = "The JSON format is invalid.",
        FormatVersion = "formatVersion is not 1.",
        UsedInvalid = "used is invalid.",
        EntriesArray = "entries must be an array.",
        MustBeArray = "{0} must be an array.",
        MustBeStringArray = "{0} must be an array of strings.",
    };

    public static readonly Messages Japanese = new()
    {
        ErrorPrefix = "エラー: ",
        Help =
            """
            使い方:
              excerptStringPutback extract -encoding <符号化> -pattern <正規表現> -o <出力.json> <テキストファイル>
              excerptStringPutback extract -encoding <符号化> -pattern-file <ファイル> -o <出力.json> <テキストファイル>
              excerptStringPutback write -number <番号> -o <出力ファイル> [-json-out <JSON>] [-disable <方式>] <カタログ.json> <テキストファイル>
              excerptStringPutback version
              -language en|ja    メッセージを英語か日本語に切り替える。省略時は OS の表示言語。日本語と英語以外は英語
            """,
        MissingArguments = "引数が足りません。",
        PatternChoice = "-pattern と -pattern-file はどちらか一方を指定します。",
        UnknownCommand = "未知のコマンドです。",
        UnknownArgument = "未知の引数です。",
        DuplicateArgument = "同じ引数が2回あります。",
        InvalidLanguage = "言語は en か ja です。",
        Extracted = "{0} 件を書き出しました。",
        Wrote = "{0} 件を書き戻しました。",
        WroteWithUnmatched = "{0} 件を書き戻しました。対応しなかった項目は {1} 件です。",
        KeepOriginalText = "元のテキストファイルは変更しません。別の出力先を指定してください。",
        JsonPathIsText = "JSON の出力先がテキストファイルと同じです。",
        NumberRange = "-number は 1 以上の整数です。",
        UsedZero = "-number が無く、used が 0 です。",
        Utf32Bom = "UTF-32 の BOM は扱えません。",
        BomOverridesEncoding = "警告: -encoding は {0} ですが、BOM を優先して {1} として読みます。",
        EncodingRequired = "BOM が無いファイルには -encoding が必要です。",
        OddUtf16 = "UTF-16 のバイト数が奇数です。",
        UnpairedSurrogate = "対になっていないサロゲートがあります。",
        InvalidUtf8 = "UTF-8 として読めません。",
        CannotDecode = "テキストを復号できません。",
        ReencodeChangesLength = "符号化し直すとバイト数が変わります。",
        InvalidEncoding = "符号化が不正です: {0}",
        DirectoryNotFound = "フォルダが見つかりません: {0}",
        CannotWrite = "ファイルを書けません: {0}",
        PatternFileUtf8 = "パターンファイルを UTF-8 として読めません。",
        FileNotFound = "ファイルが見つかりません: {0}",
        CannotRead = "ファイルを読めません: {0}",
        InvalidPattern = "正規表現が不正です。",
        Lookaround = "先読み、後読み、後方参照は使えません。",
        DuplicateCaptureName = "同じ名前のキャプチャが2つあります。",
        MatchTimeout = "正規表現の照合が時間内に終わりませんでした。",
        PatternMissing = "パターンがありません。",
        KeyNameMissing = "警告: カタログの keys に、今のパターンに無い名前があります。key 方式は使いません。",
        OverlappingSource = "原文の範囲が重なっています。",
        CannotNormalize = "変更後文字列を NFC にできません。",
        UnknownMethod = "未知の方式名です: {0}",
        JsonBom = "JSON は BOM なしの UTF-8 です。",
        JsonUtf8 = "JSON を UTF-8 として読めません。",
        JsonShape = "JSON の形式が違います。",
        FormatVersion = "formatVersion が 1 ではありません。",
        UsedInvalid = "used が不正です。",
        EntriesArray = "entries は配列です。",
        MustBeArray = "{0} は配列です。",
        MustBeStringArray = "{0} は文字列の配列です。",
    };

    public required string ErrorPrefix { get; init; }

    public required string Help { get; init; }

    public required string MissingArguments { get; init; }

    public required string PatternChoice { get; init; }

    public required string UnknownCommand { get; init; }

    public required string UnknownArgument { get; init; }

    public required string DuplicateArgument { get; init; }

    public required string InvalidLanguage { get; init; }

    public required string Extracted { get; init; }

    public required string Wrote { get; init; }

    public required string WroteWithUnmatched { get; init; }

    public required string KeepOriginalText { get; init; }

    public required string JsonPathIsText { get; init; }

    public required string NumberRange { get; init; }

    public required string UsedZero { get; init; }

    public required string Utf32Bom { get; init; }

    public required string BomOverridesEncoding { get; init; }

    public required string EncodingRequired { get; init; }

    public required string OddUtf16 { get; init; }

    public required string UnpairedSurrogate { get; init; }

    public required string InvalidUtf8 { get; init; }

    public required string CannotDecode { get; init; }

    public required string ReencodeChangesLength { get; init; }

    public required string InvalidEncoding { get; init; }

    public required string DirectoryNotFound { get; init; }

    public required string CannotWrite { get; init; }

    public required string PatternFileUtf8 { get; init; }

    public required string FileNotFound { get; init; }

    public required string CannotRead { get; init; }

    public required string InvalidPattern { get; init; }

    public required string Lookaround { get; init; }

    public required string DuplicateCaptureName { get; init; }

    public required string MatchTimeout { get; init; }

    public required string PatternMissing { get; init; }

    public required string KeyNameMissing { get; init; }

    public required string OverlappingSource { get; init; }

    public required string CannotNormalize { get; init; }

    public required string UnknownMethod { get; init; }

    public required string JsonBom { get; init; }

    public required string JsonUtf8 { get; init; }

    public required string JsonShape { get; init; }

    public required string FormatVersion { get; init; }

    public required string UsedInvalid { get; init; }

    public required string EntriesArray { get; init; }

    public required string MustBeArray { get; init; }

    public required string MustBeStringArray { get; init; }

    public static Messages ForCulture(CultureInfo culture)
    {
        return culture.TwoLetterISOLanguageName == "ja" ? Japanese : English;
    }

    public static Messages ForOption(string option)
    {
        if (IsLanguage(option, "en"))
        {
            return English;
        }

        if (IsLanguage(option, "ja"))
        {
            return Japanese;
        }

        throw new UsageException(Text.Current.InvalidLanguage);
    }

    private static bool IsLanguage(string option, string language)
    {
        return option.Equals(language, StringComparison.OrdinalIgnoreCase)
            || option.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase);
    }

    public string Format(string template, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, template, args);
    }
}
