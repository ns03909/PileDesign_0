using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// help.html とアプリの主要 UI ラベル (リボンタブ + 主要ウィンドウ Title) の対応をチェックする。
    ///
    /// 目的: 機能追加時に help.html 更新を忘れる問題への対策。
    /// 新しいリボンタブ / ウィンドウを追加した瞬間、help.html に未記載なら fail。
    ///
    /// 検査範囲 (誤検出を避けるため意図的に保守的):
    ///   - Fluent:RibbonTabItem Header=... のテキスト (Alt キーマーク除去後)
    ///   - 主要ウィンドウの Title="..." (HorizontalCalculationWindow 等の主要編集ウィンドウのみ)
    ///
    /// 検査基準: 単純な substring 検索 (Header テキストが help.html のどこかに含まれていれば OK)。
    /// </summary>
    [TestClass]
    public class HelpCoverageTests
    {
        // 検査対象のラベル -> 探したい help.html 内の代表キーワード (Alt キーマーク除去後の文字列でも OK)
        // null の場合は Header テキストそのものを substring 検索
        private static readonly Dictionary<string, string?> ExpectedRibbonTabs = new()
        {
            ["表示"] = null,
            ["解析条件/解析"] = "解析条件",
            ["解析結果"] = null,
            ["ツール"] = null,
            ["ウィンドウ"] = null,
            ["ヘルプ"] = null,
        };

        // 主要ウィンドウの Title 抜粋。これらは help.html に章 / 節として記載必須
        private static readonly Dictionary<string, string?> ExpectedWindowTitles = new()
        {
            ["水平解析"] = null,
            ["沈下"] = null,        // SettlementWindow 系
            ["杭体"] = null,        // PileBodyWindow
            ["杭頭"] = null,        // PileTopWindow
            ["杭断面"] = null,      // PileSectionWindow
            ["地盤"] = null,        // GroundWindow
            ["要素分割"] = null,    // ElementDivisionWindow
            ["基礎梁"] = null,      // FoundationBeamWindow
            ["荷重ケース"] = null,  // LoadCaseWindow
            ["プロジェクト情報"] = null,  // ProjectInfoWindow
        };

        private static string GetHelpHtmlPath()
        {
            // テスト実行ディレクトリから solution root → Graphics_r1/Help/help.html へ辿る
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("help.html が見つかりません (Graphics_r1/Help/help.html)");
        }

        private static string LoadHelpHtmlContent()
        {
            return File.ReadAllText(GetHelpHtmlPath());
        }

        [TestMethod]
        public void HelpHtml_ContainsAllRibbonTabLabels()
        {
            var html = LoadHelpHtmlContent();
            var missing = new List<string>();

            foreach (var (label, keyword) in ExpectedRibbonTabs)
            {
                string searchTerm = keyword ?? label;
                if (!html.Contains(searchTerm))
                {
                    missing.Add($"  ・ リボンタブ「{label}」(検索キーワード「{searchTerm}」) が help.html に未記載");
                }
            }

            Assert.AreEqual(0, missing.Count,
                $"help.html に主要リボンタブの記述が不足:\n{string.Join("\n", missing)}\n" +
                $"→ Help/help.html に該当機能の説明を追加してください。");
        }

        [TestMethod]
        public void HelpHtml_ContainsAllMajorWindowTitles()
        {
            var html = LoadHelpHtmlContent();
            var missing = new List<string>();

            foreach (var (label, keyword) in ExpectedWindowTitles)
            {
                string searchTerm = keyword ?? label;
                if (!html.Contains(searchTerm))
                {
                    missing.Add($"  ・ 主要ウィンドウ「{label}」(検索キーワード「{searchTerm}」) が help.html に未記載");
                }
            }

            Assert.AreEqual(0, missing.Count,
                $"help.html に主要ウィンドウの記述が不足:\n{string.Join("\n", missing)}\n" +
                $"→ Help/help.html に該当ウィンドウの章 / 節を追加してください。");
        }

        [TestMethod]
        public void HelpHtml_RecentAdditionsAreDocumented()
        {
            // 最近追加した重要機能 (2026-05 セッション) が記載されているかをスポットチェック。
            // 追加機能を help.html に書き忘れない仕組みとして使う (機能追加と同時にここに行追加)
            var html = LoadHelpHtmlContent();
            var missing = new List<string>();

            // 各タプル: (機能名, 検索キーワード)
            var recentFeatures = new[]
            {
                ("P-S 非線形ばね", "P-S"),
                ("VL 単独ケース解析", "VL"),
                ("Chang サブプログラム", "Chang"),
                ("計算書出力 (docx)", "docx"),
            };

            foreach (var (feature, keyword) in recentFeatures)
            {
                if (!html.Contains(keyword))
                {
                    missing.Add($"  ・ 「{feature}」(キーワード「{keyword}」) が help.html に未記載");
                }
            }

            Assert.AreEqual(0, missing.Count,
                $"最近追加された機能の help.html 記載が不足:\n{string.Join("\n", missing)}");
        }
    }
}
