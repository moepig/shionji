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

リリースは既存 Action と標準コマンドで行う。公開後は immutable releases で保護する。一部配布後の失敗は管理者が調査し、修正版を別バージョンで公開する。詳細は依存更新とリリースの手順に従う。

## 手順

1. リリース PR のバージョンと CI を確認する。
2. 人がレビューしてマージする。
3. ZIP の provenance 検証と添付・公開の成功を確認する。
4. 失敗時は [再実行の条件](../dependency-release.md#失敗時の再実行)を確認する。

## コード署名

実行ファイルのコード署名は行わない。実行環境によっては SmartScreen の警告を表示する。
