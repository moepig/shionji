# リリース

## 配布形態

インストーラーは作らない。実行に必要なファイル一式を含むフォルダを圧縮して配布し、利用者は任意の場所へ展開して実行する。レジストリと共有フォルダには何も書かない。

同梱するもの、しないものを次に示す。

| 要素 | 扱い |
| --- | --- |
| Windows App SDK | 同梱する (`WindowsAppSDKSelfContained`) |
| .NET ランタイム | 同梱しない。利用者側に .NET Desktop Runtime 10.0 が要る |
| `session-manager-plugin` | 同梱しない。利用者が別途導入する |

.NET ランタイムを同梱する場合は publish に `--self-contained` を加える。配布物は 3 割ほど大きくなる。

## リリースワークフロー

配布物の生成と GitHub Release への公開は GitHub Actions で行う。定義は [`.github/workflows/release.yml`](../../.github/workflows/release.yml)。

| 要素 | 内容 |
| --- | --- |
| 契機 | `v` で始まるタグの push |
| 実行環境 | `windows-2025`。SDK の版は `global.json` に従う |
| 処理 | バージョンの照合、`dotnet test` による全テスト、`dotnet publish`、zip 圧縮、Release の作成 |
| 成果物 | `shionji-<バージョン>-win-x64.zip` を Release に添付する。あわせて 14 日間 Actions のアーティファクトにも残す |

バージョンの照合は、タグから `v` を除いた文字列と `src/Shionji.App.WinUI/Shionji.App.WinUI.csproj` の `Version` を突き合わせる。食い違う場合と `Version` が無い場合はここで停止し、テスト以降を実行しない。

バージョンにハイフンを含む場合 (`1.2.0-rc.1` など) はプレリリースとして作成し、latest として扱わない。

Release の本文はタグ間のコミットから自動生成する。

## 手順

1. 作業ツリーが `main` の最新で、未コミットの変更が無いことを確認する。

2. デモモードで UI の主要経路を確認する。手順は [ビルド](build.md#アプリの実行) を参照。

3. 実 AWS でのスモークテストを行う。項目は [テスト](test.md#実-aws-でのスモークテスト) を参照。

4. バージョンを決めて `src/Shionji.App.WinUI/Shionji.App.WinUI.csproj` に記録し、コミットする。

   ```xml
   <Version>1.2.0</Version>
   ```

   この値は実行ファイルのバージョン情報になり、起動時のログ 1 行目にも出る。版を上げずにリリースすると、利用者から受け取ったログでどのビルドかを判別できなくなる。

5. バージョンのコミットにタグを打ち、push する。

   ```bash
   git tag v1.2.0
   ```

   ```bash
   git push origin main v1.2.0
   ```

6. ワークフローの完了と、Release に zip が添付されていることを確認する。

7. 添付された zip を展開し、`Shionji.App.WinUI.exe` がリポジトリの外から起動することを確認する。開発機ではビルド出力に紛れて動いてしまうため、別のフォルダへ展開してから確認する。

## 未整備の項目

以下は現時点で仕組みが無く、リリースのたびに手で行うか、行っていない。

| 項目 | 現状 |
| --- | --- |
| 継続的インテグレーション | ビルドとテストを回すのはリリースワークフローのみ。PR と push では何も実行しない |
| コード署名 | 実行ファイルに署名していない。利用者の環境では SmartScreen の警告が出る |
| 変更履歴 | `CHANGELOG` を持っていない。Release の本文はコミットログから自動生成している |
