# NexStrap - ビルドガイド

## 必要なもの

### 1. .NET 9 SDK をインストール
https://dotnet.microsoft.com/download/dotnet/9.0

Windows x64 の「SDK」をダウンロードしてインストールしてください。

### 2. Visual Studio 2022 (推奨) または VS Code
- VS 2022: .NET デスクトップ開発 ワークロードを含める
- VS Code: C# Dev Kit 拡張機能をインストール

---

## ビルド手順

```powershell
# ソリューションのルートで実行
cd "E:\Claude code\New project03"

# 依存パッケージを復元
dotnet restore

# デバッグビルド
dotnet build

# 実行
dotnet run --project src/NexStrap/NexStrap.csproj

# リリースビルド (最適化)
dotnet publish src/NexStrap/NexStrap.csproj -c Release -r win-x64 --self-contained
```

---

## Discord Rich Presence の設定

1. https://discord.com/developers/applications にアクセス
2. 「New Application」でアプリを作成（例: NexStrap）
3. 「OAuth2」→ Application ID をコピー
4. `src/NexStrap.Core/Services/DiscordRpcService.cs` の `ClientId` に貼り付け

---

## プロジェクト構成

```
NexStrap.sln
├── src/NexStrap/           # Avalonia UI フロントエンド
│   ├── Views/Pages/
│   │   ├── HomePage        # 起動・ステータス・クイックアクション
│   │   ├── FastFlagsPage   # Fast Flags エディター + ホットリロード
│   │   ├── ModsPage        # Mod マネージャー
│   │   ├── BrowserPage     # 内蔵ブラウザ (YouTube等)
│   │   └── SettingsPage    # 設定
│   └── ViewModels/
└── src/NexStrap.Core/      # ビジネスロジック
    ├── Services/
    │   ├── RobloxService       # Roblox 検出・起動
    │   ├── FastFlagService     # Flags 読み書き・ホットリロード
    │   ├── ModService          # Mod インポート・適用
    │   ├── DiscordRpcService   # Discord Rich Presence
    │   ├── ProfileService      # プロファイル管理
    │   └── SettingsService     # アプリ設定
    └── Models/
```

## 独自機能

| 機能 | 説明 |
|------|------|
| ホットリロード | Roblox 起動中に Fast Flags を変更 → 次ゲーム参加時に即反映 |
| 内蔵ブラウザ | ゲームしながら YouTube・アニメ・Twitter を閲覧 |
| FPS スライダー | GUI で FPS 上限をスライダー調整 |
| プロファイル | 設定セットを複数保存・一発切り替え |
| マルチインスタンス | Roblox を複数同時起動 |
