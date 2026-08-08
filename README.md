# Shionji

AWS Session Manager 専用のポートフォワード管理 GUI (Windows / WinUI 3)。

複数のポートフォワード設定を保持し、接続状況を一覧で可視化する。転送先はエンドポイント / IP の直接指定のほか、**ElastiCache / Aurora / EC2 / ECS のリソースを検索条件から自動特定**できる。トンネルは AWS SDK で `ssm:StartSession` を呼び、`session-manager-plugin.exe` を直接起動して確立する (AWS CLI 非依存)。

## 主な機能

- 転送設定の一覧 (状態ドット / 接続トグル / 解決済みエンドポイントの要約) + 詳細ペイン
- リソースクエリ: 名前 glob (`*` `?`) + タグ条件で ElastiCache / Aurora / EC2 / ECS を解決。複数一致 (Ambiguous) は候補一覧を表示
- 踏み台: EC2 インスタンス (ID 直接指定 / 検索) と ECS タスク (ECS Exec)。EC2 / ECS 転送先への直接セッションも可
- 自動再接続 (指数バックオフ 2s→30s、上限 5 回)、起動時自動接続、タスクトレイ常駐、切断時トースト通知
- SSO トークン切れを検知し `aws sso login --profile <name>` を案内
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

7. **異常系の確認** — SSO トークン失効後に接続し `aws sso login` 案内が出ること、確立中に踏み台インスタンスを停止して自動再接続 (設定で有効時) とトースト通知が動くこと。

必要な IAM 権限 (最低限): `ssm:StartSession` / `ssm:TerminateSession` と、使用するクエリに応じた `ec2:DescribeInstances`, `rds:DescribeDBClusters`, `elasticache:Describe*`, `elasticache:ListTagsForResource`, `ecs:ListTasks`, `ecs:DescribeTasks`。踏み台側には SSM Agent (EC2) / ECS Exec 有効化 (ECS) が必要。

## データの保存先

- 転送設定: `%APPDATA%\Shionji\configs.json`
- アプリ設定 (plugin パス / トレイ挙動): `%APPDATA%\Shionji\appsettings.json`

デモモードはファイルに一切書き込まない (インメモリ)。
