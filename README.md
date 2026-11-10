# excerptStringPutback

正規表現でテキストから原文を書き出し、編集した変更後文字列を同じ位置へ書き戻すツールです。受け渡しは JSON のカタログです。正規表現の構文、符号化、バイト位置、対応づけは [docs/specification.md](docs/specification.md) を正とします。フォルダ構成は .NET SDK の構成です。

## 必要環境

Windows x64 で実行するだけなら、同梱の exe だけで足ります。.NET のインストールは不要です。テストや exe の作り直しには .NET 8 SDK が必要です。

## 使い方

Windows ではリポジトリ直下の bat を使います。中では `exe\win-x64\excerptStringPutback.exe` を起動します。

```bat
extract.bat -encoding utf-8 -pattern "(?m)^(?P<id>[^=]+)=(.*)$" -o script.json script.txt
write.bat -number 1 -o script-out.txt script.json script.txt
```

exe を直接呼ぶ場合:

```bat
exe\win-x64\excerptStringPutback.exe extract -encoding utf-8 -pattern "(?m)^(?P<id>[^=]+)=(.*)$" -o script.json script.txt
exe\win-x64\excerptStringPutback.exe write -number 1 -o script-out.txt script.json script.txt
exe\win-x64\excerptStringPutback.exe version
```

`extract` はテキストを変更せず、新しい JSON を作ります。人が編集するのは `names`、`fileVersion`、各項目の `target` です。`write` はカタログの `pattern` を今のテキストに掛け直し、選んだ番号の変更後文字列を原文の位置へ書き込みます。元のテキストは変更しません。`-json-out` を省くと、入力の JSON を更新します。

終了コードは 0 が完了、1 がファイルや符号化や正規表現や JSON のエラー、2 が引数の不足や未知のコマンドです。経過と警告は標準エラーに出ます。

## ビルドとテスト

```bash
dotnet test ExcerptStringPutback.sln
```

Windows 向けの単一 exe を作り直す場合:

```bash
dotnet publish src/ExcerptStringPutback/ExcerptStringPutback.csproj -c Release -r win-x64 -o exe/win-x64
```

## 構成

```text
ExcerptStringPutback.sln
src/ExcerptStringPutback/                 コンソールアプリ
tests/ExcerptStringPutback.Tests/         テスト
exe/win-x64/excerptStringPutback.exe      Windows x64 向け実行ファイル
extract.bat
write.bat
docs/specification.md
```
