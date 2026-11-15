# リリース

Windows ZIP の内容と、リリース PR をマージして公開する手順を説明する。ビルドの前提は [ビルド](build.md)、テストの実行方法は [テスト](test.md)を参照する。

## 配布形態

インストーラーは作らない。実行に必要なファイル一式を含むフォルダを圧縮して配布する。展開先は任意のフォルダでよく、レジストリと共有フォルダには何も書かない。

同梱するもの、しないものを次に示す。

| 要素 | 扱い |
| --- | --- |
| Windows App SDK | 同梱する (`WindowsAppSDKSelfContained`) |
| .NET ランタイム | 同梱しない。実行環境に .NET Desktop Runtime 10.0 が必要である |
| `session-manager-plugin` | 同梱しない。実行環境で別途導入する |

.NET ランタイムを同梱する場合は publish に `--self-contained` を加える。同梱すると配布サイズは増えるが、現在のバージョンでの増加量は未検証である。[.NET の配布方式](https://learn.microsoft.com/en-us/dotnet/core/deploying/)を参照する。

## リリースワークフロー

専用 Automation App が main の push からリリース PR を作成・更新する。人が CI、version.txt、csproj の Version、変更履歴を確認して手動マージする。マージ後の push でタグと draft を作成する。

[リリースワークフロー](../../.github/workflows/release-please.yml)の Windows ビルドは読み取り権限だけを使う。テストと publish の成功後に ZIP と checksums.txt を artifact に登録する。公開ジョブは別の runner で記録とハッシュを検査し、配布物を添付して公開する。実行ファイルを公開ジョブで実行しない。

## 手順

1. [デモモード](build.md#アプリの実行)で主要な UI 経路を確認する。
2. 実 AWS で [スモークテスト](test.md#実-aws-でのスモークテスト)を行う。
3. リリース PR のバージョン、変更履歴、必須 CI を確認して承認し、マージする。
4. ZIP、checksums.txt、release-manifest.json の添付と公開を確認する。
5. ZIP をビルド出力と別のフォルダへ展開し、Shionji.exe の起動を確認する。

停止と復旧は [依存更新とリリース](../dependency-release.md#失敗時の再実行)に従う。復旧入力は既存の release_id だけとし、配布物の不一致を上書きで回避しない。

## コード署名

実行ファイルのコード署名は行わない。実行環境によっては SmartScreen の警告を表示する。
