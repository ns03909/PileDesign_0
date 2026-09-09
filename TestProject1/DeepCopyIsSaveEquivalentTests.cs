using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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


        // ───────── 保存用に写した器 ─────────

        /// <summary>
        /// 保存用に写した器が、直列化した結果まで<b>元と一致する</b>こと。
        ///
        /// 入れ物だけ新しくして要素は共有するので、参照の畳まれ方まで含めて同じになる。
        /// ここが崩れると、保存ファイルの中身が黙って変わる。
        /// </summary>
        [TestMethod]
        public void TheSaveSnapshot_SerializesIdenticallyForAnExample()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, "例題を読み込めない");

            var snapshot = input.SnapshotForSaving();

            string a = NormalizeRuntimeIds(JsonSerializer.Serialize(input, SaveOptions()));
            string b = NormalizeRuntimeIds(JsonSerializer.Serialize(snapshot, SaveOptions()));

            Assert.AreEqual(a, b, "保存用に写した器が元と違う結果になる。保存ファイルの中身が変わる");
        }

        /// <summary>
        /// 写した器は<b>入れ物が別</b>であること。これが保存中の編集から守る要。
        /// 中身の要素は同じ実体を指したままであること。
        /// </summary>
        [TestMethod]
        public void TheSaveSnapshot_HasItsOwnContainersButSharesTheItems()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, "例題を読み込めない");
            Assert.IsTrue(input.PileLayoutItems.Count > 0, "例題に杭が無い");

            var snapshot = input.SnapshotForSaving();

            Assert.AreNotSame(input.PileLayoutItems, snapshot.PileLayoutItems,
                "入れ物が同じ。保存中に杭を足し引きすると列挙が壊れる");
            Assert.AreSame(input.PileLayoutItems[0], snapshot.PileLayoutItems[0],
                "要素まで複製している。保存ファイルの参照の畳まれ方が変わる");
        }

        /// <summary>
        /// 写した器を辿っているあいだに元のコレクションを編集しても、列挙が壊れないこと。
        /// これが直したかった現象そのもの。
        /// </summary>
        [TestMethod]
        public void EditingDuringSerialization_DoesNotBreakTheSnapshot()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, "例題を読み込めない");

            // 例題は通り芯を持たないので、編集できる状態にしてから写す
            input.GridXItems ??= [];

            var snapshot = input.SnapshotForSaving();
            int before = snapshot.GridXItems.Count;

            // 直列化の最中に利用者が通り芯を足した状況。
            // (杭の追加は画面が要るのでここでは使えない。守る仕組みは入れ物単位で同じ)
            input.GridXItems.Add(new GridDataItem());

            Assert.AreEqual(before, snapshot.GridXItems.Count,
                "元を編集すると写した器まで動く。列挙が壊れる余地が残っている");

            string json = JsonSerializer.Serialize(snapshot, SaveOptions());   // 例外が出ないこと
            Assert.IsTrue(json.Length > 0, "写した器を直列化できない");
        }
    }
}
