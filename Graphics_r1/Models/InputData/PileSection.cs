using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace PileDesign.Models.InputData
{
    public class PileSection : BaseModel
    {
        // 静的キャッシュ（CSVデータは一度だけ読み込む）
        private static readonly Lazy<List<PrecastPile>> _cachedPHCs = new(() => LoadPrecastPilesFromCsv("pile_library_PHC.csv"));
        private static readonly Lazy<List<PrecastPile>> _cachedPRCs = new(() => LoadPrecastPilesFromCsv("pile_library_PRC.csv"));
        private static readonly Lazy<List<PrecastPile>> _cachedSCs = new(() => LoadPrecastPilesFromCsv("pile_library_SC.csv"));
        private static readonly Lazy<List<SteelPipePile>> _cachedSteelPipePiles = new(() => LoadSteelPipePilesFromCsv("pile_library_SteelPile.csv"));

        // オプションリストも静的キャッシュ（一度だけ構築）
        private static readonly Lazy<ObservableCollection<string>> _cachedPHCOption = new(() =>
            new ObservableCollection<string>(_cachedPHCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedPRCOption = new(() =>
            new ObservableCollection<string>(_cachedPRCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSCOption = new(() =>
            new ObservableCollection<string>(_cachedSCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSteelPipeOption = new(() =>
            new ObservableCollection<string>(_cachedSteelPipePiles.Value.Select(p => $"{p.Diameter}x{p.Thickness}")));

        private static List<PrecastPile> LoadPrecastPilesFromCsv(string fileName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(baseDir, "Models", "PileLibrary", fileName);
                return PrecastPileLoader.LoadFromCsv(filePath) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static List<SteelPipePile> LoadSteelPipePilesFromCsv(string fileName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(baseDir, "Models", "PileLibrary", fileName);
                return SteelPipePileLoader.LoadFromCsv(filePath) ?? [];
            }
            catch
            {
                return [];
            }
        }

        // フィールド
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        // --- NQプロパティ（計算プロパティ） ---
        // NQ Raw（N[N], Q[N]）を取得し、スケーリング後（kN, kN）を返す
        public (List<double> N, List<double> Q) UnfactoredServiceNQ =>
            _unfactoredServiceNQCache ??= GetNQScaled(nameof(UnfactoredServiceNQ));
        public (List<double> N, List<double> Q) UnfactoredDamageNQ =>
            _unfactoredDamageNQCache ??= GetNQScaled(nameof(UnfactoredDamageNQ));
        public (List<double> N, List<double> Q) UnfactoredUltimateNQ =>
            _unfactoredUltimateNQCache ??= GetNQScaled(nameof(UnfactoredUltimateNQ));
        public (List<double> N, List<double> Q) FactoredServiceNQ =>
            _factoredServiceNQCache ??= GetNQScaled(nameof(FactoredServiceNQ));
        public (List<double> N, List<double> Q) FactoredDamageNQ =>
            _factoredDamageNQCache ??= GetNQScaled(nameof(FactoredDamageNQ));
        public (List<double> N, List<double> Q) FactoredUltimateNQ =>
            _factoredUltimateNQCache ??= GetNQScaled(nameof(FactoredUltimateNQ));

        // 後方互換性のためのエイリアス（Dagame -> Damage）
        public (List<double> N, List<double> Q) UnfactoredDagameNQ => UnfactoredDamageNQ;
        public (List<double> N, List<double> Q) FactoredDagameNQ => FactoredDamageNQ;

        // --- NQキャッシュ ---
        private (List<double> N, List<double> Q)? _unfactoredServiceNQCache;
        private (List<double> N, List<double> Q)? _unfactoredDamageNQCache;
        private (List<double> N, List<double> Q)? _unfactoredUltimateNQCache;
        private (List<double> N, List<double> Q)? _factoredServiceNQCache;
        private (List<double> N, List<double> Q)? _factoredDamageNQCache;
        private (List<double> N, List<double> Q)? _factoredUltimateNQCache;

        // --- 追加: NMキャッシュ（ウィンドウ起動時の再計算を抑制） ---
        private (List<double> N, List<double> M)? _unfactoredServiceNMCache;
        private (List<double> N, List<double> M)? _unfactoredDamageNMCache;
        private (List<double> N, List<double> M)? _unfactoredUltimateNMCache;
        private (List<double> N, List<double> M)? _factoredServiceNMCache;
        private (List<double> N, List<double> M)? _factoredDamageNMCache;
        private (List<double> N, List<double> M)? _factoredUltimateNMCache;

        // --- 追加: M-φキャッシュ（同一断面・同一軸力での再計算を抑制） ---
        // キーは断面プロパティハッシュ + 軸力(kN)を丸めた値
        private static readonly Dictionary<string, (List<double> Phis, List<double> Moments)> _mphiCache = [];
        private static readonly object _mPhiCacheLock = new();
        private static int _mPhiCacheHitCount = 0;
        private static int _mPhiCacheMissCount = 0;

        /// <summary>
        /// 断面パラメータ変更時にすべてのキャッシュを一括で無効化します。
        /// </summary>
        private void InvalidateAllCaches()
        {
            InvalidateNMCache();
            InvalidateSteelYieldCache();
            InvalidateCrackCache();
        }
        private void InvalidateNMCache()
        {
            _unfactoredServiceNMCache = null;
            _unfactoredDamageNMCache = null;
            _unfactoredUltimateNMCache = null;
            _factoredServiceNMCache = null;
            _factoredDamageNMCache = null;
            _factoredUltimateNMCache = null;
        }

        // 追加: 降伏開始NMのキャッシュ
        private (List<double> N, List<double> M)? _steelYieldNMRawCache;

        // 追加: キャッシュ無効化ヘルパ
        private void InvalidateSteelYieldCache() => _steelYieldNMRawCache = null;

        // 追加: 降伏開始NMのキャッシュ
        private (List<double> N, List<double> M)? _crackNMRawCache;

        // 追加: キャッシュ無効化ヘルパ
        private void InvalidateCrackCache() => _crackNMRawCache = null;

        //public List<double> UltimateLimitAxialForceThresholds { get; private set; } = [];

        // 新: バッキングフィールド＋通知
        private List<double> _ultimateLimitAxialForceThresholds = [];
        public List<double> UltimateLimitAxialForceThresholds
        {
            get => _ultimateLimitAxialForceThresholds;
            private set => SetProperty(ref _ultimateLimitAxialForceThresholds, value);
        }

        public (List<double> N, List<double> M) UnfactoredServiceNMRaw => GetNMRaw(nameof(UnfactoredServiceNM));
        public (List<double> N, List<double> M) UnfactoredDamageNMRaw => GetNMRaw(nameof(UnfactoredDamageNM));
        public (List<double> N, List<double> M) UnfactoredUltimateNMRaw => GetNMRaw(nameof(UnfactoredUltimateNM));

        public (List<double> N, List<double> M) FactoredServiceNMRaw => GetNMRaw(nameof(FactoredServiceNM));
        public (List<double> N, List<double> M) FactoredDamageNMRaw => GetNMRaw(nameof(FactoredDamageNM));
        public (List<double> N, List<double> M) FactoredUltimateNMRaw => GetNMRaw(nameof(FactoredUltimateNM));

        // --- 変更: NMプロパティをキャッシュ ---
        public (List<double> N, List<double> M) UnfactoredServiceNM =>
            _unfactoredServiceNMCache ??= (
                GetMultipliedListValues(UnfactoredServiceNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredServiceNMRaw.M, 1e-6)
            );

        public (List<double> N, List<double> M) UnfactoredDamageNM =>
            _unfactoredDamageNMCache ??= (
                GetMultipliedListValues(UnfactoredDamageNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredDamageNMRaw.M, 1e-6)
            );

        public (List<double> N, List<double> M) UnfactoredUltimateNM =>
            _unfactoredUltimateNMCache ??= (
                GetMultipliedListValues(UnfactoredUltimateNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredUltimateNMRaw.M, 1e-6)
            );

        public (List<double> N, List<double> M) FactoredServiceNM =>
            _factoredServiceNMCache ??= (
                GetMultipliedListValues(FactoredServiceNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredServiceNMRaw.M, 1e-6)
            );

        public (List<double> N, List<double> M) FactoredDamageNM =>
            _factoredDamageNMCache ??= (
                GetMultipliedListValues(FactoredDamageNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredDamageNMRaw.M, 1e-6)
            );

        public (List<double> N, List<double> M) FactoredUltimateNM =>
            _factoredUltimateNMCache ??= (
                GetMultipliedListValues(FactoredUltimateNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredUltimateNMRaw.M, 1e-6)
            );

        // 降伏開始NM（Raw: N[N], M[Nmm]）をキャッシュ付きで返す
        public (List<double> N, List<double> M) SteelYieldNMRaw
            => _steelYieldNMRawCache ??= ComputeSteelYieldNMRaw();

        // スケーリング後（kN, kNm）
        public (List<double> N, List<double> M) SteelYieldNM => (
            GetMultipliedListValues(SteelYieldNMRaw.N, 1e-3),
            GetMultipliedListValues(SteelYieldNMRaw.M, 1e-6)
        );

        // 降伏開始NM（Raw: N[N], M[Nmm]）をキャッシュ付きで返す
        public (List<double> N, List<double> M) CrackNMRaw
            => _crackNMRawCache ??= ComputeCrackNMRaw();

        // スケーリング後（kN, kNm）
        public (List<double> N, List<double> M) CrackNM => (
            GetMultipliedListValues(CrackNMRaw.N, 1e-3),
            GetMultipliedListValues(CrackNMRaw.M, 1e-6)
        );

        private (List<double> N, List<double> M) ComputeSteelYieldNMRaw()
        {
            // CreateSectionCalculator() を使って断面を取得
            var section = CreateSectionCalculator();

            // InsituReinforcedConcreteSection の場合のみ有効
            if (section is InsituReinforcedConcreteSection rcSection)
            {
                var (ns, ms, _, _) = rcSection.GetSteelYieldMNInteraction();
                return (ns, ms);
            }

            return (new List<double>(), new List<double>());
        }

        private (List<double> N, List<double> M) ComputeCrackNMRaw()
        {
            // CreateSectionCalculator() を使って断面を取得
            var section = CreateSectionCalculator();

            // InsituReinforcedConcreteSection の場合のみ有効
            if (section is InsituReinforcedConcreteSection rcSection)
            {
                var (ns, ms, _, _) = rcSection.GetCrackMNInteraction(false);
                return (ns, ms);
            }

            return (new List<double>(), new List<double>());
        }
        //private (List<double> N, List<double> M) ComputeSteelYieldNMRaw()
        //{
        //    bool isTargetRC =
        //        PileBodyType == "場所打ち鉄筋コンクリート杭" ||
        //        (PileBodyType == "場所打ち鋼管コンクリート杭" && PileSectionType == "鉄筋コンクリート部");

        //    if (!isTargetRC)
        //        return (new List<double>(), new List<double>());

        //    var insituConcrete = new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc);
        //    var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //    var section = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

        //    var (ns, ms, _, _) = section.GetSteelYieldMNInteraction();
        //    return (ns, ms);
        //}

        //private (List<double> N, List<double> M) ComputeCrackNMRaw()
        //{
        //    bool isTargetRC =
        //        PileBodyType == "場所打ち鉄筋コンクリート杭" ||
        //        (PileBodyType == "場所打ち鋼管コンクリート杭" && PileSectionType == "鉄筋コンクリート部");

        //    if (!isTargetRC)
        //        return (new List<double>(), new List<double>());

        //    var insituConcrete = new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc);
        //    var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //    var section = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

        //    var (ns, ms, _, _) = section.GetCrackMNInteraction(false); // 非線形
        //    return (ns, ms);
        //}

        private static double GetFilteredNMax((List<double> N, List<double> M) nmData)
        {
            var filteredN = nmData.N
                .Zip(nmData.M, (n, m) => new { N = n, M = m })
                .Where(pair => pair.M != 0)
                .Select(pair => pair.N);

            return filteredN.Any() ? filteredN.Max() : 0.0;
        }

        private static double GetFilteredNMin((List<double> N, List<double> M) nmData)
        {
            var filteredN = nmData.N
                .Zip(nmData.M, (n, m) => new { N = n, M = m })
                .Where(pair => pair.M != 0)
                .Select(pair => pair.N);

            return filteredN.Any() ? filteredN.Min() : 0.0;
        }

        public double UnfactoredServiceNMax => GetFilteredNMax(UnfactoredServiceNM);
        public double UnfactoredDamageNMax => GetFilteredNMax(UnfactoredDamageNM);
        public double UnfactoredUltimateNMax => GetFilteredNMax(UnfactoredUltimateNM);

        public double UnfactoredServiceNMin => GetFilteredNMin(UnfactoredServiceNM);
        public double UnfactoredDamageNMin => GetFilteredNMin(UnfactoredDamageNM);
        public double UnfactoredUltimateNMin => GetFilteredNMin(UnfactoredUltimateNM);

        public double FactoredServiceNMax => GetFilteredNMax(FactoredServiceNM);
        public double FactoredDamageNMax => GetFilteredNMax(FactoredDamageNM);
        public double FactoredUltimateNMax => GetFilteredNMax(FactoredUltimateNM);

        public double FactoredServiceNMin => GetFilteredNMin(FactoredServiceNM);
        public double FactoredDamageNMin => GetFilteredNMin(FactoredDamageNM);
        public double FactoredUltimateNMin => GetFilteredNMin(FactoredUltimateNM);

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
            => GetMPhiRelationship(axialN, 1.0);

        /// <summary>
        /// M-φキャッシュ用のキーを生成します。
        /// 断面の種類と主要パラメータ + 軸力を組み合わせた文字列を返します。
        /// 軸力は1kN単位で丸めてキャッシュヒット率を向上させます。
        /// 注: このメソッドはGetMPhiRelationshipと同じ単位系（kN）を期待
        /// </summary>
        private string GetMPhiCacheKey(double axialN)
        {
            // 軸力を1kN単位で丸める（同程度の軸力では同じ曲線とみなす）
            // 注: axialNはkN単位を期待（GetMPhiRelationshipの入力と同じ）
            long axialNRounded = (long)Math.Round(axialN);

            // 断面タイプに応じて関連パラメータを含める
            return (PileBodyType, PileSectionType) switch
            {
                // 場所打ちRC杭
                ("場所打ち鉄筋コンクリート杭", _) =>
                    $"RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - RC部
                ("場所打ち鋼管コンクリート杭", "鉄筋コンクリート部") =>
                    $"SPRC-RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - 鋼管RC部
                ("場所打ち鋼管コンクリート杭", "鋼管コンクリート部") =>
                    $"SPRC-SP|{PipeGrade}|{PipeDia}|{PipeTs}|{CorrosionDepth}|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // PHC杭
                ("既製コンクリート杭", "PHC杭") =>
                    $"PHC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // PRC杭
                ("既製コンクリート杭", "PRC杭") =>
                    $"PRC|{PileDiameter}|{ConcreteThickness}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|{TendonDp}|{TendonAp}|{TendonSigmaPy}|{TendonSigmaPu}|{Prestress}|N={axialNRounded}",

                // SC杭
                ("既製コンクリート杭", "SC杭") =>
                    $"SC|{PileDiameter}|{PipeTs}|{ConcreteThickness}|{ConcreteFc}|{PipeGrade}|{PipeDia}|{CorrosionDepth}|N={axialNRounded}",

                // 鋼管杭（未対応）
                _ => $"OTHER|{PileBodyType}|{PileSectionType}|N={axialNRounded}"
            };
        }

        // デバッグ用: GetMPhiRelationship呼び出し回数
        private static int _getMphiCallCount = 0;

        public (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN, double _)
        {
            _getMphiCallCount++;

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

                // 断面が生成できない場合はフォールバック
                if (section == null)
                {
                    return CreateLinearFallback();
                }

                // 単位変換: 軸力 kN → N
                double axialN_inN = axialN * 1000.0;

                var (phisRaw, msRaw) = section.GetMPhiRelationship(axialN_inN);

                // 結果が不正な場合もフォールバック
                if (phisRaw == null || msRaw == null || phisRaw.Count < 2 || msRaw.Count != phisRaw.Count)
                {
                    return CreateLinearFallback();
                }

                // 単位変換: φ [1/mm] → [1/m], M [N·mm] → [kNm]
                var phis = phisRaw.Select(p => p * 1000.0).ToList();
                var ms = msRaw.Select(m => m * 1e-6).ToList();

                // キャッシュに保存
                lock (_mPhiCacheLock)
                {
                    _mPhiCacheMissCount++;
                    if (!_mphiCache.ContainsKey(cacheKey))
                    {
                        _mphiCache[cacheKey] = (phis, ms);
                    }
                }

                return (new List<double>(phis), new List<double>(ms));
            }
            catch (Exception ex)
            {
                return CreateLinearFallback();
            }
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
                _mPhiCacheHitCount = 0;
                _mPhiCacheMissCount = 0;
            }
        }

        /// <summary>
        /// M-φ 関係のフォールバック（線形近似）
        /// </summary>
        private (List<double> Phis, List<double> Moments) CreateLinearFallback()
        {
            const double phiSample = 1e-6; // [1/m]
            return (
                new List<double> { 0.0, phiSample },
                new List<double> { 0.0, EI * phiSample }  // EI [kNm²] × phi [1/m] = M [kNm]
            );
        }



        public string PileDescription
        {
            get
            {
                if (PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                    (PileBodyType == "場所打ち鋼管コンクリート杭" && PileSectionType == "鉄筋コンクリート部"))
                {
                    return $"D={PileDiameter}({MainBarNum}-{MainBarSize})";
                }
                else if (PileBodyType == "場所打ち鋼管コンクリート杭" && PileSectionType == "鋼管コンクリート部")
                {
                    if (MainBarNum != 0)
                    {
                        return $"{PipeDia}x{PipeTs}({MainBarNum}-{MainBarSize}) ";
                    }
                    else
                    {
                        return $"{PipeDia}x{PipeTs}";
                    }
                }
                else if (PileBodyType == "既製コンクリート杭")
                {
                    return SelectedPrecastPile.Name;
                }
                else if (PileBodyType == "鋼管杭")
                {
                    return SelectedSteelPipePileName;
                }

                return string.Empty;
            }
        }

        private string _pileBodyType = "場所打ち鉄筋コンクリート杭";
        public string PileBodyType
        {
            get => _pileBodyType;
            set
            {
                // 値のバリデーション
                var validTypes = new[]
                {
                    "場所打ち鉄筋コンクリート杭",
                    "場所打ち鋼管コンクリート杭",
                    "既製コンクリート杭",
                    "鋼管杭"
                };
                var safeValue = string.IsNullOrWhiteSpace(value) || !validTypes.Contains(value)
                    ? validTypes[0]
                    : value;

                if (SetProperty(ref _pileBodyType, safeValue))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private string _pileSectionType = "鉄筋コンクリート部";
        public string PileSectionType
        {
            get => _pileSectionType;
            set
            {
                var validTypes = new[]
                {
                    "鉄筋コンクリート部",
                    "鋼管コンクリート部",
                    "PHC杭",
                    "PRC杭",
                    "SC杭",
                    "鋼管杭"
                };
                var safeValue = string.IsNullOrWhiteSpace(value) || !validTypes.Contains(value)
                    ? validTypes[0]
                    : value;

                if (SetProperty(ref _pileSectionType, safeValue))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private string _selectedSteelPipe;
        public string SelectedSteelPipe
        {
            get => _selectedSteelPipe;
            set => SetProperty(ref _selectedSteelPipe, value);
        }

        public string[] InsituPileSectionTypesOption { get; } =
        [
            "場所打ち鉄筋コンクリート杭",
            "場所打ち鋼管コンクリート杭",
        ];

        // 場所打ち鋼管コンクリート杭の部位 
        public string[] InsituSteelPileSectionTypeOption { get; } =
        [
            "鋼管コンクリート部",
            "鉄筋コンクリート部",
        ];

        public string[] PreCastConcretePileSectionTypeOption { get; } =
        [
            "PHC杭",
            "PRC杭",
            "SC杭"
        ];

        public string[] SteelPipePileSectionTypeOption { get; } =
        [
            "鋼管杭",
        ];

        // 杭径
        private double _pileDiameter = 1200.0;
        public double PileDiameter
        {
            get => _pileDiameter;
            set
            {
                if (SetProperty(ref _pileDiameter, value))
                {
                    RecalculatePileDia();
                }
            }
        }

        // コンクリート外径
        private double _concreteOutDia = 1200.0;
        public double ConcreteOutDia
        {
            get => _concreteOutDia;
            set
            {
                if (SetProperty(ref _concreteOutDia, value))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 腐食代
        private double _corrosionDepth = 1.0;
        public double CorrosionDepth
        {
            get => _corrosionDepth;
            set
            {
                if (SetProperty(ref _corrosionDepth, value))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private double _corrodedPipeTs;
        public double CorrodedPipeTs
        {
            get => _corrodedPipeTs;
            set
            {
                if (SetProperty(ref _corrodedPipeTs, value))
                {
                }
            }
        }

        // 杭径変更時のメソッド
        public void RecalculatePileDia()
        {
            try
            {
                if (PileBodyType == "場所打ち鉄筋コンクリート杭")
                {
                    PileDiameter = ConcreteOutDia;
                    ConcreteThickness = ConcreteOutDia * 0.5;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    PileSectionType = "鉄筋コンクリート部";
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                }

                else if (PileBodyType == "場所打ち鋼管コンクリート杭")
                {
                    if (PileSectionType == "鉄筋コンクリート部")
                    {
                        PileDiameter = ConcreteOutDia;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                    else if (PileSectionType == "鋼管コンクリート部")
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                        ConcreteOutDia = PipeDia - PipeTs * 2.0;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                }
                else if (PileBodyType == "鋼管杭")
                {
                    PileDiameter = PipeDia;
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"杭径再計算中にエラーが発生しました。\n{ex.Message}", "杭径再計算エラー", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        //杭断面変更時（デフォルト）のメソッド
        public void ResetSectionProperties()
        {
            try
            {


                // 表示の即時更新用にクリア（後で GetNMRaw が再計算して再設定）
                UltimateLimitAxialForceThresholds = [];

                if (PileBodyType == "場所打ち鉄筋コンクリート杭")
                {
                    PileSectionType = "鉄筋コンクリート部";
                    ConcreteOutDia = 1200.0;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();
                }
                else if (PileBodyType == "場所打ち鋼管コンクリート杭")
                {
                    PileSectionType = "鋼管コンクリート部";
                    ConcreteOutDia = 0.0;
                    PipeDia = 1200.0;
                    PipeTs = 16.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();

                }
                else if (PileBodyType == "既製コンクリート杭")
                {
                    PileSectionType = "PHC杭";
                    ConcreteOutDia = 0.0;
                    MainBarNum = 0;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    // PHCsの要素数チェック
                    if (PHCs.Count >= 10)
                        SelectedPrecastPile = PHCs[^10];
                    else if (PHCs.Count > 0)
                        SelectedPrecastPile = PHCs[0];
                    else
                        SelectedPrecastPile = new PrecastPile();
                    RecalculateSelectedPrecastPile();
                    RecalculatePileDia();
                    ConcreteGamma = 26.0;
                }
                else if (PileBodyType == "鋼管杭")
                {
                    PileSectionType = "鋼管杭";
                    ConcreteOutDia = 0.0;
                    MainBarNum = 0;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    // SteelPipePilesの要素数チェック
                    if (SteelPipePiles.Count >= 5)
                        SelectedSteelPipePileName = $"{SteelPipePiles[^5].Diameter}x{SteelPipePiles[^5].Thickness}";
                    else if (SteelPipePiles.Count > 0)
                        SelectedSteelPipePileName = $"{SteelPipePiles[0].Diameter}x{SteelPipePiles[0].Thickness}";
                    else
                        SelectedSteelPipePileName = string.Empty;
                    RecalculateSelectedSteelPipePipe();
                    RecalculatePileDia();
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"断面プロパティのリセット中にエラーが発生しました。\n{ex.Message}", "断面リセットエラー", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        // コンクリート肉厚
        private double _concreteThickness;
        public double ConcreteThickness
        {
            get => _concreteThickness;
            set
            {
                double safeValue = value < 0 ? 0 : value;
                if (SetProperty(ref _concreteThickness, safeValue))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリート基準強度
        private double _concreteFc = 27.0;
        public double ConcreteFc
        {
            get => _concreteFc;
            set
            {
                // 物理的に不正な値を防ぐ
                double safeValue = value < 12 ? 12 : value;
                if (SetProperty(ref _concreteFc, safeValue))
                {
                    RecalculateConcreteE();
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリートプレストレス
        private double _prestress;
        public double Prestress
        {
            get => _prestress;
            set => SetProperty(ref _prestress, value);
        }

        // コンクリート単位体積重量
        private double _concreteGamma = 23.0;
        public double ConcreteGamma
        {
            get => _concreteGamma;
            set
            {
                if (SetProperty(ref _concreteGamma, value))
                {
                    RecalculateConcreteE();
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリート現場管理係数
        private double _concreteGsi = 1.00;
        public double ConcreteGsi
        {
            get => _concreteGsi;
            set
            {
                if (SetProperty(ref _concreteGsi, value))
                {
                    RecalculateConcreteE();
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリート縦弾性係数 N/mm2
        private double _concreteE;
        public double ConcreteE
        {
            get => _concreteE;
            set => SetProperty(ref _concreteE, value);
        }

        // コンクリートのヤング係数の計算メソッド
        public void RecalculateConcreteE()
        {
            ConcreteE = 3.35 * Math.Pow(10, 4) * Math.Pow(ConcreteGamma / 24.0, 2.0) * Math.Pow(ConcreteGsi * ConcreteFc / 60.0, 1.0 / 3.0);
        }

        // コンクリートプレストレス N/mm2
        private double _concreteSigmaE;
        public double ConcreteSigmaE
        {
            get => _concreteSigmaE;
            set => SetProperty(ref _concreteSigmaE, value);
        }

        // 選択したPrecast杭
        private PrecastPile _selectedPrecastPile = new();
        public PrecastPile SelectedPrecastPile
        {
            get => _selectedPrecastPile;
            set => SetProperty(ref _selectedPrecastPile, value);
        }

        public SteelPipePile SelectedSteelPipePile = new();

        // 鋼管杭リスト（静的キャッシュを参照）
        private static List<SteelPipePile> SteelPipePiles => _cachedSteelPipePiles.Value;
        readonly PrecastPileLoader precastPileLoader = new();

        // クラス PileSection 内に追加するメソッド
        public void SetSelectedPrecastPileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            // 既に同じ選択なら何もしない
            if (SelectedPrecastPile?.Name == name) return;

            // 名前をセットして再計算
            SelectedPrecastPile = new PrecastPile { Name = name };
            RecalculateSelectedPrecastPile();

            // 再計算結果に基づき関連値を更新して表示を整える
            RecalculatePileDia();
            RecalculateConcreteE();
            SetSpecs();

            // キャッシュ無効化（必要に応じて）
            InvalidateAllCaches();

            // 通知（SelectedPrecastPile と他プロパティの変更を View に反映）
            OnPropertyChanged(nameof(SelectedPrecastPile));
            OnPropertyChanged(nameof(PileDiameter));
            OnPropertyChanged(nameof(ConcreteOutDia));
        }

        public void RecalculateSelectedPrecastPile()
        {
            List<PrecastPile> precastPiles = [];
            if (PileSectionType == "PHC杭") { precastPiles = PHCs; }
            else if (PileSectionType == "PRC杭") { precastPiles = PRCs; }
            else if (PileSectionType == "SC杭") { precastPiles = SCs; }

            bool isFound = false;

            foreach (PrecastPile pipe in precastPiles)
            {
                if (SelectedPrecastPile.Name == pipe.Name)
                {
                    SelectedPrecastPile = new PrecastPile
                    {
                        No = pipe.No,
                        ThicknessType = pipe.ThicknessType,
                        PrestressType = pipe.PrestressType,
                        Name = pipe.Name,
                        PileType = pipe.PileType,
                        PileDiameter = pipe.PileDiameter,
                        PileThickness = pipe.PileThickness,
                        Fc = pipe.Fc,
                        SFc = pipe.SFc,
                        Fbc = pipe.Fbc,
                        SigmaE = pipe.SigmaE,
                        Ec = pipe.Ec,
                        Ap = pipe.Ap,
                        Dp = pipe.Dp,
                        Ftp = pipe.Ftp,
                        SigmaPu = pipe.SigmaPu,
                        Ep = pipe.Ep,
                        HasReinf = pipe.HasReinf,
                        Nr = pipe.Nr,
                        RDesignation = pipe.RDesignation,
                        Ag = pipe.Ag,
                        Dr = pipe.Dr,
                        Ftr = pipe.Ftr,
                        Er = pipe.Er,
                        Ts = pipe.Ts,
                        Fts = pipe.Fts,
                        Es = pipe.Es,
                        PsSigmaY = pipe.PsSigmaY
                    };
                    PileDiameter = pipe.PileDiameter;
                    ConcreteOutDia = pipe.PileDiameter - pipe.Ts * 2.0;
                    ConcreteThickness = pipe.PileThickness - pipe.Ts;
                    TendonDp = pipe.Dp;
                    MainBarNum = pipe.Nr;
                    MainBarSize = pipe.RDesignation;
                    MainBarDr = pipe.Dr;
                    MainBarCenterCover = (ConcreteOutDia - MainBarDr) * 0.5;
                    PipeDia = pipe.PileDiameter;
                    PipeTs = pipe.Ts;
                    ConcreteFc = pipe.Fc;
                    Prestress = pipe.SigmaE;
                    ConcreteE = pipe.Ec;
                    TendonAp = pipe.Ap;
                    TendonEp = pipe.Ep;
                    TendonSigmaPy = pipe.Ftp;
                    TendonSigmaPu = pipe.SigmaPu;
                    MainBarAg = pipe.Ag;
                    MainBarEr = pipe.Er;
                    PipeEs = pipe.Es;
                    HoopPsSigmay = pipe.PsSigmaY;

                    isFound = true;
                    break;
                }
            }

            if (!isFound)
            {
                // 一致するものが見つからなかった場合の処理
                Console.WriteLine($"Error: SelectedPrecastPile.Name '{SelectedPrecastPile.Name}' not found in precastPiles.");
                // 必要に応じてデフォルト値を設定するなどの処理を追加
            }
        }

        // 選択したS杭
        private string _selectedSteelPipePileName;
        public string SelectedSteelPipePileName
        {
            get => _selectedSteelPipePileName;
            set
            {
                if (SetProperty(ref _selectedSteelPipePileName, value))
                {
                    RecalculateSelectedSteelPipePipe();
                }
            }
        }

        // S杭選択時のメソッド
        private void RecalculateSelectedSteelPipePipe()
        {
            foreach (var pipe in SteelPipePiles)
            {
                if (SelectedSteelPipePileName == $"{pipe.Diameter}x{pipe.Thickness}")
                {
                    SelectedSteelPipePile = pipe;
                    PipeDia = pipe.Diameter;
                    PipeTs = pipe.Thickness;

                    break;
                }
            }
        }

        // PC鋼線断面積
        private double _tendonAp;
        public double TendonAp
        {
            get => _tendonAp;
            set => SetProperty(ref _tendonAp, value);
        }

        // PC鋼線配置直径断面積
        private double _tendonDp;
        public double TendonDp
        {
            get => _tendonDp;
            set => SetProperty(ref _tendonDp, value);
        }

        // PC鋼線降伏強さ
        private double _tendonSigmaPy;
        public double TendonSigmaPy
        {
            get => _tendonSigmaPy;
            set => SetProperty(ref _tendonSigmaPy, value);
        }

        // PC鋼線引張強さ
        private double _tendonSigmaPu;
        public double TendonSigmaPu
        {
            get => _tendonSigmaPu;
            set => SetProperty(ref _tendonSigmaPu, value);
        }

        // PC鋼線縦弾性係数
        private double _tendonEp;
        public double TendonEp
        {
            get => _tendonEp;
            set => SetProperty(ref _tendonEp, value);
        }

        // 鉄筋径
        public string[] MainBarSizeOption { get; } =
        [
            "D10","D13","D16","D19","D22","D25","D29","D32","D35","D38","D41"
        ];

        // 鉄筋規格
        public string[] MainBarSpecOption { get; } =
        [
            "SD295", "SD345", "SD390", "SD490"
        ];


        // 鋼管規格オプション
        public string[] PipeGradeOption { get; } =
        [
            "SKK400",
            "SKK490"
        ];

        // PHC（静的キャッシュを参照）
        public static ObservableCollection<string> PHCOption => _cachedPHCOption.Value;
        public static List<PrecastPile> PHCs => _cachedPHCs.Value;

        // PRC（静的キャッシュを参照）
        public static ObservableCollection<string> PRCOption => _cachedPRCOption.Value;
        public static List<PrecastPile> PRCs => _cachedPRCs.Value;

        // SC（静的キャッシュを参照）
        public static ObservableCollection<string> SCOption => _cachedSCOption.Value;
        public static List<PrecastPile> SCs => _cachedSCs.Value;

        // 鋼管（静的キャッシュを参照）
        public static ObservableCollection<string> SteelPipeOption => _cachedSteelPipeOption.Value;


        // 鉄筋径
        private string _mainBarSize = "D29";
        public string MainBarSize
        {
            get => _mainBarSize;
            set
            {
                if (SetProperty(ref _mainBarSize, value))
                {
                    MainBarAg = MainBarNum * GetBarArea(MainBarSize);
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum = 16;
        public int MainBarNum
        {
            get => _mainBarNum;
            set
            {
                int safeValue = value < 0 ? 0 : value;
                if (SetProperty(ref _mainBarNum, safeValue))
                {
                    MainBarAg = MainBarNum * GetBarArea(MainBarSize);
                    InvalidateAllCaches();
                }
            }
        }

        private static readonly Dictionary<string, double> BarAreas = new()
        {
            ["D10"] = 71.3,
            ["D13"] = 127.0,
            ["D16"] = 199.0,
            ["D19"] = 287.0,
            ["D22"] = 387.0,
            ["D25"] = 507.0,
            ["D29"] = 642.0,
            ["D32"] = 794.0,
            ["D35"] = 957.0,
            ["D38"] = 1140.0,
            ["D41"] = 1340.0,
            ["D51"] = 2027.0
        };

        internal static double GetBarArea(string barSize)
            => BarAreas.TryGetValue(barSize, out var area) ? area : 0.0;


        // 鉄筋断面積 mm2
        private double _mainBarAg;
        public double MainBarAg
        {
            get => _mainBarAg;
            set
            {
                if (SetProperty(ref _mainBarAg, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec = "SD390";
        public string MainBarSpec
        {
            get => _mainBarSpec;
            set
            {
                if (SetProperty(ref _mainBarSpec, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋配置直径
        private double _mainBarDr;
        public double MainBarDr
        {
            get => _mainBarDr;
            set
            {
                if (SetProperty(ref _mainBarDr, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr;
        public double MainBarFtr
        {
            get => _mainbarFtr;
            set => SetProperty(ref _mainbarFtr, value);
        }

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover = 200.0;
        public double MainBarCenterCover
        {
            get => _mainbarCenterCover;
            set
            {
                if (SetProperty(ref _mainbarCenterCover, value))
                {
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋縦弾性係数
        private double _mainbarEr = 205_000;
        public double MainBarEr
        {
            get => _mainbarEr;
            set
            {
                if (SetProperty(ref _mainbarEr, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋径
        private string _hoopSize = "D13";
        public string HoopSize
        {
            get => _hoopSize;
            set
            {
                if (SetProperty(ref _hoopSize, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋間隔
        private double _hoopSpacing = 150.0;
        public double HoopSpacing
        {
            get => _hoopSpacing;
            set
            {
                double safeValue = value < 10 ? 10 : value;
                if (SetProperty(ref _hoopSpacing, safeValue))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋規格
        private string _hoopSpec = "SD295";
        public string HoopSpec
        {
            get => _hoopSpec;
            set
            {
                if (SetProperty(ref _hoopSpec, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋重心かぶり
        private double _hoopCenterCover = 150.0;
        public double HoopCenterCover
        {
            get => _hoopCenterCover;
            set
            {
                if (SetProperty(ref _hoopCenterCover, value))
                {
                    InvalidateAllCaches();
                }
            }
        }
        // せん断補強筋比
        //private double _hoopPw;
        public double HoopPw => 2 * GetBarArea(HoopSize) / (Math.PI / 4.0 * ConcreteOutDia) / HoopSpacing;


        // せん断補強筋降伏点
        //private double _hoopSigmay;
        public double HoopSigmay
        {
            get /*=> _hoopPw;*/
            {
                if (HoopSpec == "SD295") return 295;
                else if (HoopSpec == "SD345") return 345;
                else if (HoopSpec == "SD390") return 390;
                else if (HoopSpec == "SD490") return 490;
                else return 0;
            }
        }

        // せん断補強筋降伏点
        private double _hoopPsSigmay;
        public double HoopPsSigmay
        {
            get => _hoopPsSigmay;
            set => SetProperty(ref _hoopPsSigmay, value);
        }

        // 鋼管径
        private double _pipeDia = 0.0;
        public double PipeDia
        {
            get => _pipeDia;
            set
            {
                if (_pipeDia != value)
                {
                    _pipeDia = value;
                    OnPropertyChanged(nameof(PipeDia));
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管厚
        private double _pipeTs = 0.0;
        public double PipeTs
        {
            get => _pipeTs;
            //set => SetProperty(ref _pipeTs, value);
            set
            {
                if (_pipeTs != value)
                {
                    _pipeTs = value;
                    OnPropertyChanged(nameof(PipeTs));
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管断面積
        public double PipeAs => PipeTs * (PipeDia - PipeTs) * Math.PI;

        // 鋼管降伏点

        private double _pipeFts;
        public double PipeFts
        {
            get => _pipeFts;
            set
            {
                if (SetProperty(ref _pipeFts, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管規格
        private string _pipeGrade = "SKK490";
        public string PipeGrade
        {
            get => _pipeGrade;
            set
            {
                if (SetProperty(ref _pipeGrade, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管縦弾性係数 (N/mm2)
        private double _pipeEs = 205000.0;
        public double PipeEs
        {
            get => _pipeEs;
            set
            {
                if (SetProperty(ref _pipeEs, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 杭全断面積(mm2)
        public double A0 => Ac + MainBarAg + TendonAp + PipeAs;

        // 杭コンクリート断面積(mm2)
        public double Ac => (ConcreteOutDia - ConcreteThickness) * Math.PI * ConcreteThickness - MainBarAg - TendonAp;

        // 杭単位長さ重量 (kN/m)
        public double W => ((MainBarAg + TendonAp + PipeAs) * 78.5 + (Ac - (MainBarAg + TendonAp)) * ConcreteGamma) * Math.Pow(10, -6);

        // 軸剛性 (kN)
        public double EA => (ConcreteE * Ac + MainBarEr * MainBarAg + TendonEp * TendonAp) * 0.001;

        // 曲げ剛性 (kNm2)
        // 鉄筋の換算断面二次モーメント項は MainBarAg（鉄筋断面積）を使用
        // ※ A0（全断面積）を使用すると過大評価になるので注意
        // ※ コンクリート断面は中空断面（ConcreteOutDia - 2*ConcreteThickness = 内径）として計算
        public double EI => (ConcreteE * (Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 64.0
            + 0.5 * (MainBarEr / ConcreteE - 1) * MainBarAg * Math.Pow((ConcreteOutDia - 2 * MainBarCenterCover), 2) / 4.0)
            + PipeEs * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 64.0) * Math.Pow(10, -9);

        // ねじり剛性 (kNm2)
        // ※ コンクリート断面は中空断面として計算
        public double GJ => (GetG(ConcreteE, 0.2) * Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 64.0 +
            GetG(PipeEs, 0.3) * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 64.0) * Math.Pow(10, -9);

        // せん断剛性
        private static double GetG(double e, double nu)
        {
            return e / (2 * (1 + nu));
        }

        private ObservableCollection<Spec> _selectedPileSectionSpecification;
        public ObservableCollection<Spec> SelectedPileSectionSpecification
        {
            //get => _selectedPileSectionSpecification;
            get => _selectedPileSectionSpecification ??= [];
            set => SetProperty(ref _selectedPileSectionSpecification, value);
        }

        // コンストラクタ
        public PileSection()
        {
            // 初期化処理
            SelectedPrecastPile = new PrecastPile();

            // PHCs, PRCs, SCs, SteelPipeOption は静的キャッシュのプロパティから取得
            // （コンストラクタでの初期化は不要）

            RecalculatePileDia();
            RecalculateConcreteE();
            SetSpecs();
            MainBarAg = MainBarNum * GetBarArea(MainBarSize);
        }

        public void SetSpecs()
        {
            SelectedPileSectionSpecification = [
                new Spec("杭断面タイプ", "", PileSectionType, ""),
                new Spec("杭径", "D", $"{PileDiameter:N0}", "mm")];

            if (PileSectionType == "鋼管コンクリート部" || PileSectionType == "SC杭" || PileSectionType == "鋼管杭")
            {
                string notePipeDia =
                    (PileSectionType == "鋼管コンクリート部" && PipeDia > 2700) ? "([強度と変形性能]4.1,3)2700より大" :
                    (PileSectionType == "鋼管コンクリート部" && PipeDia < 600) ? "([強度と変形性能]4.1,3)600未満" :
                    (PileSectionType == "鋼管杭" && PipeDia > 2000) ? "([強度と変形性能]8.1.4(1))2000より大" :
                    (PileSectionType == "鋼管杭" && PipeDia < 318.5) ? "([強度と変形性能]8.1.4(1))318.5未満" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管外径", "PipeDia", $"{PipeDia:N0}", "mm", notePipeDia));

                string notePipeTs =
                    (PileSectionType == "鋼管コンクリート部" && PipeTs < 6) ? "([強度と変形性能]4.1,3)6未満" :
                    (PileSectionType == "鋼管杭" && PipeTs > 6) ? "([強度と変形性能]8.1.4(2))6未満" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管厚", "Ts", $"{PipeTs:N0}", "mm", notePipeTs));
                string noteDonTs =
                    (PileSectionType == "鋼管コンクリート部" && PipeDia / PipeTs > 125) ? "([強度と変形性能]4.1,3)125より大" :
                    (PileSectionType == "鋼管杭" && PipeDia / PipeTs > 100) ? "([強度と変形性能]8.1.4(2))100より大" : "";
                string pipeDiaTsValue = (PipeTs != 0) ? $"{PipeDia / PipeTs:N1}" : "N/A";
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管径厚比", "Tc/Ts", pipeDiaTsValue, "", noteDonTs));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管断面積", "As", $"{PipeAs:N0}", "mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管規格", "", PipeGrade, ""));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管ヤング係数", "Es", $"{PipeEs:N0}", "N/mm2"));
            }

            if (PileSectionType != "鋼管杭")
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート外径", "Dc", $"{ConcreteOutDia:N0}", "mm"));

            if (PileSectionType == "PHC杭" || PileSectionType == "PRC杭" || PileSectionType == "SC杭")
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート肉厚", "Dt", $"{ConcreteThickness:N0}", "mm"));

            if (PileSectionType != "鋼管杭")
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート断面積", "Ac", $"{Ac:N0}", "mm2"));
                string noteFc =
                    PileSectionType == "鉄筋コンクリート部" && ConcreteFc < 21 ? "([強度と変形性能]3.1,2)21N/mm2未満" :
                    PileSectionType == "鉄筋コンクリート部" && ConcreteFc > 40 ? "([強度と変形性能]3.1,2)40N/mm2より大" :
                    PileSectionType == "鋼管コンクリート部" && ConcreteFc < 21 ? "([強度と変形性能]4.1,2)21N/mm2未満" :
      PileSectionType == "鋼管コンクリート部" && ConcreteFc > 40 ? "([強度と変形性能]4.1,2)40N/mm2より大" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート基準強度", "Fc", $"{ConcreteFc:N0}", "N/mm2", noteFc));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート単位体積重量", "γc", $"{ConcreteGamma:N1}", "kN/m3"));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート縦弾性係数", "Ec", $"{ConcreteE:N0}", "N/mm2"));
            }

            if (PileBodyType == "場所打ち鉄筋コンクリート杭" || PileBodyType == "場所打ち鋼管コンクリート杭")
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート施工品質管理係数", "ξ", $"{ConcreteGsi:N2}", ""));
            }

            if (PileSectionType == "PHC杭" || PileSectionType == "PRC杭")
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリートプレストレス", "σe", $"{Prestress:N1}", "N/mm2"));
            }

            if (PileSectionType == "PHC杭" || PileSectionType == "PRC杭")
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材断面積", "Ap", $"{TendonAp:N0}", "mm2"));

                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材降伏点", "σpy", $"{TendonSigmaPy:N0}", "N/mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材最大耐力", "σpu", $"{TendonSigmaPu:N0}", "N/mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材ヤング係数", "Ep", $"{TendonEp:N0}", "N/mm2"));
            }

            if (PileSectionType == "鉄筋コンクリート部" || PileSectionType == "PRC杭" ||
                (PileSectionType == "鋼管コンクリート部" && MainBarAg > 0))
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋数-呼び径", "", MainBarNum.ToString() + "-" + MainBarSize, ""));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋規格", "", $"{MainBarSpec}", ""));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋断面積", "Ag", $"{MainBarAg:N0}", "mm2"));
                string notePg =
                    PileSectionType == "鉄筋コンクリート部" && MainBarAg / A0 * 100 < 0.4 ? "([強度と変形性能]3.1,5(5))0.4%未満" : "";
                string mainBarRatio = (A0 != 0) ? $"{MainBarAg / A0 * 100:N2}" : "N/A";
                SelectedPileSectionSpecification.Add(
                    new Spec("主筋比", "pg", mainBarRatio, "%", notePg));
                //SelectedPileSectionSpecification.Add(
                //    new Spec("鉄筋規格降伏点", "Ftr", $"{MainBarFtr:N0}", "mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋重心かぶり厚", "", $"{MainBarCenterCover:N0}", "mm"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋ヤング係数", "Er", $"{MainBarEr:N0}", "N/mm2"));
            }

            if (PileSectionType == "鉄筋コンクリート部" || PileSectionType == "PRC杭")
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("せん断補強筋規格", "", $"{HoopSpec}", ""));

                if (PileSectionType == "PRC杭")
                {
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋比×降伏点", "ps・σy", $"{HoopPsSigmay:N2}", "N/mm2"));
                }
                else
                {
                    string noteHoopSpacing =
                        HoopSpacing > 300 ? "([強度と変形性能]3.1,5(6))300より大" : "";
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋呼び径@間隔", "", $"{HoopSize}@{HoopSpacing}", "", noteHoopSpacing));
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋降伏点", "σy", $"{HoopSigmay:N0}", "N/mm2"));
                    string noteHoopPw =
                        PileSectionType == "鉄筋コンクリート部" && HoopPw * 100 < 0.1 ? "([強度と変形性能]3.1,5(6))0.1%未満" : "";
                    string hoopPwValue = (HoopSpacing != 0 && ConcreteOutDia != 0) ? $"{HoopPw * 100:N2}" : "N/A";
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋比", "pw", hoopPwValue, "%", noteHoopPw));
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋重心かぶり厚", "", $"{HoopCenterCover:N0}", "mm"));
                }
            }

            //new Spec("PCリング定着筋呼び径", "", BarSize, ""),

            SelectedPileSectionSpecification.Add(
               new Spec("杭の全断面積", "A0", $"{A0:N0}", "mm2"));

            //SelectedPileSectionSpecification.Add(
            //    new Spec("杭等価断面積", "Ae", $"{Ae:N0}", "mm2"));
            //SelectedPileSectionSpecification.Add(
            //    new Spec("杭等価断面係数", "Ze", $"{Ze:N0}", "mm3"));
            SelectedPileSectionSpecification.Add(
                new Spec("杭の単位長さ重量", "W", $"{W:N2}", "kN/m"));
            SelectedPileSectionSpecification.Add(
                new Spec("杭の弾性軸剛性", "EA", $"{EA:N0}", "kN"));
            SelectedPileSectionSpecification.Add(
                new Spec("杭の弾性曲げ剛性", "EI", $"{EI:N0}", "kNm2"));
            //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            //return specs;
        }

        // List<double>型データの構成要素すべてに係数を乗ずるメソッド
        internal static List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            if (originalList == null)
                return [];
            List<double> result = [];
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        private (List<double> N, List<double> M) GetNMRaw(string propertyName)
        {
            var section = CreateSectionCalculator();
            if (section == null)
                return ([], []);

            // 軸力閾値を更新
            UltimateLimitAxialForceThresholds = [.. section.UltimateLimitAxialForceThresholds];

            // プロパティ名に応じた NM を取得
            var (n, m, _, _) = propertyName switch
            {
                nameof(UnfactoredServiceNM) => section.UnfactoredServiceNM,
                nameof(UnfactoredDamageNM) => section.UnfactoredDamageNM,
                nameof(UnfactoredUltimateNM) => section.UnfactoredUltimateNM,
                nameof(FactoredServiceNM) => section.FactoredServiceNM,
                nameof(FactoredDamageNM) => section.FactoredDamageNM,
                nameof(FactoredUltimateNM) => section.FactoredUltimateNM,
                _ => (new List<double>(), new List<double>(), new List<double>(), new List<double>())
            };

            return (n, m);
        }

        /// <summary>
        /// NQ曲線を取得し、スケーリング後（kN, kN）で返す
        /// </summary>
        private (List<double> N, List<double> Q) GetNQScaled(string propertyName)
        {
            var section = CreateSectionCalculator();
            if (section is not AbstractPileSection absSection)
                return ([], []);

            // プロパティ名に応じた NQ を取得
            // 注意: AbstractPileSection の NQ プロパティは (Q, N) の順で格納されている
            var (q, n) = propertyName switch
            {
                nameof(UnfactoredServiceNQ) => absSection.UnfactoredServiceNQ,
                nameof(UnfactoredDamageNQ) => absSection.UnfactoredDamageNQ,
                nameof(UnfactoredUltimateNQ) => absSection.UnfactoredUltimateNQ,
                nameof(FactoredServiceNQ) => absSection.FactoredServiceNQ,
                nameof(FactoredDamageNQ) => absSection.FactoredDamageNQ,
                nameof(FactoredUltimateNQ) => absSection.FactoredUltimateNQ,
                _ => ((List<double>)[], (List<double>)[])
            };

            if (n == null || q == null)
                return ([], []);

            // N[N] -> kN, Q[N] -> kN にスケーリング
            // 戻り値は (N, Q) の順
            return (
                GetMultipliedListValues(n, 1e-3),
                GetMultipliedListValues(q, 1e-3)
            );
        }

        //private (List<double> N, List<double> M) GetNMRaw(string propertyName)
        //{
        //    if (PileBodyType == "場所打ち鉄筋コンクリート杭")
        //    {
        //        var insituConcrete = new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc);
        //        var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //        var pileSection = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

        //        //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //        UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];


        //        (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //        {
        //            "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //            "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //            "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //            "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //            "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //            "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //            _ => ([], [], [], [])
        //        };

        //        return (n, m);
        //    }
        //    else if (PileBodyType == "場所打ち鋼管コンクリート杭")
        //    {
        //        if (PileSectionType == "鉄筋コンクリート部")
        //        {
        //            var insituConcrete = new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc);
        //            var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //            var pileSection = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

        //            //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //            UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];

        //            (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //            {
        //                "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //                "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //                "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //                "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //                "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //                "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //                _ => ([], [], [], [])
        //            };

        //            return (n, m);
        //        }
        //        else if (PileSectionType == "鋼管コンクリート部")
        //        {
        //            var insituSteelPipe = new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth);
        //            var insituConcrete = new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc);
        //            var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //            var pileSection = new InsituSteelPipeReinforcedConcreteSection(insituSteelPipe, insituConcrete, mainBars);

        //            //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //            UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];

        //            (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //            {
        //                "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //                "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //                "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //                "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //                "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //                "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //                _ => ([], [], [], [])
        //            };

        //            return (n, m);
        //        }
        //    }
        //    else if (PileBodyType == "既製コンクリート杭" && PileSectionType == "PHC杭" && TendonAp > 0 && PileDiameter != 2 * ConcreteThickness)
        //    {
        //        var precastConcrete = new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc);
        //        var tendons = new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu);
        //        var pileSection = new PHCSection(precastConcrete, tendons, Prestress);

        //        //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //        UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];

        //        (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //        {
        //            "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //            "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //            "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //            "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //            "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //            "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //            _ => ([], [], [], [])
        //        };

        //        return (n, m);
        //    }
        //    else if (PileBodyType == "既製コンクリート杭" && PileSectionType == "PRC杭" && TendonAp > 0 && PileDiameter != 2 * ConcreteThickness)
        //    {
        //        var precastConcrete = new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc);
        //        var mainBars = new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize);
        //        var tendons = new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu);
        //        var pileSection = new PRCSection(precastConcrete, mainBars, tendons, Prestress);

        //        //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //        UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];

        //        (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //        {
        //            "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //            "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //            "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //            "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //            "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //            "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //            _ => ([], [], [], [])
        //        };

        //        return (n, m);
        //    }
        //    else if (PileBodyType == "既製コンクリート杭" && PileSectionType == "SC杭" && PileDiameter != 2 * ConcreteThickness)
        //    {
        //        var precastConcrete = new PrecastSCConcrete(PileDiameter - 2 * PipeTs, PileDiameter - 2 * PipeTs - 2 * ConcreteThickness, ConcreteFc);
        //        var steelPipe = new PrecastSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth);
        //        var pileSection = new SCSection(precastConcrete, steelPipe);

        //        //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
        //        UltimateLimitAxialForceThresholds = [.. pileSection.UltimateLimitAxialForceThresholds];

        //        (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
        //        {
        //            "UnfactoredServiceNM" => pileSection.UnfactoredServiceNM,
        //            "UnfactoredDamageNM" => pileSection.UnfactoredDamageNM,
        //            "UnfactoredUltimateNM" => pileSection.UnfactoredUltimateNM,
        //            "FactoredServiceNM" => pileSection.FactoredServiceNM,
        //            "FactoredDamageNM" => pileSection.FactoredDamageNM,
        //            "FactoredUltimateNM" => pileSection.FactoredUltimateNM,
        //            _ => ([], [], [], [])
        //        };

        //        return (n, m);
        //    }

        //    return ([], []);
        //}

        // 浅いコピーを作成するメソッド
        public PileSection ShallowCopy()
        {
            return (PileSection)this.MemberwiseClone();
        }

        // 深いコピーを作成するメソッド
        public PileSection DeepCopy()
        {
            // 浅いコピーを作成してから、参照型フィールドを個別に複製する
            var copy = (PileSection)this.MemberwiseClone();

            // SelectedPrecastPile をコピー (null 安全)
            if (this.SelectedPrecastPile != null)
            {
                copy.SelectedPrecastPile = new PrecastPile
                {
                    No = this.SelectedPrecastPile.No,
                    ThicknessType = this.SelectedPrecastPile.ThicknessType,
                    PrestressType = this.SelectedPrecastPile.PrestressType,
                    Name = this.SelectedPrecastPile.Name,
                    PileType = this.SelectedPrecastPile.PileType,
                    PileDiameter = this.SelectedPrecastPile.PileDiameter,
                    PileThickness = this.SelectedPrecastPile.PileThickness,
                    Fc = this.SelectedPrecastPile.Fc,
                    SFc = this.SelectedPrecastPile.SFc,
                    Fbc = this.SelectedPrecastPile.Fbc,
                    SigmaE = this.SelectedPrecastPile.SigmaE,
                    Ec = this.SelectedPrecastPile.Ec,
                    Ap = this.SelectedPrecastPile.Ap,
                    Dp = this.SelectedPrecastPile.Dp,
                    Ftp = this.SelectedPrecastPile.Ftp,
                    SigmaPu = this.SelectedPrecastPile.SigmaPu,
                    Ep = this.SelectedPrecastPile.Ep,
                    HasReinf = this.SelectedPrecastPile.HasReinf,
                    Nr = this.SelectedPrecastPile.Nr,
                    RDesignation = this.SelectedPrecastPile.RDesignation,
                    Ag = this.SelectedPrecastPile.Ag,
                    Dr = this.SelectedPrecastPile.Dr,
                    Ftr = this.SelectedPrecastPile.Ftr,
                    Er = this.SelectedPrecastPile.Er,
                    Ts = this.SelectedPrecastPile.Ts,
                    Fts = this.SelectedPrecastPile.Fts,
                    Es = this.SelectedPrecastPile.Es,
                    PsSigmaY = this.SelectedPrecastPile.PsSigmaY
                };
            }
            else
            {
                copy.SelectedPrecastPile = new PrecastPile();
            }

            // SelectedSteelPipePile をコピー
            copy.SelectedSteelPipePile = new SteelPipePile
            {
                Diameter = this.SelectedSteelPipePile?.Diameter ?? 0.0,
                Thickness = this.SelectedSteelPipePile?.Thickness ?? 0.0
            };

            // SelectedPileSectionSpecification の複製（仕様表示のコレクション）
            if (this.SelectedPileSectionSpecification != null)
            {
                copy.SelectedPileSectionSpecification = new ObservableCollection<Spec>(
                    this.SelectedPileSectionSpecification.Select(s => new Spec(s.Item, s.Mark, s.Value, s.Unit, s.Note))
                );
            }
            else
            {
                copy.SelectedPileSectionSpecification = [];
            }

            // キャッシュ系はコピーせず再計算させる（安全のため null にしておく）
            copy._unfactoredServiceNMCache = null;
            copy._unfactoredDamageNMCache = null;
            copy._unfactoredUltimateNMCache = null;
            copy._factoredServiceNMCache = null;
            copy._factoredDamageNMCache = null;
            copy._factoredUltimateNMCache = null;
            copy._steelYieldNMRawCache = null;
            copy._crackNMRawCache = null;

            return copy;
        }

        /// <summary>
        /// 現在の断面パラメータに基づいて断面計算オブジェクトを生成します。
        /// </summary>
        /// <returns>断面計算オブジェクト。生成できない場合は null。</returns>
        private IPileSectionCalculation? CreateSectionCalculator()
        {
            return (PileBodyType, PileSectionType) switch
            {
                // 場所打ちRC杭
                ("場所打ち鉄筋コンクリート杭", _) =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // 場所打ち鋼管RC杭 - RC部
                ("場所打ち鋼管コンクリート杭", "鉄筋コンクリート部") =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // 場所打ち鋼管RC杭 - 鋼管RC部
                ("場所打ち鋼管コンクリート杭", "鋼管コンクリート部") =>
                    new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth),
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // PHC杭
                ("既製コンクリート杭", "PHC杭") when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC杭
                ("既製コンクリート杭", "PRC杭") when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PRCSection(
                        new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // SC杭
                ("既製コンクリート杭", "SC杭") when PileDiameter != 2 * ConcreteThickness =>
                    new SCSection(
                        new PrecastSCConcrete(PileDiameter - 2 * PipeTs, PileDiameter - 2 * PipeTs - 2 * ConcreteThickness, ConcreteFc),
                        new PrecastSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth)),

                // 鋼管杭（未実装の場合は null）
                ("鋼管杭", _) => null,

                _ => null
            };
        }
        public static void DebugDumpProperties()
        {
        }
    }
}
