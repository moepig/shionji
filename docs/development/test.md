# テスト

本ドキュメントは、自動テストの実行方法と構成、および自動テストで賄えない範囲の手動確認を扱う。ビルドの前提は [ビルド](build.md) を参照。

## 実行

全テストを実行するコマンドは次のとおりである。

```bash
dotnet test Shionji.slnx
```

特定のプロジェクトだけを実行する場合はプロジェクトを指定する。

```bash
dotnet test --project tests/Shionji.Domain.Tests/Shionji.Domain.Tests.csproj
```

## テストフレームワーク

TUnit を使う。Microsoft.Testing.Platform (MTP) 上で動作するため、テストプロジェクトは実行可能ファイルとしてビルドされる。

`dotnet test` を MTP モードで動かす設定は `global.json` にある。

```json
{
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

この指定が無いと `dotnet test` は VSTest モードで動き、テストを 1 件も見つけられない。

用途ごとの記法を、以下にまとめる。

| 用途 | 記法 |
| --- | --- |
| テストの宣言 | `[Test]` |
| パラメタライズ | `[Arguments(...)]` |
| アサーション | `await Assert.That(actual).IsEqualTo(expected)` |
| 直列実行の強制 | `[NotInParallel]` |

テストメソッド名は日本語で書き、確認したい性質を文として表すこと。C# の識別子の制約から、数字で始まる名前と空白を含む名前は使えない。

## テストプロジェクトの構成

層ごとにテストプロジェクトを分けている。プロジェクトと検証対象の対応を、以下にまとめる。

| プロジェクト | 対象 |
| --- | --- |
| `Shionji.Domain.Tests` | 値オブジェクトの検証、集約の不変条件、`TunnelPlanner` の全組み合わせ、状態機械、glob 一致 |
| `Shionji.Application.Tests` | セッション監督のライフサイクル、自動再接続、解決キャッシュ、ログの内容 |
| `Shionji.Infrastructure.Tests` | AWS 応答のマッピング、エラー分類、永続化、ログファイル出力、パスの解決 |
| `Shionji.Presentation.Tests` | ViewModel の状態遷移と表示文言 |
| `Shionji.IntegrationTests` | 実プロセス・実 TCP・実 SDK を通した結合検証 |

補助プロジェクトが 2 つある。内容は次のとおりである。

| プロジェクト | 内容 |
| --- | --- |
| `Shionji.TestSupport` | ポートのフェイク実装、それらを組んだハーネス、テストデータの生成。Application 層以上のテストで共用する |
| `Shionji.FakePlugin` | `session-manager-plugin.exe` の代役となる実行可能ファイル。結合テストから実プロセスとして起動する |

## 結合テスト

AWS アカウントも Docker も使わずに、実 SDK・実プロセス・実 TCP を通した検証を行う。フェイクをアプリ側に差し込むのではなく、アプリの外側 (プロセスとネットワーク) を置き換えている点が単体テストとの違いである。

### 偽 plugin によるトンネル検証

`Shionji.FakePlugin` は本物と同じ 6 引数を受け取り、実際にローカルポートを listen して接続をエコーバックする。挙動は環境変数で切り替える。指定できる環境変数を、以下に示す。

| 環境変数 | 内容 |
| --- | --- |
| `SHIONJI_FAKE_PLUGIN_MODE` | `normal` / `exit-before-open` / `hang` / `drop-after` |
| `SHIONJI_FAKE_PLUGIN_DROP_AFTER_MS` | `drop-after` で確立から切断までの時間 |
| `SHIONJI_FAKE_PLUGIN_ARGS_FILE` | 受け取った引数を JSON 配列で書き出す先 |
| `SHIONJI_FAKE_PLUGIN_QUIET` | `1` で確立の出力を抑止する (ポートは開く) |

これにより、AWS CLI と同じ引数の順序と内容、ローカルポートの実際の開通、データの往復、停止時の `TerminateSession` 呼び出し、Job Object への所属、および異常系 (ポートを開かず終了 / 確立後の切断 / plugin 未検出 / 確立タイムアウト) を確認する。

### スタブ AWS サーバによるリソース検索の検証

ローカルに立てた HTTP サーバが AWS の JSON プロトコル (`X-Amz-Target`) と Query プロトコル (フォーム送信と XML 応答) で応答する。SDK のシリアライズとアンマーシャルを通したまま、名前 glob の客側フィルタ、タグ条件の送出、ページング、エラー分類を確認する。

## 実 AWS でのスモークテスト

自動テストで確認できない範囲を、実際の AWS 環境に対して手で確認する。確認に使う環境の準備手順は、[インストール手順](../usage/setup.md) を参照。

1. SSO プロファイルでログインし、リソース自動検索が意図した 1 件に絞れることを接続先設定ウィンドウの検索確認で見る。

2. `session-manager-plugin` を未導入の状態で接続し、詳細ペインにインストール案内 (`PluginNotFound`) が出ることを確認する。

3. plugin を導入し、EC2 踏み台経由で Aurora へ接続して疎通を確認する。

   ```bash
   psql -h localhost -p 13306 -U myuser mydb
   ```

4. 同様に ElastiCache へ接続して疎通を確認する。

   ```bash
   redis-cli -p 16379 ping
   ```

5. SSO トークンを失効させた状態で接続し、詳細ペインの「SSO ログイン」からブラウザ承認を経て自動で再接続されることを確認する。あわせて CLI 側もログイン済みになることを見る。

   ```bash
   aws sts get-caller-identity --profile my-dev
   ```

6. 確立中に踏み台インスタンスを停止し、自動再接続とトースト通知が動くことを確認する。

7. ログファイルに、接続した実エンドポイント、経由した踏み台、SSM セッション ID が残っていることを確認する。
