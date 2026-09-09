using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 入力モデルの複製が、設定項目を写し忘れていないこと。
    ///
    /// <c>InputModel.DeepCopy</c> は元に戻す操作の土台で、写し忘れた項目は
    /// <b>元に戻した瞬間に既定値へ落ちる</b>。手書きなので足し忘れが起きやすい。
    /// 「元と複製を直列化した結果が一致する」ことで機械的に確かめる。
    /// 実際に杭先端の P-S 非線形ばねと VL 単独解析の 2 つが写されていなかった。
    ///
    /// <b>この複製は保存には使えない。</b> 場所打ち杭の土層-杭セットが持つ地盤への参照は、
    /// 地盤側とは別の経路 (JSON 往復) で複製されるため、元では 1 つだった実体が
    /// 複製では 2 つに分かれる。保存に使うとファイルの形 ($ref の畳まれ方) が変わる。
    /// 保存中の編集から守る話は、この複製とは別の手立てが要る。
    /// </summary>
    [TestClass]
    public class DeepCopyIsSaveEquivalentTests
    {
        /// <summary>保存と同じ直列化設定。</summary>
        private static JsonSerializerOptions SaveOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static readonly Regex _idLineRegex = new("\"Id\": \\d+", RegexOptions.Compiled);
        private static string NormalizeRuntimeIds(string json) => _idLineRegex.Replace(json, "\"Id\": *");

        private static void AssertSaveEquivalent(InputModel original, string what)
        {
            var copy = original.DeepCopy();

            // InputNode.Id は読込のたびに振り直される runtime-only な番号で、
            // 永続識別子は UniqueId。ここでは比較の対象外にする
            // (SaveLoadRoundTripTests と同じ扱い)。
            string a = NormalizeRuntimeIds(JsonSerializer.Serialize(original, SaveOptions()));
            string b = NormalizeRuntimeIds(JsonSerializer.Serialize(copy, SaveOptions()));

            if (a == b) return;

            var linesA = a.Split('\n');
            var linesB = b.Split('\n');
            var diffs = new List<string>();
            for (int i = 0; i < Math.Min(linesA.Length, linesB.Length) && diffs.Count < 10; i++)
            {
                if (linesA[i] != linesB[i])
                    diffs.Add($"  元 {linesA[i].Trim()} / 複製 {linesB[i].Trim()}");
            }

            Assert.Fail(
                $"{what}: 複製が保存の観点で元と違う。保存のたびにこの項目が失われる。" + Environment.NewLine
                + string.Join(Environment.NewLine, diffs)
                + (linesA.Length != linesB.Length
                    ? Environment.NewLine + $"  行数も違う 元={linesA.Length} 複製={linesB.Length}"
                    : ""));
        }

        /// <summary>まっさらな入力。</summary>
        [TestMethod]
        public void ANewModel_CopiesIdentically()
        {
            AssertSaveEquivalent(new InputModel(), "新規の入力");
        }

        /// <summary>
        /// 既定と違う値を入れた入力。
        ///
        /// 既定のままだと「写し忘れた項目が偶然一致する」ので検査にならない。
        /// bool は反転させ、数値と文字列も動かす。
        /// </summary>
        [TestMethod]
        public void AModelWithNonDefaultFlags_CopiesIdentically()
        {
            var input = new InputModel
            {
                UseAnalysisAxialForce = true,
                IsAxialForceVariationMode = true,
                UsePsSpringAtPileTip = true,
                IsVLAnalysisEnabled = true,
            };
            if (input.FundamentalInput != null)
            {
                input.FundamentalInput.ReferenceAltitude = 12.5;
                input.FundamentalInput.SeismicGrade = "S";
            }

            AssertSaveEquivalent(input, "既定と違う値を入れた入力");
        }

        /// <summary>
        /// コレクションが未設定でも複製が落ちないこと。
        ///
        /// 荷重ケースの複製は 6 つのコレクションを無条件に辿っており、
        /// いずれかが null だと例外になっていた。形は保ったまま (null は null のまま) 通す。
        /// </summary>
        [TestMethod]
        public void MissingCollections_DoNotThrow()
        {
            var input = new InputModel();
            if (input.LoadCasesInput != null)
            {
                input.LoadCasesInput.LoadCombinationsPlus = null!;
                input.LoadCasesInput.LoadCasesLevel2 = null!;
            }

            var copy = input.DeepCopy();   // 例外が出ないこと

            Assert.IsNotNull(copy, "複製できない");
            Assert.IsNull(copy.LoadCasesInput?.LoadCombinationsPlus,
                "未設定のコレクションが空リストに置き換わっている (保存ファイルの中身が変わる)");
        }

    }
}
