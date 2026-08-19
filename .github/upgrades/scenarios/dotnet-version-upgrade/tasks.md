# .NET Version Upgrade Progress

## Overview

PileDesign ソリューションを net10.0 へ互換性優先で移行します。中核の PileDesign を先行し、依存するテスト/ベンチマークを順次追従する最小変更アプローチです。破壊的変更を避け、段階ごとに検証します。

**Progress**: 0/5 tasks complete <progress value="0" max="100"></progress> 0%

## Tasks

- 🔲 01-prerequisites: SDK とビルド前提の固定
- 🔲 02-upgrade-piledesign: PileDesign 本体の TFM/Pkg/API 最小移行
- 🔲 03-upgrade-testproject1: テストプロジェクト追従移行
- 🔲 04-upgrade-benchmarksuite1: ベンチマークプロジェクト追従移行
- 🔲 05-final-validation: 全体検証と保留項目整理
