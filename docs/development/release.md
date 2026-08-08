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

## 手順

1. 作業ツリーが `main` の最新で、未コミットの変更が無いことを確認する。

2. 全テストが通ることを確認する。

   ```bash
   dotnet test Shionji.slnx
   ```

3. デモモードで UI の主要経路を確認する。手順は [ビルド](build.md#アプリの実行) を参照。

4. 実 AWS でのスモークテストを行う。項目は [テスト](test.md#実-aws-でのスモークテスト) を参照。

5. バージョンを決めて `src/Shionji.App.WinUI/Shionji.App.WinUI.csproj` に記録する。

   ```xml
   <Version>1.2.0</Version>
   ```

   この値は実行ファイルのバージョン情報になり、起動時のログ 1 行目にも出る。版を上げずにリリースすると、利用者から受け取ったログでどのビルドかを判別できなくなる。

6. 配布物を作る。

   ```bash
   dotnet publish src/Shionji.App.WinUI/Shionji.App.WinUI.csproj -c Release -r win-x64 -o publish
   ```

7. 生成された `publish` フォルダで、`Shionji.App.WinUI.exe` がリポジトリの外から起動することを確認する。開発機ではビルド出力に紛れて動いてしまうため、別のフォルダへ移してから確認する。

8. `publish` フォルダを `shionji-<バージョン>-win-x64.zip` として圧縮する。

9. バージョンのコミットにタグを打つ。

   ```bash
   git tag v1.2.0
   ```

## 未整備の項目

以下は現時点で仕組みが無く、リリースのたびに手で行うか、行っていない。

| 項目 | 現状 |
| --- | --- |
| 継続的インテグレーション | ビルドとテストを自動実行する仕組みが無い。手順 2 と 3 は手で行う |
| コード署名 | 実行ファイルに署名していない。利用者の環境では SmartScreen の警告が出る |
| 変更履歴 | `CHANGELOG` を持っていない。変更内容はコミットログから辿る |
