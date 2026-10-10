using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    // PileSection のうち、M-φ 関係とそのキャッシュ、杭頭の M-θ。
    public partial class PileSection
    {
        // --- 追加: M-φキャッシュ（同一断面・同一軸力での再計算を抑制） ---
        // キーは断面プロパティハッシュ + 軸力(kN)を丸めた値
        private static readonly Dictionary<string, (List<double> Phis, List<double> Moments)> _mphiCache = [];
        private static readonly object _mPhiCacheLock = new();
        private static int _mPhiCacheHitCount = 0;
        private static int _mPhiCacheMissCount = 0;
        private static bool _mPhiCacheLimitLogged = false;

        /// <summary>
        /// M-φキャッシュ用のキーを生成します。
        /// 断面の種類と主要パラメータ + 軸力を組合せた文字列を返します。
        /// 軸力は1kN単位で丸めてキャッシュヒット率を向上させます。
        /// 注: このメソッドはGetMPhiRelationshipと同じ単位系（kN）を期待。
        /// internal はテスト用（MphiCacheKeyTests が「諸元 1 個の変更→キー変化」を検証し、
        /// 断面プロパティ追加時のキー更新漏れ＝キャッシュ衝突を検出する）。
        /// </summary>
        /// <summary>
        /// M-φ キャッシュで用いる軸力の量子化 [kN]。
        ///
        /// キャッシュキーは軸力を 1kN 単位に丸める。<b>曲線もこの丸めた軸力で計算する</b>こと。
        /// 丸めた値をキーにしながら生の軸力で計算すると、同じキーに対して
        /// 「最初にそのキーを作った軸力」の曲線が入るため、キャッシュの履歴で結果が変わる
        /// （＝ 実行順やキャッシュの温冷で非線形解析の収束経路が変わる）。
        /// キーと値で同じ軸力を使うことで、キャッシュが純粋な関数のキャッシュになる。
        /// </summary>
        internal static double QuantizeAxialNForMPhi(double axialN)
            => double.IsFinite(axialN) ? Math.Round(axialN) : axialN;

        internal string GetMPhiCacheKey(double axialN)
        {
            // 軸力を1kN単位で丸める（同程度の軸力では同じ曲線とみなす）
            // 注: axialNはkN単位を期待（GetMPhiRelationshipの入力と同じ）
            long axialNRounded = (long)QuantizeAxialNForMPhi(axialN);

            // バイリニアコンクリートのモデル化オプション（引張無視・圧縮低減）を含める。
            // オプションが変わると M-φ も変わるため、キャッシュ衝突を防ぐ。
            string cmo = ConcreteModelOptions.Signature();

            // 断面タイプに応じて関連パラメータを含める
            string key = (PileBodyType, PileSectionType) switch
            {
                // 場所打ちRC杭
                (PileTypeNames.InsituRc, _) =>
                    $"RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{ConcreteGamma}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.RcSection) =>
                    $"SPRC-RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{ConcreteGamma}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - 鋼管RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) =>
                    $"SPRC-SP|{PipeGrade}|{PipeDia}|{PipeTs}|{CorrosionDepth}|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{ConcreteGamma}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // PHC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Phc) =>
                    $"PHC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // PHC節杭。断面耐力は軸部基準で PHC杭 と同一だが、キーは別プレフィクスにして
                // 「どちらの断面のキャッシュか」を追えるようにする (値が同じでも衝突はしない)。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PhcNodular) =>
                    $"NPH|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // PRC節杭 (PHC部)。断面耐力は PHC杭 と同一だが、キーは別プレフィクスにする。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodularPhcPart) =>
                    $"NPRC-PHC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // BF.S (頭部厚型節付き杭)。断面耐力は PHC杭 と同一だが、
                // 頭部軸部と先端軸部で外径・肉厚・σce が違うのでキーも分ける。
                (PileTypeNames.PrecastConcrete, PileTypeNames.BfsHead) =>
                    $"BFS-HEAD|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                (PileTypeNames.PrecastConcrete, PileTypeNames.BfsTip) =>
                    $"BFS-TIP|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // PRC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Prc) =>
                    $"PRC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // PRC節杭 (PRC部)。断面耐力は PRC杭 と同一だが、キーは別プレフィクスにする。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodular) =>
                    $"NPRC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // SC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Sc) =>
                    $"SC|{PileDiameter}|{PipeTs}|{ConcreteThickness}|{ConcreteFc}|{PipeGrade}|{PipeDia}|{CorrosionDepth}|N={axialNRounded}",

                // その他（鋼管杭の鋼管部、コンクリート充填鋼管部など）。
                // 充填鋼管部はファイバー M-φ の対象となり得るため、断面諸元をキーに含めて
                // 異なる断面同士のキャッシュ衝突を防ぐ。
                // 鋼管杭はここに落ちる。座屈長は耐力を変えるのでキーに含める
                // (含めないと、液状化の有無が違うモデルで前のモデルの曲線が返る)。
                // ConcreteGamma を含める。Ec = 3.35e4 · (γ/24)² · (ξ·Fc/60)^(1/3) なので
                // γ は曲線に効く。場所打ちRC と場所打ち鋼管コンクリートの鍵には入って
                // いたが、ここだけ抜けていた。M-φ キャッシュ (static) を捨てるのは
                // ファイル読込の 1 か所だけなので、キャッシュの正しさは鍵の完全性だけに
                // 依存する。抜けていると γ を変えても古い曲線が返る
                // (MphiCacheKeyCompletenessTests が検出)。
                // コンクリート充填鋼管部の杭頭 M-φ (SteelPipeSection) は合成 EI から作るので、Ec・主筋の断面積・鋼管の E も効く (2026-10-08)
                _ => $"OTHER|{PileBodyType}|{PileSectionType}|{PipeGrade}|{PipeDia}|{PipeTs}|{CorrosionDepth}|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{ConcreteGamma}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|Ec={ConcreteE:R}|Ag={MainBarAg:R}|Er={MainBarEr:R}|Es={PipeEs:R}|lk={EffectiveBucklingLength:F3}|N={axialNRounded}"
            };

            return $"{key}|{cmo}";
        }

        // デバッグ用: GetMPhiRelationship呼び出し回数
        private static int _getMphiCallCount = 0;

        /// <summary>
        /// PileSection に各断面型の GetMPhiRelationship を仲介するメソッド。
        ///
        /// 単位系変換の説明:
        /// - 呼び出し側（FEM解析）の入力:
        ///   axialN (軸力): [kN]
        /// - 断面計算側（InsituReinforcedConcreteSection等）の期待入力/出力:
        ///   axialN: [N]
        ///   φ (曲率): [1/mm]
        ///   M (曲げモーメント): [N·mm]
        /// - FEM解析側（MomentCurvatureCurve）の期待値:
        ///   φ: [1/m] = [rad/m]
        ///   M: [kNm]
        /// </summary>
        public (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            _getMphiCallCount++;

            // キャッシュキーと同じ量子化を計算にも適用する。
            // ここで揃えないと、同じキーに入る曲線が「最初にそのキーを作った軸力」次第で変わり、
            // キャッシュの履歴（実行順・温冷・上限到達による全クリア）で
            // 非線形解析の収束経路が変わってしまう。
            axialN = QuantizeAxialNForMPhi(axialN);

            // キャッシュキーを生成
            string cacheKey = GetMPhiCacheKey(axialN);

            // キャッシュから取得を試みる
            lock (_mPhiCacheLock)
            {
                if (_mphiCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    _mPhiCacheHitCount++;

                    // キャッシュされたリストのコピーを返す（元データの変更防止）
                    return (new List<double>(cachedResult.Phis), new List<double>(cachedResult.Moments));
                }
            }

            try
            {
                var section = CreateSectionCalculator();

                // 単位変換: 軸力 kN → N
                double axialN_inN = axialN * UnitConversion.KN_TO_N;

                List<double>? phisRaw = null;
                List<double>? msRaw = null;

                // 断面が生成できない場合は、鋼管杭+鋼管部のケースを SteelPipeSection で代替する
                // (CreateSectionCalculator が (PileTypeNames.SteelPipe, _) で null を返すため)
                if (section == null)
                {
                    if (PileBodyType == PileTypeNames.SteelPipe
                        && (PileSectionType == PileTypeNames.SteelPipeSection || PileSectionType == PileTypeNames.SteelPipe))
                    {
                        var sps = TryCreateSteelPipeSection();
                        if (sps != null)
                        {
                            try
                            {
                                var (psphi, psm) = sps.GetMPhiRelationshipMiddle(axialN_inN);
                                if (psphi != null && psm != null && psphi.Count >= 2 && psm.Count == psphi.Count)
                                {
                                    phisRaw = psphi;
                                    msRaw = psm;
                                }
                            }
                            catch (Exception ex)
                            {
                                Serilog.Log.Debug(
                                    $"[GetMPhiRelationship] SteelPipeSection.GetMPhiRelationshipMiddle 例外: {ex.Message}");
                            }
                        }
                    }

                    if (phisRaw == null)
                        return CreateLinearFallback();
                }
                else
                {
                    // 鋼管杭のコンクリート充填鋼管部 (杭頭接合部): 杭断面ウィンドウと同じ SteelPipeSection の杭頭 M-φ
                    // (0 → (φMd, Md) → (φMu', Mu) → (φu, Mu)。ひび割れモーメントの概念を用いず、M-N 図の Md・Mu と整合) を使う。
                    // 以前の解析は断面計算 (場所打ち鋼管コンクリート杭の式。Mcr → My → Mu) の折線を使い、
                    // 利用者が画面で確かめる曲線と解析に入る曲線が別の式だった。ファイバー M-φ オプションのときは従来どおりファイバー曲線
                    if (!ConcreteModelOptions.UseFiberMPhi
                        && PileBodyType == PileTypeNames.SteelPipe && PileSectionType == PileTypeNames.CftSection
                        && TryCreateSteelPipeSection() is { } headSps)
                    {
                        try
                        {
                            var (hphi, hm) = headSps.GetMPhiRelationshipHead(axialN_inN);
                            if (hphi != null && hm != null && hphi.Count >= 2 && hm.Count == hphi.Count)
                                (phisRaw, msRaw) = MakeMonotonicForAnalysis(hphi, hm);
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Debug($"[GetMPhiRelationship] SteelPipeSection.GetMPhiRelationshipHead 例外: {ex.Message}");
                        }
                    }

                    // ファイバーモデル M-φ オプション。
                    // 対応断面: 場所打ちRC / 場所打ち鋼管コンクリート（RC部・鋼管コンクリート部）/
                    //           PHC / PRC / SC / コンクリート充填鋼管部（すべて AbstractPileSection 系）。
                    // 鋼管杭の鋼管部は SteelPipeSection（別系統、section == null パス）のため対象外。
                    // 解けない場合（軸力が耐力範囲外等）は従来ポリリニアへフォールバック。
                    if (phisRaw != null)
                    {
                        // 上の杭頭 M-φ (鋼管杭のコンクリート充填鋼管部) を使う
                    }
                    else if (ConcreteModelOptions.UseFiberMPhi
                        && section is AbstractPileSection fiberSection
                        && fiberSection.GetMPhiRelationshipFiber(axialN_inN) is { } fiber)
                    {
                        // FEM ばねとして負勾配・零勾配とならないよう単調化＋最小勾配床
                        (phisRaw, msRaw) = MakeMonotonicForAnalysis(fiber.Phis, fiber.Moments);
                    }
                    else
                    {
                        // 指針折線。FEM は区間勾配をそのまま接線剛性に使い、1% の下限は終点より先にしか無い。
                        // 折線の作り方は断面ごとに分岐が多く、既製杭の折り返し (Mcr ≥ β1β2·Mu0、2026-09-07)
                        // のように途中の区間が負勾配になると K_tan が負のまま Newton-Raphson に入る。
                        // 断面側で折り返さないようにしたうえで、FEM の入口でもファイバー M-φ と同じ
                        // 単調化 (最大 M の割線 × 1% を勾配の下限) を通しておく。
                        // 単調な折線には (勾配が下限を下回る区間が無い限り) 何もしない。
                        (phisRaw, msRaw) = section.GetMPhiRelationship(axialN_inN);
                        if (phisRaw != null && msRaw != null && phisRaw.Count == msRaw.Count && phisRaw.Count >= 2)
                            (phisRaw, msRaw) = MakeMonotonicForAnalysis(phisRaw, msRaw);
                    }
                }

                // 結果が不正な場合もフォールバック
                if (phisRaw == null || msRaw == null || phisRaw.Count < 2 || msRaw.Count != phisRaw.Count)
                {
                    return CreateLinearFallback();
                }

                // 単位変換: φ [1/mm] → [1/m], M [N·mm] → [kNm]
                var phis = phisRaw.Select(p => p * UnitConversion.PER_MM_TO_PER_M).ToList();
                var ms = msRaw.Select(m => m * UnitConversion.NMM_TO_KNM).ToList();

                // キャッシュに保存
                lock (_mPhiCacheLock)
                {
                    _mPhiCacheMissCount++;
                    // エントリ数上限: 諸元×軸力(1kN 丸め)×オプションでキーが増え続けるため、
                    // 長時間セッションでの無制限成長を防ぐ。上限到達時は全クリア
                    // （LRU 管理は不要 — 再計算は数 ms、通常の解析 1 回で使うキーは数百程度）。
                    // 上限に達したら「以後は積まない」だけにする。
                    // 以前は全クリアしていたが、いつクリアされるかが実行履歴で変わり、
                    // 同じ解析でもキャッシュヒットの当たり外れが変わっていた。
                    // 値はキーの純粋な関数なので、積まない分は再計算されるだけで結果は変わらない。
                    const int MaxCacheEntries = 10_000;
                    bool hasRoom = _mphiCache.Count < MaxCacheEntries;
                    if (!hasRoom && !_mPhiCacheLimitLogged)
                    {
                        _mPhiCacheLimitLogged = true;
                        Serilog.Log.Debug(
                            "[MphiCache] エントリ数が上限 {Max} に達したため、以後は新規エントリを保持しません",
                            MaxCacheEntries);
                    }
                    if (hasRoom && !_mphiCache.ContainsKey(cacheKey))
                    {
                        _mphiCache[cacheKey] = (phis, ms);
                    }
                }

                return (new List<double>(phis), new List<double>(ms));
            }
            catch (Exception ex)
            {
                // CreateLinearFallback() が「線形弾性で代替した」ことは記録するが、
                // 例外そのものは伝わらない。原因が分からないと直せないので、
                // ここで理由だけ足しておく。
                PileDesign.Common.CalcFallbackTracker.Report(
                    "M-φ の算定で例外", ex,
                    $"杭体={PileBodyType}, 断面={PileSectionType}");
                return CreateLinearFallback();
            }
        }

        /// <summary>
        /// ファイバーモデル M-φ（生曲線）を解析用に単調非減少化する。
        /// ひび割れ直後の局所的な M 低下（コンクリート引張脱落）や終局近傍のプラトーが
        /// FEM の負勾配・零勾配ばね（→ Newton 収束不能）とならないよう、各セグメントに
        /// 最小勾配床を与える。床は「最大 M 点の割線剛性 × 1%」とし、
        /// MomentCurvatureCurve の post-yield 外挿床（降伏時割線 × 1%）と同方針で整合させる。
        /// 単位は入力のまま（φ [1/mm], M [N·mm]）。
        /// </summary>
        internal static (List<double> Phis, List<double> Moments) MakeMonotonicForAnalysis(
            List<double> phis, List<double> ms)
        {
            // 最大 M 点の割線剛性から最小勾配床を決める
            double mMax = 0.0, phiAtMax = 0.0;
            for (int i = 0; i < ms.Count; i++)
            {
                if (ms[i] > mMax) { mMax = ms[i]; phiAtMax = phis[i]; }
            }
            double minSlope = phiAtMax > 0.0 ? 0.01 * (mMax / phiAtMax) : 0.0;

            var outMs = new List<double>(ms);
            for (int i = 1; i < outMs.Count; i++)
            {
                double floor = outMs[i - 1] + minSlope * (phis[i] - phis[i - 1]);
                if (outMs[i] < floor) outMs[i] = floor;
            }
            return (phis, outMs);
        }

        /// <summary>
        /// M-θ 関係を取得します（場所打ちRC杭用）。
        /// 単位: θ [rad], M [kNm]
        /// </summary>
        public (List<double> Thetas, List<double> Moments)? GetMThetaRelationship(double axialN)
        {
            var section = CreateSectionCalculator();
            if (section is not InsituReinforcedConcreteSection rcSection)
                return null;

            // 単位変換: 軸力 kN → N
            var (thetas, msRaw) = rcSection.GetMThetaRelationship(axialN * UnitConversion.KN_TO_N);
            if (thetas == null || msRaw == null || thetas.Count < 2 || msRaw.Count != thetas.Count)
                return null;

            // 単位変換: M [N·mm] → [kNm]
            var ms = msRaw.Select(m => m * UnitConversion.NMM_TO_KNM).ToList();
            return (thetas, ms);
        }

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の杭頭ひび割れモーメント Mcr を返します。
        /// 他の断面型では null を返します (Mcr 同期 Mode 切替は無効)。
        /// 単位: 入力 axialN [kN], 返り値 [kNm]。
        /// </summary>
        public double? GetPileHeadMcrInKNm(double axialN)
        {
            var section = CreateSectionCalculator();
            if (section is not InsituReinforcedConcreteSection rcSection)
                return null;

            // 単位変換: 軸力 kN → N
            (double mcrNmm, _) = rcSection.GetCrackMoment(axialN * UnitConversion.KN_TO_N, false);
            if (!double.IsFinite(mcrNmm) || mcrNmm <= 0.0) return null;

            // 単位変換: M [N·mm] → [kNm]
            return mcrNmm * UnitConversion.NMM_TO_KNM;
        }

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の杭頭 M-θ 関係が適用可能な軸力範囲か判定します。
        /// 基礎指針: -Ft ≤ σ0 ≤ (1/4)·ξ·Fc の範囲でのみ M-θ を考慮、範囲外は剛結扱い。
        ///   σ0  = N / Ae [N/mm²]  (N: axial force, Ae: effective area)
        ///   Ft  : concrete tensile strength [N/mm²]
        ///   ξ·Fc: reduced compressive strength [N/mm²]
        /// 他の断面型では false を返す。
        /// </summary>
        public bool IsWithinMThetaValidAxialRange(double axialN)
        {
            var section = CreateSectionCalculator();
            if (section is not InsituReinforcedConcreteSection rcSection)
                return false;
            if (rcSection.Ae <= 0.0) return false;

            // 単位変換: axialN [kN] → N, σ0 [N/mm²]
            double sigma0 = (axialN * UnitConversion.KN_TO_N) / rcSection.Ae;
            double Ft = rcSection.Ft;
            double xiFc = rcSection.InsituConcrete.Gsi * rcSection.InsituConcrete.Fc;

            return sigma0 >= -Ft && sigma0 <= 0.25 * xiFc;
        }

        /// <summary>
        /// M-φキャッシュの統計情報を取得します。
        /// </summary>
        public static (int hits, int misses, int cacheSize) GetMphiCacheStats()
        {
            lock (_mPhiCacheLock)
            {
                return (_mPhiCacheHitCount, _mPhiCacheMissCount, _mphiCache.Count);
            }
        }

        /// <summary>
        /// M-φキャッシュをクリアします（断面パラメータ変更時や新規プロジェクト読み込み時）。
        /// </summary>
        public static void ClearMphiCache()
        {
            lock (_mPhiCacheLock)
            {
                _mphiCache.Clear();
                _mPhiCacheLimitLogged = false;
                _mPhiCacheHitCount = 0;
                _mPhiCacheMissCount = 0;
            }
        }

        /// <summary>
        /// M-φ 関係のフォールバック（線形近似）
        /// </summary>
        private (List<double> Phis, List<double> Moments) CreateLinearFallback()
        {
            // M-φ が算定できなかった場合の線形弾性代替（GetMPhiRelationship の失敗パスからのみ呼ばれる）。
            // 非線形性が失われたまま解析が継続するため、フォールバックとして必ず記録する。
            PileDesign.Common.CalcFallbackTracker.Report("M-φ の算定（→線形弾性で代替）",
                detail: $"PileBodyType={PileBodyType}, PileSectionType={PileSectionType}");

            const double phiSample = 1e-6; // [1/m]
            return (
                new List<double> { 0.0, phiSample },
                new List<double> { 0.0, EI * phiSample }  // EI [kNm²] × phi [1/m] = M [kNm]
            );
        }



    }
}
