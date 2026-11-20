using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭間隔比 R/B を考慮するかを明示して選ぶ (外部レビュー #14 2026-11-20)。
    ///
    /// <para>以前は R/B = 0 が「未入力」と「群杭の影響を考えない」の両方を表し、入力忘れと意図を区別できなかった。
    /// いまは <see cref="PileLayoutDataItem.ConsidersPileSpacing"/> で選ぶ。考慮するのに未入力なら解析の前に止め、
    /// 考慮しないなら R/B の値によらず単杭と同じ扱い。以前の保存ファイルは R/B = 0 を「考慮しない」として読み、
    /// 計算結果は変わらない。</para>
    /// </summary>
    [TestClass]
    public class PileSpacingConsiderationTests
    {
        // ── 設定の決まり方 ──

        /// <summary>
        /// 明示して選んでいない杭は R/B から決まる (R/B &gt; 0 なら考慮する)。考慮していた杭の R/B を消すと「考慮する」が
        /// 確定し、入れ忘れとして止まる (黙って考慮しないにならない)。明示して外した杭は、R/B を入れても外したまま。
        /// </summary>
        [TestMethod]
        public void UnchosenPilesFollowTheSpacing_AndClearingItKeepsConsidering()
        {
            var p = new PileLayoutDataItem();
            Assert.IsFalse(p.ConsidersPileSpacing, "新しい杭は R/B = 0 で考慮しない");

            p.PileSpacingFactor = 2.5;
            Assert.IsTrue(p.ConsidersPileSpacing, "R/B を入れたのに考慮しないまま");

            p.PileSpacingFactor = 0;
            Assert.IsTrue(p.ConsidersPileSpacing, "R/B を消したら黙って考慮しないになった (入れ忘れを捕まえられない)");

            var q = new PileLayoutDataItem { ConsidersPileSpacing = false };
            q.PileSpacingFactor = 3.0;
            Assert.IsFalse(q.ConsidersPileSpacing, "利用者が外した設定を、R/B の入力で戻している");
        }

        /// <summary>
        /// 読み込み (保存ファイルの項目をどの順に読んでも) で同じになること。R/B の設定で「考慮する」を確定させる規則は、
        /// R/B を正から 0 以下にしたときだけ働き、読み込みでは R/B は 0 から入るので働かない。
        /// </summary>
        [TestMethod]
        public void TheOrderOfSettingDoesNotMatter()
        {
            foreach (bool? chosen in new bool?[] { null, true, false })
                foreach (double spacing in new[] { 0.0, 2.5 })
                {
                    var a = new PileLayoutDataItem();
                    a.PileSpacingFactor = spacing;
                    if (chosen is bool c1) a.ConsidersPileSpacing = c1;
                    var b = new PileLayoutDataItem();
                    if (chosen is bool c2) b.ConsidersPileSpacing = c2;
                    b.PileSpacingFactor = spacing;
                    Assert.AreEqual(a.ConsidersPileSpacing, b.ConsidersPileSpacing, $"設定={chosen?.ToString() ?? "未選択"}, R/B={spacing} で順により違う");
                }
        }

        [TestMethod]
        public void TheAnalysisIgnoresTheSpacingWhenNotConsidered()
        {
            var p = new PileLayoutDataItem { GroupPileFactor = 0.8, PileSpacingFactor = 2.5 };
            Assert.AreEqual(2.5, GroupPileEffect.For(p).ROnB);
            p.ConsidersPileSpacing = false;
            Assert.AreEqual(0.0, GroupPileEffect.For(p).ROnB, "考慮しないのに R/B を解析に渡している");
            Assert.AreEqual(0.8, GroupPileEffect.For(p).Xi, "群杭係数 ξ はこの設定と関係なく使う");
        }

        [TestMethod]
        public void DeepCopyKeepsAnExplicitChoice()
        {
            var p = new PileLayoutDataItem { PileSpacingFactor = 2.5 };
            p.ConsidersPileSpacing = false;
            var copy = p.QuickClone();   // 元に戻す (Undo) の控えに使う写し
            Assert.AreEqual(2.5, copy.PileSpacingFactor);
            Assert.IsFalse(copy.ConsidersPileSpacing, "写すと R/B の設定で「考慮する」に戻った");
        }

        // ── 検査 ──

        [TestMethod]
        public void ConsideredButUnsetBlocks_NotConsideredDoesNot()
        {
            var m = new InputModel();
            m.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = m });
            m.PileLayoutItems ??= [];
            var a = new PileLayoutDataItem { No = 1, PileSpacingFactor = 2.0 };
            a.PileSpacingFactor = 0;                                    // 考慮するのに消した
            var b = new PileLayoutDataItem { No = 2 };                  // 考慮しない・R/B = 0
            var c = new PileLayoutDataItem { No = 3, PileSpacingFactor = 0.5 };
            c.ConsidersPileSpacing = false;                             // 考慮しないなら 1 未満でも使わない
            m.PileLayoutItems.Add(a); m.PileLayoutItems.Add(b); m.PileLayoutItems.Add(c);

            var problems = CheckInputData.CollectPileSpacingProblems(m);
            Assert.AreEqual(1, problems.Count, string.Join(" / ", problems.Select(x => x.Message)));
            Assert.AreEqual(1, problems[0].Target.PileNo);
            StringAssert.Contains(problems[0].Message, "考慮する設定ですが、R/B が入力されていません");
            Assert.IsFalse(problems[0].Message.Contains("ほかに"), "1 本なのに他の杭を並べている");

            c.ConsidersPileSpacing = true;
            Assert.AreEqual(2, CheckInputData.CollectPileSpacingProblems(m).Count, "考慮するなら 1 未満は止める");
        }

        // ── 保存ファイル ──

        /// <summary>
        /// 以前の版の保存ファイル (設定を持たない) は R/B = 0 を「考慮しない」、R/B &gt; 0 を「考慮する」として読む。
        /// 保存した設定 (R/B &gt; 0 で考慮しない) は往復で残る。
        /// </summary>
        [TestMethod]
        public void OldFilesReadTheFormerMeaning_AndAnExplicitChoiceSurvivesSaveAndLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var svc = new FileOperationService(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                });
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
                input.PileLayoutItems ??= [];
                var unset = new PileLayoutDataItem { No = 1 };
                var spaced = new PileLayoutDataItem { No = 2, PileSpacingFactor = 2.5 };
                var optedOut = new PileLayoutDataItem { No = 3, PileSpacingFactor = 3.0 };
                optedOut.ConsidersPileSpacing = false;
                input.PileLayoutItems.Add(unset); input.PileLayoutItems.Add(spaced); input.PileLayoutItems.Add(optedOut);

                string file = Path.Combine(dir, "spacing.pdj");
                svc.SaveProjectData(file, input, new PileDesign.FEM.AnaModel(), null);
                var loaded = svc.LoadProjectData(file).InputModel.PileLayoutItems;
                CollectionAssert.AreEqual(new[] { false, true, false }, loaded.Select(p => p.ConsidersPileSpacing).ToList(),
                    "保存した設定が往復で変わった");

                // 以前の版のファイル: 設定の項目が無い
                string text = FileOperationService.ReadSaveFileText(file);
                string old = Regex.Replace(text, "\\s*\"ConsidersPileSpacing\":\\s*(true|false),?", "");
                Assert.IsFalse(old.Contains("ConsidersPileSpacing"), "(前提) 設定の項目を消せていない");
                string oldFile = Path.Combine(dir, "old.pdj");
                File.WriteAllText(oldFile, old);
                var fromOld = svc.LoadProjectData(oldFile).InputModel.PileLayoutItems;
                CollectionAssert.AreEqual(new[] { false, true, true }, fromOld.Select(p => p.ConsidersPileSpacing).ToList(),
                    "以前のファイルの R/B = 0 を「考慮しない」、R/B > 0 を「考慮する」と読んでいない");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 後片付けの失敗は無視 */ }
            }
        }
    }
}
