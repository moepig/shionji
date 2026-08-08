# Shionji

AWS Session Manager 専用のポートフォワード管理 GUI (Windows / WinUI 3)。

複数のポートフォワード設定を保持し、接続状況を一覧で可視化する。転送先はエンドポイント / IP の直接指定のほか、**ElastiCache / Aurora / EC2 / ECS のリソースを検索条件から自動特定**できる。トンネルは AWS SDK で `ssm:StartSession` を呼び、`session-manager-plugin.exe` を直接起動して確立する (AWS CLI 非依存)。

## 主な機能

- 転送設定の一覧 (状態ドット / 接続トグル / 解決済みエンドポイントの要約) + 詳細ペイン
- リソースクエリ: 名前 glob (`*` `?`) + タグ条件で ElastiCache / Aurora / EC2 / ECS を解決。複数一致 (Ambiguous) は候補一覧を表示
- 踏み台: EC2 インスタンス (ID 直接指定 / 検索) と ECS タスク (ECS Exec)。EC2 / ECS 転送先への直接セッションも可
- 自動再接続 (指数バックオフ 2s→30s、上限 5 回)、起動時自動接続、タスクトレイ常駐、切断時トースト通知
- ウィンドウ下部のステータスバーに最新の動作を表示。「ログ」ボタンから直近 200 件の履歴を確認でき、ログファイルの保存先も開ける (画面に出る内容とファイルログは同じ流れ)
- SSO トークン切れを検知し、詳細ペインの「SSO ログイン」ボタンから**アプリ内でブラウザ承認ログイン**できる (SDK のデバイス認可フロー。トークンは AWS CLI と `~/.aws/sso/cache` を共有するため CLI 側もログイン済みになる)。ログイン成功後は自動で再解決 / 再接続
- `--demo` でフェイク実装によるデモモード (AWS 不要)

## アーキテクチャ

ヘキサゴナル構成。ドメイン層がポート (インターフェース) を定義し、インフラ層が実装する。WinUI プロジェクトは XAML と DI 構成のみを持ち、GUI フレームワークの乗り換えに耐える。

| プロジェクト | 内容 |
| --- | --- |
| `src/Shionji.Domain` | 純粋 C#。VO / ForwardingConfig 集約 / TunnelPlanner / TunnelSession 状態機械 / ポート定義 |
| `src/Shionji.Application` | TunnelSupervisor (セッション監督・自動再接続) / ResolutionService (解決キャッシュ) / ConfigService / StartupService |
| `src/Shionji.Infrastructure` | AWS SDK アダプタ / session-manager-plugin 起動 (Job Object 付き) / JSON 永続化 / デモ用フェイク |
| `src/Shionji.Presentation` | WinUI 非依存の ViewModel 群 (CommunityToolkit.Mvvm) |
| `src/Shionji.App.WinUI` | WinUI 3 ヘッド (unpackaged)。XAML / DI / トレイ / 通知のみ |

## ビルドと実行

必要環境: Windows 10 19041+ / .NET SDK 10.0.3xx

```bash
dotnet test
```

`tests/Shionji.IntegrationTests` は **AWS アカウントも Docker もなし**で、実 SDK・実プロセス・実 TCP を通した結合検証を行う。

- **トンネル**: 偽の `session-manager-plugin` ([tests/Shionji.FakePlugin](tests/Shionji.FakePlugin)) を実プロセスとして起動し、引数の順序、実ポートの開通、データの往復、停止時の `TerminateSession`、異常系 (ポートを開かず終了 / 確立後の切断 / plugin 未検出) を確認する
- **リソース解決**: ローカルの HTTP スタブが AWS の JSON / Query 両プロトコルで応答し、実 SDK のシリアライズとアンマーシャルを通したまま、名前 glob の客側フィルタ、タグ条件の送出、ページング、エラー分類を確認する

```bash
dotnet build src/Shionji.App.WinUI/Shionji.App.WinUI.csproj
```

デモモード (AWS 不要。フェイクデータで全 UI フローを確認できる):

```bash
"src/Shionji.App.WinUI/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Shionji.App.WinUI.exe" --demo
```

デモの設定名は挙動のデモを兼ねる: `api-db` (起動時自動接続) / `cache` (確立 10 秒後に疑似切断 → 自動再接続) / `broken-ambiguous` (複数一致エラー) / `sso-expired` (SSO トークン切れ案内)。

## 実 AWS でのスモークテスト チェックリスト

アプリは `~/.aws/config` の名前付きプロファイルを使う。以下を順に実施する。

1. **プロファイル作成** — `~/.aws/config` に SSO プロファイルを定義する。

   ```ini
   [profile my-dev]
   sso_session = my-sso
   sso_account_id = 123456789012
   sso_role_name = MyRole
   region = ap-northeast-1

   [sso-session my-sso]
   sso_start_url = https://xxxx.awsapps.com/start
   sso_region = ap-northeast-1
   sso_registration_scopes = sso:account:access
   ```

2. **SSO ログイン**

   ```bash
   aws sso login --profile my-dev
   ```

3. **session-manager-plugin 未導入時の案内確認** — plugin 未インストールのまま接続し、詳細ペインにインストール案内 (`PluginNotFound`) が出ることを確認する。

4. **session-manager-plugin のインストール** — [公式手順](https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html) に従いインストール (既定: `C:\Program Files\Amazon\SessionManagerPlugin\bin`)。別の場所に置いた場合は `%APPDATA%\Shionji\appsettings.json` の `PluginPath` で指定する。

5. **EC2 踏み台経由で Aurora へ接続** — 転送先 = Aurora (検索 / Writer / 既定ポート)、経路 = EC2 踏み台。接続後:

   ```bash
   psql -h localhost -p 13306 -U myuser mydb
   ```

6. **同様に ElastiCache へ接続して疎通確認**

   ```bash
   redis-cli -p 16379 ping
   ```

7. **異常系の確認** — SSO トークン失効後に接続し、詳細ペインの「SSO ログイン」ボタンでブラウザ承認 → 自動で再接続されること (`aws sts get-caller-identity --profile my-dev` で CLI 側もログイン済みになっていること)。確立中に踏み台インスタンスを停止して自動再接続 (設定で有効時) とトースト通知が動くこと。

必要な IAM 権限 (最低限): `ssm:StartSession` / `ssm:TerminateSession` と、使用するクエリに応じた `ec2:DescribeInstances`, `rds:DescribeDBClusters`, `elasticache:Describe*`, `elasticache:ListTagsForResource`, `ecs:ListTasks`, `ecs:DescribeTasks`。踏み台側には SSM Agent (EC2) / ECS Exec 有効化 (ECS) が必要。

## データの保存先

- 転送設定: `%APPDATA%\Shionji\configs.json`
- アプリ設定 (plugin パス / トレイ挙動 / AWS エンドポイント上書き): `%APPDATA%\Shionji\appsettings.json`
- ログ: `%APPDATA%\Shionji\logs\shionji-yyyyMMdd.log` (既定 30 日で自動削除。`LogRetentionDays` で変更可)

## ログ (監査用途)

1 つの出来事につき「短い要約」と「詳細フィールド」を持ち、**画面には要約だけ、テキストログには詳細まで**出力する。画面のステータスバーは一目で状況が分かる簡潔さを保ち、ファイルは監査に必要な事実をすべて残す。

画面 (ステータスバー / 履歴):

```
api-db: localhost:13306 で接続しました
```

テキストログ (`ISO 8601 タイムスタンプ(オフセット付き) [レベル] カテゴリ: 要約 | key=値 …`):

```
2026-08-08T18:13:56.615+09:00 [INF] Shionji.Application.TunnelSupervisor: api-db: localhost:13306 で接続しました | 試行=6128f3e6 設定=api-db 転送先=demo-aurora.cluster-demo.ap-northeast-1.rds.amazonaws.com:3306 経路=EC2:i-0demo0123456789a SSMターゲット=i-0demo0123456789a 文書=AWS-StartPortForwardingSessionToRemoteHost プロファイル=demo@ap-northeast-1 ローカル=localhost:13306 セッション=s-demo508478
```

値に空白が含まれる場合は `"` で囲まれるため、`key=値` として機械的に読み取れる。

記録される項目:

| 項目 | 内容 |
| --- | --- |
| 起動時 | アプリの版、OS 利用者 (`ドメイン\ユーザー`)、端末名、プロセス ID |
| `#xxxxxxxx` | 接続試行ごとの相関 ID。1 回の試行に属する行を突き合わせる。再接続では振り直す |
| `転送先` / `経路` / `文書` | 実際に繋いだエンドポイント、経由した踏み台 (SSM ターゲット)、使用した SSM ドキュメント |
| `プロファイル` | 使用した AWS プロファイルとリージョン |
| `セッション` | SSM セッション ID。CloudTrail や `aws ssm describe-sessions` と突き合わせる鍵 |
| 解決の証跡 | 検索条件がどの実リソースに解決されたか (表示名 + ARN / インスタンス ID + エンドポイント) |
| 切断 | 理由 (利用者操作 / 設定変更 / アプリ終了 / 予期せぬ終了)、接続時間、原因のフェーズとコード |

セッショントークンや資格情報はログに出力しない。

デモモードはファイルに一切書き込まない (インメモリ)。
