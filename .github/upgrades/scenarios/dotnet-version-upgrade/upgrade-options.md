# Upgrade Options — PileDesign.sln

Assessment: 3 projects (all net8.0-windows / SDK-style), 8 incompatible packages, binary/source API breaking changes detected.

## Strategy

### Upgrade Strategy
互換性最優先のため、作業中のビルド可能性を維持しやすい段階的アプローチを選びます。

| Value | Description |
|-------|-------------|
| **Top-Down** (selected) | エントリーポイント側から先に上げ、共有ライブラリは必要に応じて一時的にマルチターゲット化して段階移行します。 |
| All-at-Once | 全プロジェクトを一括で更新します。最短ですが移行中に全体が壊れるリスクがあります。 |

## Project Structure

### Package Management
3プロジェクト構成かつモダン.NET間アップグレードですが、まずは変更量を最小化するためCPM導入を後段に回します。

| Value | Description |
|-------|-------------|
| **Per-Project (defer CPM to post-migration)** (selected) | 移行中は各プロジェクトごとにPackageReferenceを維持し、CPMは移行完了後に検討します。 |
| Central Package Management (CPM) | `Directory.Packages.props` を導入してバージョンを集中管理します。 |

## Compatibility

### Unsupported Packages
互換性非対応のパッケージ件数が多いため、まずコンパイル継続性を確保する方針を選びます。

| Value | Description |
|-------|-------------|
| **Defer Resolution** (selected) | まずビルド継続を優先し、難しい置換は後続タスクで段階的に解決します。 |
| Resolve Inline | 同一タスク内で互換性非対応パッケージを調査・置換し切ります。 |
| Compatibility Mode | 参照アセンブリ互換モードで暫定維持します（ランタイムリスクあり）。 |

### Unsupported API Handling
複雑変更の先送りを増やしすぎないため、既知の置換は同タスク内で処理する基本方針にします。

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | API互換性問題を同一タスク内で解決し、後送りを最小化します。 |
| Defer Complex Changes | 複雑なAPI変更はSTUB化して後続サブタスクで解決します。 |
