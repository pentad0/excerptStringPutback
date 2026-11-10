# excerptStringPutback

正規表現に一致したテキストを TSV へ書き出し、編集した内容を同じ正規表現の位置へ書き戻すツールです。手順は excerptTextPutback に揃えています。フォルダ構成は Go 版の `cmd` / `internal` ではなく、.NET SDK の構成です。

動作の詳細は [docs/specification.md](docs/specification.md) を正とします。

## 必要環境

Windows x64 で実行するだけなら、同梱の exe だけで足ります。.NET のインストールは不要です。テストや exe の作り直しには .NET 8 SDK が必要です。

## 使い方

Windows ではリポジトリ直下の bat を使います。中では `exe\win-x64\excerptStringPutback.exe` を起動します。

```bat
excerpt.bat "名前:\S+" input.txt excerpt.tsv
putback.bat "名前:\S+" input.txt excerpt.tsv output.txt
```

exe を直接呼ぶ場合:

```bat
exe\win-x64\excerptStringPutback.exe excerpt "名前:\S+" input.txt excerpt.tsv
exe\win-x64\excerptStringPutback.exe putback "名前:\S+" input.txt excerpt.tsv output.txt
```

`excerpt` は入力を変更しません。抜粋ファイルでは `edited` だけを編集し、`id`、`start`、`length`、`raw` は残します。`putback` は同じ正規表現で再検索し、一致したテキストが `raw` と順に同じときだけ `edited` を書き込みます。出力に入力と同じパスを指定すると、そのファイルを置き換えます。

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
excerpt.bat
putback.bat
docs/specification.md
```
