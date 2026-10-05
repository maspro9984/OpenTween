# CLAUDE.md

このファイルは、このリポジトリでコードを操作する際の Claude Code (claude.ai/code) への指針を提供します。

# 実行ポリシー

このプロジェクトでは自律実行モードで作業します。

## 言語

- プロンプトのやりとりは日本語でお願いします（途中経過・完了報告・質問も含めて全部日本語で書く）。


## 基本方針
・確認を求めずに進めてください。
・安全な編集は承認済みとして扱ってください。
・ファイルは直接変更してください。
・計画フェーズは不要です。直接実装してください。
・タスクが完了するまで中断せずに進めてください。

## 許可されている操作
・複数ファイルの変更
・新規ファイルの作成
・クラス／メソッドの追加・修正
・コンパイルエラーの修正
・ビルドコマンドの実行
・リファクタリング作業

## 確認が必要な場合
・重要なファイルの削除
・破壊的な変更
・データ消失の可能性がある操作

それ以外は自律的に判断して進めてください。

## 公開リポジトリでのコミット・push 前チェック（最優先）

このリポジトリは GitHub で **public** になっている。一度 push した内容は、後から履歴を書き換えても第三者のコピーや GitHub 上のキャッシュに残るため、実質的に取り消せない。この節は上の「確認を求めずに進めてください」より優先する。

### コミット前に必ず行うこと
・`git add -A` / `git add .` / `git commit -a` は使わない。今回の作業で変更したファイルだけを名前で指定して add する。
・commit 前に `git status` と `git diff --cached` でステージした差分を全行確認する。新規ファイルとバイナリファイルは特に中身を確認する。
・コミットメッセージ、コードコメント、テストデータにも下記の情報を書かない。

### 絶対にコミットしてはいけないもの
・認証情報：パスワード、API キー、アクセストークン、Cookie（`auth_token` / `ct0` 等）、OAuth シークレット、秘密鍵、`.env`、実際の設定ファイル（`SettingCommon.xml` 等）
・個人情報：本名、住所、電話番号、個人のメールアドレス（下記の noreply アドレス以外すべて）、ユーザー名入りのローカルパス（`C:\Users\…`、`D:\…` 等）
・このプロジェクトと無関係な個人的な内容：株・投資・銘柄・資産・金融に関するメモや数値、仕事・家庭・他人に関する情報、他のプロジェクトのコードや文書
・写真・画像・スクリーンショット：アプリのアイコン等ソースとして必要なもの以外は追加しない。スクリーンショットにはタイムライン・他人のアカウント・通知などが写り込み、写真には撮影場所（EXIF の位置情報）が含まれうる
・実データ：実アカウントのタイムライン・DM・ログ・ダンプ・キャッシュ（`FollowerIds_*.txt` 等）。API レスポンスをテストデータにする場合は、既存のフィクスチャと同様に公開済みの投稿に限り、トークンや個人を特定できる値は伏せる
・ビルド成果物・ローカル専用ファイル：`OpenTween.zip`、`bin/`、`obj/`、`.claude/settings.local.json`

### 迷ったとき・誤ってコミットしたとき
・上記に該当する、または該当するか判断できない変更がある場合は、commit・push せずにユーザーに確認する。
・push 前に気づいた場合は、そのコミットを取り消してから進める。
・push 後に気づいた場合は、それ以上 push せず直ちにユーザーに報告する（自己判断で履歴を書き換えない）。

### コミットの作者情報
・作者メールアドレスは `77472337+maspro9984@users.noreply.github.com` を使う。`git config user.email` がこれと異なる場合は、コミット前にこのリポジトリのローカル設定を直す。

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
