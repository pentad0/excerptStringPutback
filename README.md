# excerptStringPutback

UTF-8 のテキストから二重引用符で囲まれた文字列を抜粋し、編集した内容を同じ位置へ書き戻すツールです。入力ファイルの引用符の外はそのまま残します。

抜粋して、抜粋側だけを編集し、元の断片と一致することを確認してから書き戻す手順は excerptTextPutback に揃えています。対象が自由なテキスト範囲ではなく、二重引用符付きの文字列です。フォルダ構成は Go 版の `cmd` / `internal` ではなく、.NET SDK の構成です。

動作の詳細は [docs/specification.md](docs/specification.md) を正とします。

## 必要環境

Windows x64 で実行するだけなら、同梱の exe だけで足ります。.NET のインストールは不要です。テストや exe の作り直しには .NET 8 SDK が必要です。

## 使い方

Windows ではリポジトリ直下の bat を使います。中では `exe\win-x64\excerptStringPutback.exe` を起動します。

```bat
excerpt.bat input.txt excerpt.jsonl
putback.bat input.txt excerpt.jsonl output.txt
```

exe を直接呼ぶ場合:

```bat
exe\win-x64\excerptStringPutback.exe excerpt input.txt excerpt.jsonl
exe\win-x64\excerptStringPutback.exe putback input.txt excerpt.jsonl output.txt
```

`excerpt` は入力を変更しません。抜粋ファイルの各行にある `edited` だけを編集し、`id`、`start`、`length`、`raw` は残します。`putback` は `raw` が入力の該当範囲と一致するときだけ `edited` を差し込みます。出力に入力と同じパスを指定すると、そのファイルを置き換えます。

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
