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

Windows App SDK はアプリに同梱されているため、別途の導入は不要である。AWS CLI は接続や既存の SSO ログインには不要だが、メニューの「AWSにログイン…」には AWS CLI v2（2.32.0 以降）が必要である。インストール後は `aws` を PATH から実行できるようにし、Shionji を再起動する。

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

   配布物一式を任意のフォルダへ展開し、`Shionji.exe` を実行する。インストーラーは無く、レジストリも書き換えない。アンインストールはフォルダの削除と、[保存先のファイル](config.md#保存先) の削除で完了する。

6. 接続先設定を追加する。

   一覧の下部にある「接続先設定を追加」を押し、接続先設定ウィンドウで項目を入力する。項目の意味は [設定](config.md#接続先設定) を参照。

   リソース自動検索を使う場合は、保存する前に「この条件で検索してみる」で条件が意図した 1 件に絞れることを確認できる。

7. 接続して疎通を確認する。

   一覧の行にある接続トグルを押す。状態ドットが緑になれば確立している。詳細ペインの「コピー」で `localhost:ポート番号` を取得し、対象に応じたクライアントで疎通を確認する。

   ```bash
   psql -h localhost -p 13306 -U myuser mydb
   ```

## AWS コンソールの資格情報でログインする

メニューの「AWS認証」→「AWSにログイン…」、またはログの上にある認証状態領域の「ログイン…」を押す。`aws login --profile <プロファイル> --region <リージョン>` を実行し、コマンド出力の認証 URL を読み取って既定のブラウザを一度だけ開く。ブラウザで承認すると一時資格情報が保存される。

対象は選択中の接続先のプロファイルとリージョンである。接続先を選んでいない場合は `default / ap-northeast-1` を使う。実行前に認証状態領域の対象表示を確認する。ログイン中は「ログインをキャンセル」で中止でき、対象を切り替えた場合やアプリ終了時にも中止する。

認証状態は起動時、対象切り替え時、ログイン後、ウィンドウを再表示したとき、および 1 分ごとに確認する。「認証状態を再確認」から手動でも更新できる。SDK で資格情報を取得し、STS の `GetCallerIdentity` が成功してセッショントークンがある場合に「一時認証済み」と表示する。長期アクセスキーの場合は「一時認証なし（長期キー使用中）」、失効時は「未認証・期限切れ」、通信障害などで判断できない場合は「確認できません」と表示する。確認だけでブラウザは開かない。

SSO や長期キーが設定されたプロファイルは `aws login` と併用できないため、専用のプロファイルを使う。既存プロファイルの別アカウントへの切り替えに確認入力が必要な場合は、アプリで自動承諾せずターミナルでの実行を案内する。認証 URL とトークンはログに保存しない。

必要な権限や認証方式の詳細は [AWS CLI のログイン手順](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-sign-in.html) を参照。

認証状態領域は 1 行で表示する。状態の文字をクリックすると、確認時刻・認証の詳細・エラー全文を確認できる。

画面下部の「ログ」とログファイルの両方に、対象プロファイル・リージョン、ログイン開始、ブラウザ承認待ち、終了コード、成功・失敗・キャンセル、認証状態の変化を記録する。定期確認で状態が変わらない場合は同じログを繰り返さない。ログへ渡す内容は定義済みの進行状況と結果だけに限定し、CLI の生出力、認証 URL、アクセスキー、各種トークン、例外の原文は保存しない。

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
