# excerptStringPutback

正規表現でテキストから原文を書き出し、編集した変更後文字列を同じ位置へ書き戻すツールです。受け渡しは JSON のカタログです。正規表現の構文、符号化、バイト位置、対応づけは [docs/specification.md](docs/specification.md) を正とします。フォルダ構成は .NET SDK の構成です。

## 必要環境

Windows x64 の exe は `bat\win-x64.bat` で作り、`bin\win-x64\excerptStringPutback.exe` に出力します。Linux x64 の実行ファイルは `bat/linux-x64.sh` で作り、`bin/linux-x64/excerptStringPutback` に出力します。どちらの実行ファイルも、.NET の入っていない PC で動きます。DLL は `bat\dll.bat` で作り、`bin\dll\excerptStringPutback.dll` に出力します。この DLL を読む側には .NET 8 以降が必要です。実行ファイルと DLL はリポジトリには含まれません。ビルドとテストには .NET 8 以降の SDK が必要です。8 が入っていなければ、それより新しい SDK でビルドします。

## 使い方

```bat
bin\win-x64\excerptStringPutback.exe excerpt -encoding utf-8 -pattern "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$" -o script.json script.txt
bin\win-x64\excerptStringPutback.exe putback -number 0 -o script-out.txt script.json script.txt
bin\win-x64\excerptStringPutback.exe putback -number 0 -o script.txt script.json script.txt
bin\win-x64\excerptStringPutback.exe putback -source -o script.txt script.json script.txt
bin\win-x64\excerptStringPutback.exe version
```

`excerpt` はテキストを変更せず、新しい JSON を作ります。人が編集するのは `replacementNames`、`fileVersion`、`note`、各項目の `replacements` です。`fileVersion` の書き出し直後は `"1"` です。`note` には JSON の値を自由に書けます。書き出し直後は空文字です。書き戻しでは中身を読まず、書いてあった値は JSON に残します。`putback` はカタログの `pattern` を今のテキストに掛け直し、選んだ番号の変更後文字列を原文の位置へ書き込みます。`-number` は `replacementNames` のインデックスで、0 が先頭です。0 未満はエラーです。省略したときは `used` を使います。`-source` を付けると、番号の代わりに各項目の `source` をその位置へ書きます。値は取りません。`-number` と同時には指定できません。このときは `used` を `"source"` にします。次に `-number` も `-source` も省略すると、`used` が整数ならその番号を、`"source"` なら原文を書きます。カタログの `source` はファイル側の文字列では上書きしません。`-o` と入力のテキストが同じパスのときは、そのファイルを更新します。別のパスのときは、元のテキストは変更しません。`-json-out` を省くと、入力の JSON を更新します。

終了コードは 0 が完了、1 がファイルや符号化や正規表現や JSON のエラー、2 が引数の不足や未知のコマンドです。経過と警告は標準エラーに出ます。エラー、警告、使い方などのメッセージは英語が基本です。OS の表示言語が日本語なら日本語、英語なら英語に切り替わります。それ以外の表示言語では英語のままです。`-language ja` または `-language en` でも切り替えられます。

## ビルドとテスト

```bash
dotnet test ExcerptStringPutback.sln
```

実行ファイルと DLL は、作る対象ごとのスクリプトで作ります。.NET 8 以降の SDK で動きます。中では次を実行します。

```bat
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -r win-x64 -o bin\win-x64
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -r linux-x64 -o bin\linux-x64
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -o bin\dll -p:UseAppHost=false -p:DebugType=none -p:DebugSymbols=false
```

## 構成

```text
ExcerptStringPutback.sln
src/ExcerptStringPutback/                 コンソールアプリ
tests/ExcerptStringPutback.Tests/         テスト
bat/win-x64.bat                           Windows x64 の exe を bin/win-x64 に作る
bat/linux-x64.sh                          Linux x64 の実行ファイルを bin/linux-x64 に作る
bat/dll.bat                               DLL を bin/dll に作る
docs/specification.md
```
