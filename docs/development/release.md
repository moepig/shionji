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

[Release Please](../../.github/workflows/release-please.yml)は main への push と手動実行で起動する。未公開の fix・feat などを検出するとリリース PR を作成・更新する。定期実行は行わない。GITHUB_TOKEN による依存 PR の自動マージ後は、main を選んで手動実行する。PR 作成時は tag を空欄にする。

リリース PR を main へマージすると、その push で自動起動し、タグと draft Release を作成して[配布処理](../../.github/workflows/distribute.yml)を呼ぶ。Windows 上でタグとマージ SHA、version.txt と csproj の Version を照合する。不一致なら停止する。

テストと publish の成功後、ZIP を添付する。アップロードが成功すると draft を解除する。Release の変更履歴は release-please の生成内容を維持する。PR 作成と公開を分けた流れは [依存更新とリリース](../dependency-release.md#公開)の図を参照する。

## 手順

1. デモモードで UI の主要経路を確認する。[ビルド](build.md#アプリの実行)の手順を使う。
2. 実 AWS で [スモークテスト](test.md#実-aws-でのスモークテスト)を行う。
3. リリース PR のバージョンと変更履歴を確認する。GITHUB_TOKEN が作成・更新した PR の CI が承認待ちなら、管理者が開始を承認する。
4. CI のテストと配布用ビルド、必須チェックの成功を確認して PR をマージする。
5. GitHub Release に ZIP が添付され、draft が解除されたことを確認する。
6. ZIP をリポジトリ外へ展開し、Shionji.exe が起動することを確認する。同梱漏れを周囲のビルド出力で補わないよう、別のフォルダで確認する。

停止と再実行は [依存更新とリリース](../dependency-release.md#失敗時の再実行)に従う。既存 draft の再配布時だけ tag を指定する。

## コード署名

実行ファイルのコード署名は行わない。実行環境によっては SmartScreen の警告を表示する。
