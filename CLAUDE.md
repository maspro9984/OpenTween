# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

OpenTween は .NET Framework 4.8 / C# で開発された Windows 用 Twitter/Misskey クライアント。UIは Windows Forms。Tween からのフォークであり、GPLv3 ライセンス。コードのコメントやUI文字列は主に日本語。

## ビルド・テストコマンド

**ビルド**（Visual Studio 2022 + 「.NET デスクトップ開発」ワークロードが必要）:
```
msbuild /target:restore,build /p:Configuration=Debug /verbosity:minimal
```

**全テスト実行**（xUnit）:
```
dotnet test OpenTween.Tests/OpenTween.Tests.csproj
```

**特定テストの実行（完全修飾名指定）:**
```
dotnet test OpenTween.Tests/OpenTween.Tests.csproj --filter "FullyQualifiedName=OpenTween.PostClassTest.MethodName"
```

**パターンに一致するテストの実行:**
```
dotnet test OpenTween.Tests/OpenTween.Tests.csproj --filter "FullyQualifiedName~SomePattern"
```

## ソリューション構成

- **OpenTween/** — メインアプリケーション（WinExe, `net48`, C# 11.0）
- **OpenTween.Tests/** — ユニットテスト（xUnit 2.6.2, Moq 4.20.70, Xunit.StaFact）

OpenTween の internal 型は `InternalsVisibleTo` によりテストプロジェクトからアクセス可能。

## アーキテクチャ

### エントリーポイントと起動フロー
`ApplicationEvents.Main()` → `SettingManager` の読み込み → `ApplicationContainer` の生成 → `TweenMain`（メインウィンドウ）の起動。`ApplicationContainer` がコンポジションルートとして機能し、`Lazy<T>` / `DisposableLazy<T>` による遅延初期化でサービスを管理する。

### マルチプラットフォーム対応（`SocialProtocol/`）
Twitter と Misskey を共通インターフェースで抽象化:
- `ISocialAccount` — 認証済みアカウント（Twitter または Misskey）を表す
- `ISocialProtocolClient` — プラットフォーム固有の API 操作
- `AccountCollection` — 複数アカウントの管理
- 実装は `SocialProtocol/Twitter/` と `SocialProtocol/Misskey/` に配置

### API・通信レイヤー（`Connection/`, `Api/`）
- `IApiConnection` — HTTP 通信の単一メソッドインターフェース（`SendAsync(IHttpRequest)`）
- `TwitterApiConnection` — OAuth 1.0a およびクッキーベース認証
- `MisskeyApiConnection` — Misskey API 接続
- リクエスト型: `GetRequest`, `PostRequest`, `PostJsonRequest`, `PostMultipartRequest`, `DeleteRequest`
- `Api/` にプラットフォーム固有の API メソッド、`Api/DataModel/` に JSON マッピング型
- `Api/GraphQL/` と `Api/TwitterV2/` で新しい Twitter API バージョンに対応

### データモデル（`Models/`）
- `PostClass` — ツイート/投稿を表す中心的な record 型
- `TabInformations` — 全タブの状態と投稿コレクションを管理するシングルトン
- タブモデル: `HomeTabModel`, `MentionsTabModel`, `FavoritesTabModel`, `SearchTabModel` 等
- 厳密な型付き ID: `PostId`, `PersonId`, `AccountKey`

### 設定管理（`Setting/`）
- `SettingManager` — 永続化された設定を管理するシングルトン
- `SettingCommon` — アプリ全体の設定、`SettingLocal` — マシン固有の設定、`SettingTabs` — タブ構成
- `Setting/Panel/` — `SettingPanelBase` を継承する設定 UI パネル群

### UI レイヤー
- `TweenMain`（`Tween.cs` で定義）— タブベースのタイムラインを持つメインフォーム
- `Controls/` にカスタムコントロール（`PublicSearchHeaderPanel`, `GeneralTimelineHeaderPanel` 等）
- ルートレベルに各種ダイアログフォーム（`FilterDialog`, `UserInfoDialog`, `LoginDialog` 等）
- ローカライズ: 日本語（デフォルト `.resx`）と英語（`.en.resx`）のサテライトリソース

### サムネイルシステム（`Thumbnail/`）
- `ThumbnailGenerator` — 画像プレビュー生成を統括
- `Thumbnail/Services/` — サービスごとのサムネイルリゾルバー

## コードスタイル

- StyleCop.Analyzers による静的解析（設定は `.editorconfig` と `stylecop.json`）
- `#nullable enable` を全体で使用（null 許容参照型）
- `using` ディレクティブは名前空間の**外側**に記述
- インスタンスメンバーには `this.` を付ける（フィールド、プロパティ、メソッド、イベント）
- C# ファイルのインデントは 4 スペース
