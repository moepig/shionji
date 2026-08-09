# インストール手順

本ドキュメントは、Shionji を初めて使うまでの手順を扱う。設定項目そのものの説明は、[設定](config.md) を参照。

## 必要なもの

導入前に揃えるものを、以下に示す。

| 要件 | 内容 |
| --- | --- |
| OS | Windows 10 バージョン 2004 (ビルド 19041) 以降、x64 |
| .NET | .NET Desktop Runtime 10.0 (x64) |
| session-manager-plugin | AWS 公式の `session-manager-plugin.exe` |
| AWS プロファイル | `~/.aws/config` に定義された名前付きプロファイル |

Windows App SDK はアプリに同梱されているため、別途の導入は不要である。AWS CLI も接続には不要である (SSO ログインをコマンドで行う場合のみ使う)。

## 手順

1. .NET Desktop Runtime 10.0 (x64) を導入する。

   [.NET 10 のダウンロードページ](https://dotnet.microsoft.com/download/dotnet/10.0) から Windows x64 の Desktop Runtime を入手する。導入済みかどうかは次で確認できる。

   ```bash
   dotnet --list-runtimes
   ```

   `Microsoft.WindowsDesktop.App 10.0.x` の行があればよい。

2. session-manager-plugin を導入する。

   [公式手順](https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html) に従う。既定の導入先は `C:\Program Files\Amazon\SessionManagerPlugin\bin` で、Shionji はこの場所と `PATH` を自動で探す。別の場所に置いた場合はアプリ設定の `PluginPath` で指定する。

3. AWS プロファイルを用意する。

   `~/.aws/config` に接続に使うプロファイルを定義する。SSO を使う場合の例を次に示す。

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

4. IAM 権限を確認する。

   接続に使うロールに、[必要な IAM 権限](#必要な-iam-権限) に挙げた権限が付いていることを確認する。

5. Shionji を配置して起動する。

   配布物一式を任意のフォルダへ展開し、`Shionji.App.WinUI.exe` を実行する。インストーラーは無く、レジストリも書き換えない。アンインストールはフォルダの削除と、[保存先のファイル](config.md#保存先) の削除で完了する。

6. 接続先設定を追加する。

   一覧の下部にある「接続先設定を追加」を押し、接続先設定ウィンドウで項目を入力する。項目の意味は [設定](config.md#接続先設定) を参照。

   リソース自動検索を使う場合は、保存する前に「この条件で検索してみる」で条件が意図した 1 件に絞れることを確認できる。

7. 接続して疎通を確認する。

   一覧の行にある接続トグルを押す。状態ドットが緑になれば確立している。詳細ペインの「コピー」で `localhost:ポート番号` を取得し、対象に応じたクライアントで疎通を確認する。

   ```bash
   psql -h localhost -p 13306 -U myuser mydb
   ```

## SSO トークン失効時の再ログイン

SSO トークンの期限が切れると、詳細ペインに認証エラーと「SSO ログイン」ボタンが出る。このボタンを押すとブラウザが開き、承認するとログインが完了して自動で再検索と再接続が行われる。

このログインは AWS CLI と同じ `~/.aws/sso/cache` にトークンを書くため、`aws sso login` を別途実行する必要はない。逆にコマンドから先にログインしておいてもよい。

## 必要な IAM 権限

接続に使うロールへ付与するアクションを、用途ごとに以下へまとめる。

| 用途 | アクション |
| --- | --- |
| セッションの開始と終了 | `ssm:StartSession`, `ssm:TerminateSession` |
| EC2 のリソース自動検索 | `ec2:DescribeInstances` |
| Aurora のリソース自動検索 | `rds:DescribeDBClusters` |
| ElastiCache のリソース自動検索 | `elasticache:Describe*`, `elasticache:ListTagsForResource` |
| ECS のリソース自動検索 | `ecs:ListTasks`, `ecs:DescribeTasks` |

リソース自動検索の権限は、実際に使う種別のものだけでよい。

踏み台側にも準備が必要である。EC2 を踏み台にする場合は、SSM Agent が動作していて Systems Manager の管理対象になっていること。ECS を踏み台にする場合は、タスクで ECS Exec が有効になっていること。
