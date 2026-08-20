using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    // HorizontalCalculationViewModel partial: 梁の M-φ・杭頭 M-θ のセットアップ（ケース初期化・ステップ毎の軸力再解決・接線剛性更新）
    public partial class HorizontalCalculationViewModel
    {
        // 荷重ケースの代表軸力Nで PileSection.GetMPhi/MPhiRelationship を呼び、各梁にセット
        // こちらも安全なヘルパに統一（例外発生源を除去）
        // v6: InputModel.PileBodies ではなく SoilPile.PileBodySegments を使用
        private void SetupMPhiFromPileSectionForLoadCase(AnaModel model, LoadCase loadCase)
        {

            if (model == null)
            {
                return;
            }
            if (!loadCase.IsPileNonLinear)
            {
                return;
            }

            int totalBeams = model.Beams.Count;
            int skippedNoPileBody = 0;
            int skippedNoSoilPile = 0;
            int skippedInvalidSeg = 0;
            int skippedNoSection = 0;
            int skippedNoCurve = 0;
            int successCount = 0;

            // SoilPileをPileBodyNoでキャッシュ（同じPileBodyNoを持つ最初のSoilPileを使用）
            var soilPileByPileBodyNo = new Dictionary<int, SoilPile>();
            if (InputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in InputModel.ElementDivision.SoilPiles)
                {
                    if (sp.PileBodyNo > 0 && !soilPileByPileBodyNo.ContainsKey(sp.PileBodyNo))
                    {
                        soilPileByPileBodyNo[sp.PileBodyNo] = sp;
                    }
                }
            }

            // PileLayoutDataItemをPileBodyNoでキャッシュ（軸力取得用）
            // 注: 複数のPileが同じPileBodyNoを使用する場合は代表値（最初のもの）を使用
            var pileByPileBodyNo = new Dictionary<int, PileLayoutDataItem>();
            if (InputModel.PileLayoutItems != null)
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    if (pile.PileBodyNo > 0 && !pileByPileBodyNo.ContainsKey(pile.PileBodyNo))
                    {
                        pileByPileBodyNo[pile.PileBodyNo] = pile;
                    }
                }
            }

            foreach (var beam in model.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                {
                    skippedNoPileBody++;
                    continue;
                }

                // SoilPileを取得（PileBodyNoで検索）
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile))
                {
                    skippedNoSoilPile++;
                    continue;
                }

                // SoilPile.PileBodySegments を使用（杭要素分割後のセグメント）
                if (seg < 0 || seg >= soilPile.PileBodySegments.Count)
                {
                    skippedInvalidSeg++;
                    continue;
                }

                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null)
                {
                    skippedNoSection++;
                    continue;
                }

                // 軸力を取得（PileLayoutItemから）
                // 注: pile.AxialForce / model.GetAxialForce は kN 単位で格納されている
                //     (UI 入力 (kN), SetAxialForce コメント [kN], AxialForceLevel{1,2}s [kN] と整合)。
                //     PileSection.GetMPhiRelationship は kN を期待 (内部で *1000 して N に変換)。
                //     旧実装は誤って /1000.0 で「N→kN 変換」していたため、軸力が 1/1000 で
                //     M-φ が 24% 程度過小評価される単位バグがあった (検証テスト: PileSectionMPhiUnitTests)。
                // 初期セットアップではケース固有の入力軸力 (AxialForceLevel{1,2}s) を優先。
                // (per-step の SetupMPhiByCurrentAxialForMiddleBeam がステップごとに再解決するため、
                //  ここでの値は step 0 の K 行列構築時に効く)
                double axialN_kN = 0.0;
                if (pileByPileBodyNo.TryGetValue(pb, out var pile))
                {
                    try
                    {
                        double nSeis = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                        if (double.IsFinite(nSeis) && nSeis != 0.0)
                            axialN_kN = nSeis;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[SetupMPhi] GetSeismicAxialForce(loadCaseNo={No}, level={Lv}) failed, fallback to gravity baseline.",
                            loadCase.No, loadCase.Level);
                    }
                    if (axialN_kN == 0.0)
                    {
                        axialN_kN = model.GetAxialForce(pile); // kN フォールバック (重力ベース)
                    }
                }

                // M-φ 曲線の解決（場所打ち鋼管コンクリート杭の杭中間部特別扱いを含む共通ロジック）
                var curve = Services.MphiCurveResolver.Resolve(section, axialN_kN, beam.IsPileTop);

                if (curve is null)
                {
                    skippedNoCurve++;
                    continue;
                }

                beam.SetResolvedCombinedMPhi(curve.Value.Phis, curve.Value.Moments);
                successCount++;
            }

        }

        // M-φ 曲線の解決は Services.MphiCurveResolver に集約（初期セットアップ／ステップ毎再解決で共用）。

        /// <summary>
        /// 荷重ケース用の M-θ セットアップ（ケース開始時）。
        /// 非線形 ON/OFF に応じて線形 K を必ず設定し、曲線は ON 時のみ使用する。
        /// クラック履歴をリセットし、曲線は入力の地震時軸力で構築する
        /// （直後の SnapshotMThetaToOriginalSprings がこの構成を表示用に記録するため、
        ///  画面・計算書には設計軸力ベースの M-θ が出る。解析中の曲線は
        ///  <see cref="UpdateMThetaByCurrentAxialForLoadCase"/> がステップ毎に作り直す）。
        /// </summary>
        // internal はテスト用 (MThetaAxialForceTests)
        internal void SetupNonlinearMThetaForLoadCase(AnaModel model, LoadCase loadCase)
            => ApplyMThetaForLoadCase(model, loadCase, resetCrackState: true, useSeismicAxialForce: true);

        /// <summary>
        /// 現ステップの軸力で杭頭 M-θ を作り直す（ステップ毎、M-φ の再解決と対）。
        ///
        /// 杭軸力は解析中に動く。SetVectorDF が設定した
        /// <c>AxialForceIncrement = (N_seis − VL)/nStep</c> を UpdateF が毎ステップ加算するので、
        /// <c>model.GetAxialForce(pile)</c> は VL から入力地震時軸力までの荷重ステップ比例のランプになる。
        /// 杭体の M-φ はこれに追随していたが、杭頭 M-θ はケース開始時の 1 回きりで満載時の
        /// 地震時軸力に固定されており、序盤のステップで杭体と杭頭が別の軸力の断面として振る舞っていた
        /// (2026-08-21 修正。Example10 の杭頭で step 1 の Mcr を 864 → 715 と 17% 過小に見ていた)。
        ///
        /// クラック履歴 (HasCrackedXY / CrackNx,Ny / ThetaProjMax) は保持する。
        /// ここでリセットすると毎ステップ未クラックに戻り、ヒステリシスが成立しない。
        /// </summary>
        // internal はテスト用 (MThetaAxialForceTests)
        internal void UpdateMThetaByCurrentAxialForLoadCase(AnaModel model, LoadCase loadCase)
            => ApplyMThetaForLoadCase(model, loadCase, resetCrackState: false, useSeismicAxialForce: false);

        private void ApplyMThetaForLoadCase(
            AnaModel model, LoadCase loadCase, bool resetCrackState, bool useSeismicAxialForce)
        {
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0) return;

            const double KMin = 1e-6;   // 特異化回避用の下限
            const double KBig = 1e10;   // 剛体相当（杭断面 4EI/L ≈ 1e8 に対して十分大きい値）

            foreach (var spring in model.RotationalSprings)
            {
                // v28: 各ケースの setup 時にクラック履歴をリセット (ケース間独立)
                if (resetCrackState) spring.ResetCrackState();

                int pb = (spring.PileBodyNo is int v && v > 0) ? v : 1;
                if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

                // 回転バネの名前から杭番号を抽出して軸力を取得
                // 名前形式: "RθXY-{pileNo}"
                // L1/L2 地震ケースでは「地震時軸力」(GetSeismicAxialForce) を使うべき。
                // pile.AxialForce は重力のみのベース軸力で、L2 の鉛直地震成分や上部構造慣性力
                // による軸力増分が反映されないため、M-θ 曲線が誤った (低めの) N で構築される。
                // GraphViewModel の popup と同じ優先順位に揃える:
                //   GetSeismicAxialForce (case/level 別) → model.GetAxialForce (重力ベースフォールバック)
                double axialN = 0.0;
                if (spring.Name != null && spring.Name.Contains('-'))
                {
                    var parts = spring.Name.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                    {
                        var pile = InputModel.PileLayoutItems?.FirstOrDefault(p => p.No == pileNo);
                        if (pile != null)
                        {
                            if (useSeismicAxialForce)
                            {
                                try
                                {
                                    double nSeis = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0)
                                        axialN = nSeis;
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, "[SetupMTheta] GetSeismicAxialForce(loadCaseNo={No}, level={Lv}) failed, fallback to gravity baseline.",
                                        loadCase.No, loadCase.Level);
                                }
                            }
                            if (axialN == 0.0)
                            {
                                // ステップ毎の再解決はこちら。荷重ステップ比例のランプ (VL → 地震時軸力)。
                                // E3b: case-local AxialForce 経由 (主モデルでは pile.AxialForce と同値)
                                axialN = model.GetAxialForce(pile); // kN
                            }
                        }
                    }
                }

                var pileBody = InputModel.PileBodies[pb - 1];
                // ステップ毎に呼ぶため、(杭体, 軸力) でキャッシュする。
                // 軸力は 1kN に丸め、キーと計算の両方で同じ丸め値を使う
                // (丸めた値をキーにしながら生の値で計算すると、同じキーに入る曲線が
                //  「最初にそのキーを作った軸力」次第で変わり結果が実行履歴に依存する)。
                axialN = QuantizeAxialNForMTheta(axialN);
                var def = GetMThetaRelationshipCached(pb, pileBody, axialN);

                // Serilog.Log.Debug(
                //     $"[SetupMTheta] {spring.Name}: IsPileNonLinear={loadCase.IsPileNonLinear}, " +
                //     $"def.Mode={def.Mode}, axialN={axialN:F1}kN");

                // 非線形OFF: つねに剛体相当
                if (!loadCase.IsPileNonLinear)
                {
                    spring.Mode = RotationalSpringMode.CombinedXY;
                    spring.CurveXY = null;
                    spring.KthetaXY = KBig;
                    spring.McrXY = null; // Mode 切替は非線形ケースでのみ有効
                    spring.LastSetupReason = $"Rigid(IsPileNonLinear=false, axialN={axialN:F0}kN)";
                    continue;
                }

                // 非線形ON
                switch (def.Mode)
                {
                    case PileHeadRotationMode.Rigid:
                        // 非線形ONでも「剛」は剛のまま扱う
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = null;
                        spring.KthetaXY = KBig;
                        spring.LastSetupReason = $"Rigid(def.Mode=Rigid, PileTop='{pileBody.PileTopType}', PileBody='{pileBody.PileBodyType}', axialN={axialN:F0}kN)";
                        break;

                    case PileHeadRotationMode.CombinedXY:
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = def.CurveXY;
                        spring.LastSetupReason = $"CombinedXY({(def.CurveXY != null ? def.CurveXY.Points.Count + "pts" : "null")}, Mcr={(def.McrXY?.ToString("F0") ?? "null")}, axialN={axialN:F0}kN)";
                        // v28: Mcr 同期 Mode 切替 (ヒステリシス付き) 用。場所打ち RC 杭のみ非 null。
                        spring.McrXY = def.McrXY;
                        // 状態はケース開始時にリセットするため念のためクリア
                        // (ステップ毎の再解決では履歴を保持する — ここで消すとヒステリシスが壊れる)
                        if (resetCrackState) spring.ResetCrackState();
                        // sec 側の代替として KThetaXY を設定（優先順位: def.KThetaXY → Mcr 有りなら KBig → 曲線の初期接線 → KMin）
                        if (def.KthetaXY.HasValue && def.KthetaXY.Value > 0.0)
                        {
                            spring.KthetaXY = def.KthetaXY;
                        }
                        else if (def.McrXY.HasValue)
                        {
                            // Mcr 同期 Mode 切替が有効 → 未クラック時は剛 (KBig) 扱いで開始
                            spring.KthetaXY = KBig;
                        }
                        else if (spring.CurveXY != null)
                        {
                            double k0 = Math.Max(spring.CurveXY.EvaluateTangent(1e-6), 0.0);
                            spring.KthetaXY = Math.Max(k0, KMin);
                        }
                        else
                        {
                            spring.KthetaXY = KMin;
                        }
                        // Serilog.Log.Debug(
                        //     $"[SetupMTheta] {spring.Name}: → CombinedXY, CurveXY={(spring.CurveXY != null ? $"{spring.CurveXY.Points.Count}pts" : "null")}, " +
                        //     $"KthetaXY={spring.KthetaXY:E3}");
                        break;

                    case PileHeadRotationMode.Separate:
                        spring.Mode = RotationalSpringMode.SingleDof;
                        if (spring.Dof == RotationalDof.Rx)
                        {
                            spring.Curve = def.CurveX;
                            if (def.Kx.HasValue && def.Kx.Value > 0.0)
                            {
                                spring.Ktheta = def.Kx;
                            }
                            else if (spring.Curve != null)
                            {
                                double k0 = Math.Max(spring.Curve.EvaluateTangent(1e-6), 0.0);
                                spring.Ktheta = Math.Max(k0, KMin);
                            }
                            else
                            {
                                spring.Ktheta = KMin;
                            }
                        }
                        else if (spring.Dof == RotationalDof.Ry)
                        {
                            spring.Curve = def.CurveY;
                            if (def.Ky.HasValue && def.Ky.Value > 0.0)
                            {
                                spring.Ktheta = def.Ky;
                            }
                            else if (spring.Curve != null)
                            {
                                double k0 = Math.Max(spring.Curve.EvaluateTangent(1e-6), 0.0);
                                spring.Ktheta = Math.Max(k0, KMin);
                            }
                            else
                            {
                                spring.Ktheta = KMin;
                            }
                        }
                        break;
                }

                if (spring.CurveXY != null && spring.CurveXY.Points.Count > 0)
                {
                    var pts = spring.CurveXY.Points;
                }
            }
        }

        // 杭頭 M-θ 曲線のキャッシュ。場所打ち RC 杭では断面の完全 M-θ 解析が走って重く、
        // ステップ毎 × 全ばね で呼ぶため (杭体No, 軸力[kN]) で再利用する。
        // 解析 1 回ごとに ClearMThetaCurveCache() でクリアする
        // (ConcreteModelOptions など曲線に効く設定が実行間で変わり得るため)。
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int PileBodyNo, long AxialN), FEM.PileHeadRotationDef>
            _mThetaCurveCache = new();

        internal void ClearMThetaCurveCache() => _mThetaCurveCache.Clear();

        /// <summary>M-θ キャッシュで用いる軸力の量子化 [kN]。キーと計算で同じ値を使うこと。</summary>
        private static double QuantizeAxialNForMTheta(double axialN)
            => double.IsFinite(axialN) ? Math.Round(axialN) : axialN;

        private FEM.PileHeadRotationDef GetMThetaRelationshipCached(
            int pileBodyNo, PileBodyInput pileBody, double axialN)
        {
            if (!double.IsFinite(axialN)) return pileBody.GetMThetaRelationship(axialN);
            return _mThetaCurveCache.GetOrAdd(
                (pileBodyNo, (long)axialN), _ => pileBody.GetMThetaRelationship(axialN));
        }

        /// <summary>
        /// Y 案: caseModel (DeepCopy 済) のばね M-θ 構成を、永続側 targetModel の
        /// 同インデックスばねの CaseMThetaSnapshots 辞書へ書き戻す。
        /// SetupNonlinearMThetaForLoadCase 直後に呼ぶ。
        /// 同じ (LoadCase, LoadCombination, IsLiquefaction) で再試行が走った場合は上書き。
        /// </summary>
        private static void SnapshotMThetaToOriginalSprings(
            FEM.AnaModel caseModel,
            FEM.AnaModel targetModel,
            Models.InputData.LoadCase loadCase,
            Models.InputData.LoadCombination loadCombination,
            bool isLiquefaction)
        {
            var src = caseModel?.RotationalSprings;
            var dst = targetModel?.RotationalSprings;
            if (src == null || dst == null) return;
            int n = Math.Min(src.Count, dst.Count);
            string key = FEM.RotationalSpring.MakeCaseKey(
                loadCase?.LoadName, loadCombination?.No ?? 0, isLiquefaction);
            for (int i = 0; i < n; i++)
            {
                var s = src[i];
                var d = dst[i];
                d.CaseMThetaSnapshots[key] = new FEM.MThetaCaseSnapshot
                {
                    Mode = s.Mode,
                    CurveXY = s.CurveXY,
                    Curve = s.Curve,
                    KthetaXY = s.KthetaXY,
                    Ktheta = s.Ktheta,
                    McrXY = s.McrXY,
                    SetupReason = s.LastSetupReason ?? "",
                };
            }
        }

        // 接線剛性用: 端部回転から要素中央曲率を評価し、dM/dφ を EI_eff として KTan（倍率）に反映
        // useRelaxation=false: Full NR（正確なヤコビアンで2次収束）
        // useRelaxation=true:  Modified NR の初期反復（安定化のためダンピング）
        private static void UpdateBeamMPhiTangent(AnaModel model, bool useRelaxation = false)
            => UpdateBeamMPhi(model, isTangent: true, useRelaxation: useRelaxation);

        // 割線剛性用（必要なら接線と同手順でKsecも更新）
        private static void UpdateBeamMPhiSecant(AnaModel model) => UpdateBeamMPhi(model, isTangent: false);

        // 統合されたM-φ更新メソッド: 接線剛性と割線剛性の両方に対応
        private static void UpdateBeamMPhi(AnaModel model, bool isTangent, bool useRelaxation = false)
        {
            int beamIdx = 0;
            foreach (var beam in model.Beams)
            {
                beamIdx++;
                bool hasCurve = beam.ResolvedCombinedCurve != null;
                bool hasMaterial = beam.Section?.Material != null;
                if (beamIdx <= 3)  // 最初の3本だけ詳細出力
                {
                }
                // 端部変位（全体）→要素座標系
                var dI = beam.NodeI.CumulativeDisp.GetVector();
                var dJ = beam.NodeJ.CumulativeDisp.GetVector();
                var disp = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(dI.Count + dJ.Count);
                disp.SetSubVector(0, dI.Count, dI);
                disp.SetSubVector(dI.Count, dJ.Count, dJ);

                var T = PileDesign.FEM.Utils.GetTransformMatrix(beam.NodeI, beam.NodeJ);
                var d = T * disp;

                // 端部回転（要素座標）: Ry(i)=d[4], Rz(i)=d[5], Ry(j)=d[10], Rz(j)=d[11] とみなす
                double thetaYi = d[4], thetaYj = d[10];
                double thetaZi = d[5], thetaZj = d[11];
                double L = Math.Max(beam.Length, 1e-12);

                double phiY = (thetaYj - thetaYi) / L;
                double phiZ = (thetaZj - thetaZi) / L;

                // 接線剛性の場合は dM/dφ、割線剛性の場合は M/φ を使用
                var (EIy_eff, EIz_eff) = isTangent
                    ? beam.EvaluateEIeff(phiY, phiZ)
                    : beam.EvaluateEIeffSecant(phiY, phiZ);

                // 初期 EI（断面から計算）
                double EI0y = beam.Section.Material.E * beam.Section.IY;
                double EI0z = beam.Section.Material.E * beam.Section.IZ;

                // 曲線の初期接線剛性を基準にして ratio を計算
                // これにより、φ→0 では ratio=1.0 となり、曲率増加で ratio<1.0 となる
                double EI_base = beam.InitialCurveTangent;
                bool useCurveBase = (EI_base > 1e-6);

                // 倍率に変換（数値安定化のため上下限）
                // v10: 下限を 0.05 (5%) に設定して数値安定性を確保
                // 長い杭・多要素の場合に剛性が低すぎると振動の原因になる
                const double RATIO_MIN = 0.05;
                double ratioY, ratioZ;

                // デバッグ: E*I と EI_base の比較（初回のみ）
                if (beamIdx == 0 && isTangent)
                {
                }

                // v10: 常にE*Iを基準にしてratioを計算
                // SetKeでは EI_used = E*I * ratio なので、
                // ratio = EI_sec / E*I とすることで EI_used = EI_sec となる
                ratioY = (double.IsNaN(EIy_eff) || EI0y <= 0) ? 1.0 : Math.Clamp(EIy_eff / EI0y, RATIO_MIN, 1.0);
                ratioZ = (double.IsNaN(EIz_eff) || EI0z <= 0) ? 1.0 : Math.Clamp(EIz_eff / EI0z, RATIO_MIN, 1.0);

                // 要素中央の曲率を保存（合成値）- 接線/割線の両方で更新
                double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
                beam.CurrentCurvature = phiRes;

                // 要素中央のモーメント（M-φ曲線から直接評価）
                if (beam.ResolvedCombinedCurve != null)
                {
                    beam.CurrentMoment = beam.ResolvedCombinedCurve.EvaluateMoment(phiRes);
                    // v28 問題 A 診断: M-φ セグメントインデックス (接線更新時のみ、毎反復 1 回記録)
                    if (isTangent)
                    {
                        beam.CurrentMPhiSegmentIndex = beam.ResolvedCombinedCurve.GetSegmentIndex(phiRes);
                    }
                }

                if (isTangent)
                {
                    if (useRelaxation)
                    {
                        // Modified NR の初期反復: 緩和係数で安定性を確保
                        const double RELAXATION = 0.3;
                        double prevKy = beam.KTan_y;
                        double prevKz = beam.KTan_z;
                        double newKy = (prevKy > 0.01) ? prevKy * (1 - RELAXATION) + ratioY * RELAXATION : ratioY;
                        double newKz = (prevKz > 0.01) ? prevKz * (1 - RELAXATION) + ratioZ * RELAXATION : ratioZ;
                        beam.KTan_y = newKy;
                        beam.KTan_z = newKz;
                    }
                    else
                    {
                        // Full NR: 正確なヤコビアン（2次収束に必要）
                        beam.KTan_y = ratioY;
                        beam.KTan_z = ratioZ;
                    }
                    beam.SetKe(true); // KeTan 再構築
                }
                else
                {
                    // 割線剛性: 緩和なし（正確な値を使用）
                    // M(φ)/φ は常に正値で滑らかに変化するため、緩和は不要。
                    // 緩和(0.5)は内力の不正確さを生み、大変形時の収束を著しく遅延させる。
                    beam.KSec_y = ratioY;
                    beam.KSec_z = ratioZ;
                    beam.SetKe(false); // KeSec 再構築
                }
            }
        }

        //private static (IList<double> Phis, IList<double> Moments)? TryCallMPhiRelationship(object pileSection, double axialN)
        //{
        //    if (pileSection == null) return null;
        //    var t = pileSection.GetType();
        //    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        //    // 修正: 小文字p→大文字P の順でフォールバック
        //    var mi = t.GetMethod("GetMPhiRelationship", flags)
        //          ?? t.GetMethod("GetMPhiRelationship", flags);
        //    if (mi == null) return null;

        //    object? ret;
        //    try
        //    {
        //        ret = mi.GetParameters().Length switch
        //        {
        //            1 => mi.Invoke(pileSection, new object[] { axialN }),
        //            2 => mi.Invoke(pileSection, new object[] { axialN, 1.0 }),
        //            _ => null
        //        };
        //    }
        //    catch { return null; }
        //    if (ret == null) return null;

        //    var rt = ret.GetType();
        //    var item1 = rt.GetProperty("Item1")?.GetValue(ret) as System.Collections.IEnumerable;
        //    var item2 = rt.GetProperty("Item2")?.GetValue(ret) as System.Collections.IEnumerable;
        //    if (item1 == null || item2 == null) return null;

        //    var phis = item1.Cast<object>().Select(Convert.ToDouble).ToList();
        //    var ms = item2.Cast<object>().Select(Convert.ToDouble).ToList();
        //    if (phis.Count >= 2 && phis.Count == ms.Count) return (phis, ms);
        //    return null;
        //}

        // 現ステップの「各杭の軸力」を用いて、対応する全梁の M–φ（合成）を解決してセット
        private void SetupMPhiByCurrentAxialForMiddleBeam(AnaModel model)
        {
            if (model == null) return;

            // SoilPileをPileBodyNoでキャッシュ（初期M-φ設定と同じマッチ済みセグメントを使用）
            var soilPileByPileBodyNo = new Dictionary<int, SoilPile>();
            if (InputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in InputModel.ElementDivision.SoilPiles)
                {
                    soilPileByPileBodyNo.TryAdd(sp.PileBodyNo, sp);
                }
            }

            foreach (var pile in InputModel.PileLayoutItems)
            {
                // 現ステップの軸力 [kN]。
                //
                // ここは「常時軸力 VL 固定」ではない。SetVectorDF が
                //     AxialForceIncrement = (地震時軸力 - VL) / nStep
                // をケース開始時に設定し、UpdateF が毎ステップ加算するため、
                // model.GetAxialForce(pile) は荷重ステップに比例したランプになる:
                //     step k (0 始まり) では VL + (k+1)(N_seis - VL)/nStep
                //     → 最終ステップでちょうど入力の地震時軸力に一致する。
                // (実測 Example10 L2 / 16 step: 3461.6 → ... → 2465.0、VL=3528・N_seis=2465)
                // つまり入力した地震時軸力は反映済みで、しかも荷重レベルと整合している。
                // 「入力値＋応力解析結果」モードではさらに UpdateAxialForceFromAnalysis が
                // 解析 Fxi を上乗せした現在軸力になる。
                //
                // 2026-08-21: これを「VL 固定」と読み違えて常に N_seis を使う変更を入れたが、
                // 荷重レベルと軸力が不整合になるため revert した。M-θ 側はケース内 N_seis 固定
                // なので揃っていないが、揃えるなら M-θ をランプ化する方向。
                //
                // 単位: pile.AxialForce / model.GetAxialForce は kN (UI 入力, SetAxialForce コメント,
                // AxialForceLevel{1,2}s 全て kN)。PileSection.GetMPhiRelationship も kN を期待。
                // 旧実装は誤って /1000.0 で「N→kN 変換」していたため、軸力が 1/1000 で
                // M-φ が 24% 程度過小評価される単位バグがあった (検証: PileSectionMPhiUnitTests)。
                double axialN_kN = model.GetAxialForce(pile);

                int pb = pile.PileBodyNo;
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) continue;

                foreach (var beam in model.GetPileBeams(pile))
                {
                    if (beam.SegmentIndex is not int seg) continue;
                    // SoilPile.PileBodySegments はマッチ済み（要素ごとに1エントリ）
                    if (seg < 0 || seg >= soilPile.PileBodySegments.Count) continue;

                    var section = soilPile.PileBodySegments[seg].PileSection;
                    if (section == null) continue;

                    // M-φ 曲線の解決（初期セットアップと同じ共通ロジック。null なら前回曲線を維持）
                    var curve = Services.MphiCurveResolver.Resolve(section, axialN_kN, beam.IsPileTop);

                    if (curve is null) continue;
                    beam.SetResolvedCombinedMPhi(curve.Value.Phis, curve.Value.Moments);
                }
            }
        }

    }
}
