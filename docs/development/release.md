# リリース

本ドキュメントは、配布物の形態とバージョンを確定させて GitHub Release へ公開するまでを扱う。ビルドの前提は [ビルド](build.md)、テストの実行は [テスト](test.md) を参照。

## 配布形態

インストーラーは作らない。実行に必要なファイル一式を含むフォルダを圧縮して配布する。展開先は任意のフォルダでよく、レジストリと共有フォルダには何も書かない。

同梱するもの、しないものを次に示す。

| 要素 | 扱い |
| --- | --- |
| Windows App SDK | 同梱する (`WindowsAppSDKSelfContained`) |
| .NET ランタイム | 同梱しない。実行環境に .NET Desktop Runtime 10.0 が必要である |
| `session-manager-plugin` | 同梱しない。実行環境で別途導入する |

.NET ランタイムを同梱する場合は publish に `--self-contained` を加える。配布物は 3 割ほど大きくなる。

## リリースワークフロー

[リリース制御](../../.github/workflows/release-control.yml)が release-please のリリース PR を管理する。マージ後に [配布処理](../../.github/workflows/distribute.yml)を呼び、指定されたマージ SHA を Windows 上でテスト・publish する。

version.txt、WinUI の csproj の Version、タグを照合する。不一致なら配布を停止する。ZIP と checksums.txt は既存 draft Release に添付し、ファイルと SHA-256 の検証後に公開する。本文の変更履歴は release-please の生成内容を維持する。

## 手順

1. デモモードで UI の主要経路を確認する。[ビルド](build.md#アプリの実行)の手順を使う。
2. 実 AWS で [スモークテスト](test.md#実-aws-でのスモークテスト)を行う。
3. リリース PR のバージョンと変更履歴を確認する。GITHUB_TOKEN が作成・更新した PR の CI が承認待ちなら、管理者が開始を承認する。
4. CI のテストと配布用ビルド、必須チェックの成功を確認して PR をマージする。
5. GitHub Release に ZIP と checksums.txt が添付され、draft が解除されたことを確認する。
6. ZIP をリポジトリ外へ展開し、Shionji.exe が起動することを確認する。同梱漏れを周囲のビルド出力で補わないよう、別のフォルダで確認する。

保留と復旧は [依存更新とリリース](../dependency-release.md)の運用手順に従う。

## コード署名

実行ファイルのコード署名は行わない。実行環境によっては SmartScreen の警告を表示する。
