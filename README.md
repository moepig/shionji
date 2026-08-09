# Shionji

AWS Session Manager を使用したポートフォワードの管理 GUI ツール

![](docs/images/main-window.png)

複数のポートフォワード設定を保持し、各接続の状況を一覧できる。

## 機能の概要

Session Manager を使用して特定の ECS / EC2 に接続し、そのインスタンス自身もしくはインスタンスから到達可能なホスト・ポートのローカルへのフォワーディングを行う。

Session Manager 接続先およびフォワーディング対象はエンドポイント / IP の直接指定のほか、リソースを名前やタグから特定することもできる。対応しているリソース種別は以下の通り。

- EC2
- ECS
- ElastiCache
- Aurora

## ドキュメント

### 利用者向け

- [インストール手順](docs/usage/setup.md)
- [設定](docs/usage/config.md)

### 開発者向け

- [ビルド](docs/development/build.md)
- [テスト](docs/development/test.md)
- [リリース](docs/development/release.md)

### 設計

- [構成概要](docs/architecture/overview.md)
- [ドメインモデル](docs/architecture/domain-model.md)

## リポジトリ構成

トップレベルのディレクトリと、そこに置くものを以下にまとめる。

| パス                         | 内容                                                             |
| ---------------------------- | ---------------------------------------------------------------- |
| `src/Shionji.Domain`         | 値オブジェクト、集約、ドメインサービス、状態機械、ポート定義     |
| `src/Shionji.Application`    | セッション監督、解決キャッシュ、設定 CRUD、起動時処理            |
| `src/Shionji.Infrastructure` | AWS SDK アダプタ、plugin 起動、JSON 永続化、ログ、デモ用フェイク |
| `src/Shionji.Presentation`   | GUI フレームワーク非依存の ViewModel                             |
| `src/Shionji.App.WinUI`      | WinUI 3 ヘッド (unpackaged)。XAML と DI 構成のみ                 |
| `tests/`                     | 層ごとの単体テストと結合テスト、テスト補助                       |
