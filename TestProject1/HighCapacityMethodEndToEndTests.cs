using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// メーカー別高支持力杭工法（Smart-MAGNUM / Hybrid ニーディング）の通し検証。
    ///
    /// 単体テストは式ごとに固めてあるが、実際の地盤データを載せて
    /// 要素分割 → 支持力 → 単杭沈下解析 → docx 出力 まで通したことがなかった。
    /// ここでは例題の地盤をそのまま使い、杭体だけを各工法の構成に差し替えて
    /// 一連の経路が完走し、値が破綻しないことを確認する。
    ///
    /// 目視が要る項目（docx の体裁・3D 表示）は自動化できないので、
    /// ここでは「例外なく生成され、値が有限で符号が正しい」ところまでを見る。
    /// </summary>
    [TestClass]
    public class HighCapacityMethodEndToEndTests
    {
        // ── 支持力 → 沈下 ────────────────────────────────────────

        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.SmartMagnum)]
        [DataRow(PileConstructionTypeNames.HybridKneading)]
        public void BearingCapacity_IsFiniteAndConsistentWithLimitStates(string constructionType)
        {
            var model = BuildModel(constructionType, out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            var soilPiles = model.ElementDivision.SoilPiles;
            Assert.IsTrue(soilPiles.Count > 0, "SoilPiles が生成されていない");

            foreach (var sp in soilPiles)
            {
                string label = $"{constructionType} 杭体{sp.PileBodyNo}×地盤{sp.GroundNo}";

                Assert.IsTrue(double.IsFinite(sp.Qpu) && sp.Qpu > 0, $"{label}: Qpu={sp.Qpu}");
                Assert.IsTrue(double.IsFinite(sp.ApBearing) && sp.ApBearing > 0, $"{label}: ApBearing={sp.ApBearing}");
                Assert.IsTrue(double.IsFinite(sp.Rpu) && sp.Rpu > 0, $"{label}: Rpu={sp.Rpu}");
                Assert.IsTrue(double.IsFinite(sp.Rfu) && sp.Rfu > 0, $"{label}: Rfu={sp.Rfu}");
                Assert.IsTrue(double.IsFinite(sp.Ru) && sp.Ru > 0, $"{label}: Ru={sp.Ru}");

                // 限界状態はカタログの 長期 Ru/3・短期 2Ru/3 と一致する既存構造をそのまま使う
                Assert.AreEqual(sp.Ru / 3.0, sp.R_SLS, Math.Abs(sp.Ru) * 1e-9, $"{label}: 使用限界");
                Assert.AreEqual(sp.Ru / 1.5, sp.R_DLS, Math.Abs(sp.Ru) * 1e-9, $"{label}: 損傷限界");
                Assert.AreEqual(sp.Ru, sp.R_ULS, Math.Abs(sp.Ru) * 1e-9, $"{label}: 終局限界");

                // 引抜きは負値で保持する規約
                Assert.IsTrue(double.IsFinite(sp.Rtu) && sp.Rtu < 0, $"{label}: Rtu={sp.Rtu}");

                // 先端面積は節部径基準。根固め部径 (D) 基準の Ap より小さいはず
                Assert.IsTrue(sp.ApBearing < sp.Ap,
                    $"{label}: 先端面積が節部径基準になっていない (ApBearing={sp.ApBearing}, Ap={sp.Ap})");
            }
        }

        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.SmartMagnum)]
        [DataRow(PileConstructionTypeNames.HybridKneading)]
        public void SettlementCurve_UsesTheMethodUltimateToeBearing(string constructionType)
        {
            var model = BuildModel(constructionType, out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            foreach (var sp in model.ElementDivision.SoilPiles)
            {
                // 沈下曲線の極限先端支持力に工法の値をそのまま使う (ユーザー指示)
                Assert.AreEqual(sp.Rpu, sp.SettleRpu, Math.Abs(sp.Rpu) * 1e-6,
                    $"{constructionType}: 沈下曲線の極限先端支持力が工法の値と一致しない");

                // 変位スケール 0.1·Dp の Dp は根固め部径
                Assert.AreEqual(sp.PileBodyInput.PileToeDia, sp.Dp, 1e-6,
                    $"{constructionType}: 沈下曲線の先端径が根固め部径になっていない");
            }
        }

        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.SmartMagnum)]
        [DataRow(PileConstructionTypeNames.HybridKneading)]
        public void SettlementAnalysis_RunsAndProducesAMonotonicCurve(string constructionType)
        {
            var model = BuildModel(constructionType, out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            var soilPile = model.ElementDivision.SoilPiles[0];
            var vtm = new VerticalLoadTransferMethod(model, soilPile);

            Assert.IsTrue(vtm.LoadDisplacements.Count > 2,
                $"{constructionType}: 荷重-沈下曲線のステップが少なすぎる ({vtm.LoadDisplacements.Count})");

            foreach (var p in vtm.LoadDisplacements)
            {
                Assert.IsTrue(double.IsFinite(p.F0s), $"{constructionType}: 荷重が非有限 {p.F0s}");
                Assert.IsTrue(double.IsFinite(p.D0s), $"{constructionType}: 沈下量が非有限 {p.D0s}");
            }

            // 符号規約: F0s > 0 が押込みで D0s > 0 (沈下)、F0s < 0 が引抜きで D0s < 0。
            // 荷重が大きいほど沈下も大きいので、F0s 昇順に並べると D0s も単調非減少になる。
            var curve = vtm.LoadDisplacements.OrderBy(p => p.F0s).ToList();
            double span = Math.Max(Math.Abs(curve[^1].D0s), Math.Abs(curve[0].D0s));
            double tol = Math.Max(span * 1e-6, 1e-6);

            double prev = double.NegativeInfinity;
            foreach (var p in curve)
            {
                Assert.IsTrue(p.D0s >= prev - tol,
                    $"{constructionType}: 荷重-沈下曲線が単調でない (F0s={p.F0s:N1} で {prev:N6} → {p.D0s:N6})");
                prev = p.D0s;
            }

            // 押込み・引抜きの両側が算定されていること
            Assert.IsTrue(curve.Any(p => p.F0s > 0), $"{constructionType}: 押込み側の記録が無い");
            Assert.IsTrue(curve.Any(p => p.F0s < 0), $"{constructionType}: 引抜き側の記録が無い");
        }

        // ── 適用範囲チェック ─────────────────────────────────────

        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.SmartMagnum)]
        [DataRow(PileConstructionTypeNames.HybridKneading)]
        public void RangeCheck_RunsWithoutThrowing(string constructionType)
        {
            var model = BuildModel(constructionType, out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            foreach (var sp in model.ElementDivision.SoilPiles)
            {
                // 例外なく列挙できること (警告の有無自体はモデル次第なので問わない)
                var smart = sp.ValidateSmartMagnumRange().ToList();
                var hybrid = sp.ValidateHybridKneadingRange().ToList();

                // 選んでいない工法の検査は必ず空
                if (PileConstructionTypeNames.IsSmartMagnum(constructionType))
                    Assert.AreEqual(0, hybrid.Count, "選択していない工法の警告が出ている");
                else
                    Assert.AreEqual(0, smart.Count, "選択していない工法の警告が出ている");
            }
        }

        // ── docx 出力 ────────────────────────────────────────────

        /// <summary>
        /// 計算書に工法の算定根拠表と杭姿図が入る。WordDocument は WPF の
        /// DrawingVisual / RenderTargetBitmap を使うため STA スレッドで走らせる。
        /// </summary>
        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.SmartMagnum)]
        [DataRow(PileConstructionTypeNames.HybridKneading)]
        [Timeout(600000)]
        public void DocxOutput_Succeeds(string constructionType)
        {
            Exception? threadEx = null;
            string? inconclusive = null;
            long size = 0;

            var thread = new Thread(() =>
            {
                try
                {
                    var model = BuildModel(constructionType, out string? skip);
                    if (model == null) { inconclusive = skip; return; }

                    model.FundamentalInput ??= new FundamentalInput();
                    var mainVm = new MainWindowViewModel { CurrentInputModel = model };
                    mainVm.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
                    mainVm.DocxOutput.CalculationReportLevel = 1;

                    string dir = Path.Combine(Path.GetTempPath(), "PileDesignHighCapacityDocx");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"{constructionType}.docx");
                    if (File.Exists(path)) File.Delete(path);

                    // 解析結果なし (入力のみ) の計算書。支持力表と算定根拠表はこの段階で出る
                    var doc = new PileDesign.Output.WordDocument(model, null, mainVm);
                    doc.CreateWordDocument(model, path);

                    var info = new FileInfo(path);
                    Assert.IsTrue(info.Exists, "docx が生成されていない");
                    size = info.Length;
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (inconclusive != null) { Assert.Inconclusive(inconclusive); return; }
            if (threadEx != null) Assert.Fail($"{constructionType} の docx 生成で例外:\n{threadEx}");
            Assert.IsTrue(size > 10_000, $"{constructionType}: docx が小さすぎる ({size} bytes)");
        }

        // ── モデル構築 ───────────────────────────────────────────

        /// <summary>
        /// 例題の地盤をそのまま使い、杭体だけを対象工法の構成に差し替える。
        /// 下杭を節杭にするのは、どちらの工法も先端が節杭である前提のため。
        /// </summary>
        private static InputModel? BuildModel(string constructionType, out string? skipReason)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (model == null) { skipReason = $"例題ロード失敗: {error}"; return null; }
            skipReason = null;

            bool isSmartMagnum = PileConstructionTypeNames.IsSmartMagnum(constructionType);

            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileConstructionType = constructionType,
                SettlePileToeDia = 1500,
                SettleAlpha = 0.3,
                SettleN = 2.0,
            };

            if (isSmartMagnum)
            {
                body.PileToeDia = 1900;              // 拡大根固め部径 Den
                body.SmartMagnumLL = 1.0;
                body.SmartMagnumDes = 1400;
                body.SmartMagnumWingLength = 10;
                body.SmartMagnumIsReinforcedCircum = false;
            }
            else
            {
                // Hybrid は根固め部径 D3 = e·D1 を導出して PileToeDia に書き戻す
                body.PileToeDia = 1500;
                body.HybridExpansionRatio = 1.5;
                body.HybridExcavationRatio = 1.0;
                body.HybridPileBelowLength = 0.5;
                body.HybridIsFrictionEnhanced = false;
            }

            // 元の杭体の区間長を引き継ぐ (地盤との重なりを壊さないため)
            var original = model.PileBodies[0];
            body.PileBodySegments.Clear();
            for (int i = 0; i < original.PileBodySegments.Count; i++)
            {
                body.PileBodySegments.Add(new PileBodySegment
                {
                    No = i + 1,
                    SegmentLength = original.PileBodySegments[i].SegmentLength,
                    PileSection = new PileSection(),
                });
            }
            body.PileBodySegmentsUpdate();

            // 断面を設定し直す。PileBodySegments の setter が親の杭体タイプを子へ同期し
            // ResetSectionProperties() で既定値に戻すため、代入後に行う必要がある
            var segments = body.PileBodySegments;
            for (int i = 0; i < segments.Count; i++)
            {
                bool isBottom = i == segments.Count - 1;
                var sec = segments[i].PileSection;
                sec.PileBodyType = PileTypeNames.PrecastConcrete;

                if (isBottom)
                {
                    // 下杭は節杭。メーカーは工法に合わせる (適用範囲チェックが見る)
                    sec.PileSectionType = isSmartMagnum ? PileTypeNames.PhcNodular : PileTypeNames.BfsTip;
                    sec.PileDiameter = 1100;
                    sec.NodeDiameter = 1200;
                    sec.NodeHeadOffset = 600;
                    sec.NodePitch = 1000;
                    sec.NodeToeOffset = 400;
                    sec.SelectedPrecastPile.Name = isSmartMagnum
                        ? "NPH-1200-1100-標準-85-A"
                        : "BF.S-1200-110130-105-A2";
                }
                else
                {
                    sec.PileSectionType = PileTypeNames.Phc;
                    sec.PileDiameter = 1100;
                    sec.SelectedPrecastPile.Name = isSmartMagnum
                        ? "PHC-1100-標準-80-A"
                        : "MS-hi105-1100-標準型-A";
                }
            }

            model.PileBodies = new ObservableCollection<PileBodyInput> { body };
            foreach (var item in model.PileLayoutItems) item.PileBodyNo = 1;

            model.GenerateSoilPiles();
            return model;
        }
    }
}
