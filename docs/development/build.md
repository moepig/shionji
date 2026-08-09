# ビルド

本ドキュメントは、開発機でソリューションをビルドしてアプリを起動するまでを扱う。テストの実行は [テスト](test.md)、配布物の生成は [リリース](release.md) を参照。

## 前提

開発機に必要なものを、以下に示す。

| 要件 | 内容 |
| --- | --- |
| OS | Windows 10 ビルド 19041 以降、x64 |
| .NET SDK | 10.0.3xx |

SDK の版は `global.json` で固定している。`rollForward` は `latestFeature` のため、10.0.1xx 以降の同一メジャーであれば動作する。

## ソリューションのビルド

全プロジェクトをまとめてビルドするコマンドは次のとおりである。

```bash
dotnet build Shionji.slnx
```

ソリューションファイルは `.slnx` 形式で、`.sln` は置いていない。

## アプリの実行

WinUI ヘッドは unpackaged (`WindowsPackageType=None`) で、MSIX を作らず実行ファイルを直接起動する。

ヘッドのプロジェクトをビルドする。

```bash
dotnet build src/Shionji.App.WinUI/Shionji.App.WinUI.csproj
```

出力された実行ファイルを起動する。

```bash
"src/Shionji.App.WinUI/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Shionji.App.WinUI.exe" --demo
```

`--demo` を付けるとデモモードで起動する。AWS への通信と `session-manager-plugin` の起動をフェイクに差し替え、ファイルにも書き込まない。UI の全経路をアカウント無しで確認できる。

デモの設定名は再現する挙動を兼ねている。設定名と挙動の対応を、以下にまとめる。

| 設定名 | 再現する挙動 |
| --- | --- |
| `api-db` | 起動時自動接続。EC2 踏み台を経由した Aurora Writer への転送 |
| `cache` | 確立の 10 秒後に疑似切断し、自動再接続する |
| `batch-ec2` | EC2 インスタンスへの直接セッション |
| `broken-ambiguous` | 転送先の複数一致エラー |
| `sso-expired` | SSO トークン切れと、そこからのアプリ内ログイン |

デモモードと通常モードは別インスタンスとして扱われるため、同時に起動できる。同一モードの二重起動は既存のウィンドウを前面に出すだけになる。

## ビルド構成上の制約

ソリューション全体に効いている構成上の制約を、以下にまとめる。

| 制約 | 内容 |
| --- | --- |
| x64 固定 | `Platform` と `RuntimeIdentifier` を `x64` / `win-x64` に固定している。`WindowsAppSDKSelfContained` が構成に依存しないアーキテクチャ指定を受け付けないため。IDE から `Any CPU` でビルドすると失敗する |
| MSIX ツールの有効化 | MSIX は作らないが `EnableMsixTooling` を有効にしている。無効にすると publish 出力から PRI が欠落し、XAML を読み込めず起動時に異常終了する |
| 警告をエラーとして扱う | `Directory.Build.props` の `TreatWarningsAsErrors`。全プロジェクトに適用される |
| Nullable 有効 | 同じく全プロジェクトに適用される |

## ビルド失敗時の対処

出力先のファイルがロックされていると `MSB3027` / `MSB3021` で失敗する。実行中のアプリ、Visual Studio のデバッグセッション、デモモードのプロセスが原因になる。プロセスを終了してからビルドし直す。

```bash
powershell -Command "Get-Process Shionji.App.WinUI -ErrorAction SilentlyContinue | Stop-Process -Force"
```
