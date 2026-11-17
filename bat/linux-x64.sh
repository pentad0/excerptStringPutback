#!/bin/sh
set -e
cd "$(dirname "$0")/.."
dotnet publish src/ExcerptStringPutback/ExcerptStringPutback.csproj -c Release -r linux-x64 -o bin/linux-x64
echo built bin/linux-x64/excerptStringPutback
