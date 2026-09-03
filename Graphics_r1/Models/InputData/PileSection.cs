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
    public class PileSection : BaseModel
    {
        // 静的キャッシュ（CSVデータは一度だけ読み込む）
        // PHC杭 は JIS 汎用ライブラリに加え、メーカー製品ライブラリを連結する。
        // ストレート杭は断面の挙動が PHC杭 と完全に同じなので、断面タイプを増やさず
        // 同じ製品一覧に並べる (節杭のように形状・自重が違うものだけ断面タイプを分ける)。
        private static readonly Lazy<List<PrecastPile>> _cachedPHCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_PHC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_PHC_MSHI105.csv")]);
        // PRC杭 も PHC杭 / SC杭 と同様、JIS 汎用ライブラリにメーカー製品ライブラリを連結する。
        private static readonly Lazy<List<PrecastPile>> _cachedPRCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_PRC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_PRC_DAM105.csv")]);
        // SC杭 も PHC杭 と同様、JIS 汎用ライブラリにメーカー製品ライブラリを連結する。
        private static readonly Lazy<List<PrecastPile>> _cachedSCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_SC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_SC_HISC105.csv")]);
        private static readonly Lazy<List<SteelPipePile>> _cachedSteelPipePiles = new(() => LoadSteelPipePilesFromCsv("pile_library_SteelPile.csv"));
        // 節杭は Do/質量など既製杭 DTO に無い列を持つため専用ローダーで読み、
        // 断面転記の直前に PrecastPile へ詰め替える (NodularPile.ToPrecastPile)。
        private static readonly Lazy<List<NodularPile>> _cachedNodularPiles = new(LoadNodularPiles);
        private static readonly Lazy<List<NodularPileHead>> _cachedNodularPileHeads = new(LoadNodularPileHeads);
        // PRC節杭は 1 製品が PRC部 / PHC部 の 2 断面を持つので、同じライブラリから
        // 詰め替え先を 2 系統作る (ToPrecastPile(phcPart:) の引数だけが違う)。
        private static readonly Lazy<List<NodularPrcPile>> _cachedNodularPrcPiles = new(LoadNodularPrcPiles);
        private static readonly Lazy<List<NodularPrcPileHead>> _cachedNodularPrcPileHeads = new(LoadNodularPrcPileHeads);
        // BF.S は 1 製品が頭部軸部 / 先端軸部 の 2 断面を持つ (外径・肉厚が違う)。
        private static readonly Lazy<List<BfsPile>> _cachedBfsPiles = new(LoadBfsPiles);

        // オプションリストも静的キャッシュ（一度だけ構築）
        private static readonly Lazy<ObservableCollection<string>> _cachedPHCOption = new(() =>
            new ObservableCollection<string>(_cachedPHCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedPRCOption = new(() =>
            new ObservableCollection<string>(_cachedPRCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSCOption = new(() =>
            new ObservableCollection<string>(_cachedSCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSteelPipeOption = new(() =>
            new ObservableCollection<string>(_cachedSteelPipePiles.Value.Select(p => $"{p.Diameter}x{p.Thickness}")));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPileOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPrcOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPrcPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPrcPhcPartOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPrcPiles.Value.Select(p => p.PhcPartDisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedBfsHeadOption = new(() =>
            new ObservableCollection<string>(_cachedBfsPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedBfsTipOption = new(() =>
            new ObservableCollection<string>(_cachedBfsPiles.Value.Select(p => p.TipDisplayName)));
        // 既製杭と同じ転記処理に載せるための詰め替え済みリスト (毎回変換しないようキャッシュ)
        private static readonly Lazy<List<PrecastPile>> _cachedNodularAsPrecast = new(() =>
            [.. _cachedNodularPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedNodularPrcAsPrecast = new(() =>
            [.. _cachedNodularPrcPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedNodularPrcPhcPartAsPrecast = new(() =>
            [.. _cachedNodularPrcPiles.Value.Select(p => p.ToPrecastPile(phcPart: true))]);
        private static readonly Lazy<List<PrecastPile>> _cachedBfsHeadAsPrecast = new(() =>
            [.. _cachedBfsPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedBfsTipAsPrecast = new(() =>
            [.. _cachedBfsPiles.Value.Select(p => p.ToPrecastPile(tipPart: true))]);

        private static List<PrecastPile> LoadPrecastPilesFromCsv(string fileName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(baseDir, "Models", "PileLibrary", fileName);
                return PrecastPileLoader.LoadFromCsv(filePath) ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 杭ライブラリ読込失敗 ({fileName}): {ex.Message}");
                return [];
            }
        }

        private static List<NodularPile> LoadNodularPiles()
        {
            try
            {
                return NodularPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 節杭ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPileHead> LoadNodularPileHeads()
        {
            try
            {
                return NodularPileLoader.LoadDefaultHeads() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 節杭拡頭形状ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPrcPile> LoadNodularPrcPiles()
        {
            try
            {
                return NodularPrcPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] PRC節杭ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPrcPileHead> LoadNodularPrcPileHeads()
        {
            try
            {
                return NodularPrcPileLoader.LoadDefaultHeads() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] PRC節杭拡頭形状ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<BfsPile> LoadBfsPiles()
        {
            try
            {
                return BfsPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] BF.S パイルライブラリ読込失敗: {ex.Message}");
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
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 鋼管杭ライブラリ読込失敗 ({fileName}): {ex.Message}");
                return [];
            }
        }

        /// <summary>有効せい d [mm]（MonQd計算用）: d = 0.9D（基礎指針'19）</summary>
        public double EffectiveDepth => PileDiameter * 0.9;

        // フィールド
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        // --- NQプロパティ（計算プロパティ） ---
        // NQ Raw（N[N], Q[N]）を取得し、スケーリング後（kN, kN）を返す
        // [JsonIgnore]: NM 系と同じ理由で、これらも computed プロパティで重い計算をトリガする
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) UnfactoredServiceNQ =>
            GetOrComputeCurve(ref _unfactoredServiceNQCache, () => GetNQScaled(nameof(UnfactoredServiceNQ)));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) UnfactoredDamageNQ =>
            GetOrComputeCurve(ref _unfactoredDamageNQCache, () => GetNQScaled(nameof(UnfactoredDamageNQ)));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) UnfactoredUltimateNQ =>
            GetOrComputeCurve(ref _unfactoredUltimateNQCache, () => GetNQScaled(nameof(UnfactoredUltimateNQ)));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) FactoredServiceNQ =>
            GetOrComputeCurve(ref _factoredServiceNQCache, () => GetNQScaled(nameof(FactoredServiceNQ)));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) FactoredDamageNQ =>
            GetOrComputeCurve(ref _factoredDamageNQCache, () => GetNQScaled(nameof(FactoredDamageNQ)));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) FactoredUltimateNQ =>
            GetOrComputeCurve(ref _factoredUltimateNQCache, () => GetNQScaled(nameof(FactoredUltimateNQ)));

        // 後方互換性のためのエイリアス（Dagame -> Damage）
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> Q) UnfactoredDagameNQ => UnfactoredDamageNQ;
        [System.Text.Json.Serialization.JsonIgnore]
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
        private (List<double> N, List<double> M)? _factoredDamageNMLevel1Cache;
        private (List<double> N, List<double> M)? _factoredUltimateNMCache;

        /// <summary>
        /// NM / NQ の遅延キャッシュを直列化する、断面インスタンスごとのロック。
        ///
        /// 理由は 2 つあり、どちらも並列に読むと壊れる。
        /// ・キャッシュの器が <c>(List&lt;double&gt;, List&lt;double&gt;)?</c>（24 バイトの構造体）で、
        ///   書き込みがアトミックでない。「N は今回の計算・M は別の計算」という破れた値が読める。
        /// ・算出 (<c>GetNMRaw</c>) が純粋関数ではない。<c>UpdateSteelPipeAxialThresholds()</c> 等で
        ///   この断面の軸力閾値を<b>書き換えてから</b>、その閾値で曲線をクリップする。
        ///   同時に走ると互いの中間状態を掴み、クリップが狂った曲線ができる。
        ///
        /// 検定 (EvaluationService) は杭要素ごとに Parallel.For で回すため、
        /// 同じ断面のこれらのプロパティが同時に叩かれる。
        /// Monitor は再入可能なので、算出の途中で同じ断面の別プロパティを読んでも止まらない。
        /// </summary>
        private readonly object _curveCacheLock = new();

        /// <summary>遅延キャッシュの取得。読み・算出・書き込みをまとめてロックの中で行う。</summary>
        private (List<double> A, List<double> B) GetOrComputeCurve(
            ref (List<double> A, List<double> B)? cache,
            Func<(List<double> A, List<double> B)> compute)
        {
            lock (_curveCacheLock)
            {
                cache ??= compute();
                return cache.Value;
            }
        }

        // --- 追加: M-φキャッシュ（同一断面・同一軸力での再計算を抑制） ---
        // キーは断面プロパティハッシュ + 軸力(kN)を丸めた値
        private static readonly Dictionary<string, (List<double> Phis, List<double> Moments)> _mphiCache = [];
        private static readonly object _mPhiCacheLock = new();
        private static int _mPhiCacheHitCount = 0;
        private static int _mPhiCacheMissCount = 0;
        private static bool _mPhiCacheLimitLogged = false;

        /// <summary>
        /// 断面パラメータ変更時にすべてのキャッシュを一括で無効化します。
        /// </summary>
        /// <summary>
        /// この断面の NM / 降伏 / ひび割れキャッシュを外部から無効化します。
        /// コンクリートのモデル化オプション (ConcreteModelOptions) 変更時など、
        /// 断面プロパティ自体は変わらないが計算結果が変わるケースで使用します。
        /// </summary>
        public void InvalidateComputedCaches() => InvalidateAllCaches();

        private void InvalidateAllCaches()
        {
            InvalidateNMCache();
            InvalidateNQCache();
            InvalidateSteelYieldCache();
            InvalidateCrackCache();
        }
        private void InvalidateNMCache()
        {
            // 算出中に捨てられると、書き戻しと破棄が入れ違って古い曲線が残る
            lock (_curveCacheLock)
            {
                _unfactoredServiceNMCache = null;
                _unfactoredDamageNMCache = null;
                _unfactoredUltimateNMCache = null;
                _factoredServiceNMCache = null;
                _factoredDamageNMCache = null;
                _factoredDamageNMLevel1Cache = null;
                _factoredUltimateNMCache = null;
            }
        }
        // せん断 (N-Q) キャッシュ無効化。告示せん断オプション等の変更を反映するため NM と同時に破棄する。
        private void InvalidateNQCache()
        {
            lock (_curveCacheLock)
            {
                _unfactoredServiceNQCache = null;
                _unfactoredDamageNQCache = null;
                _unfactoredUltimateNQCache = null;
                _factoredServiceNQCache = null;
                _factoredDamageNQCache = null;
                _factoredUltimateNQCache = null;
            }
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

        private List<double> _damageLimitAxialForceThresholds = [];
        public List<double> DamageLimitAxialForceThresholds
        {
            get => _damageLimitAxialForceThresholds;
            private set => SetProperty(ref _damageLimitAxialForceThresholds, value);
        }

        // 使用限界軸力制限値（kN単位、PrecastPileSectionから転送）
        private double _serviceLimitNMin;
        public double ServiceLimitNMin
        {
            get => _serviceLimitNMin;
            private set => SetProperty(ref _serviceLimitNMin, value);
        }

        private double _serviceLimitNMax;
        public double ServiceLimitNMax
        {
            get => _serviceLimitNMax;
            private set => SetProperty(ref _serviceLimitNMax, value);
        }

        // せん断の軸力制限値（PHC杭用、N単位 → GetNMRawで転送）
        private double _shearNMinService, _shearNMaxService;
        private double _shearNMinDamage, _shearNMaxDamage;
        private double _shearNMinUltimate, _shearNMaxUltimate;
        public double ShearNMinService { get => _shearNMinService; private set => SetProperty(ref _shearNMinService, value); }
        public double ShearNMaxService { get => _shearNMaxService; private set => SetProperty(ref _shearNMaxService, value); }
        public double ShearNMinDamage { get => _shearNMinDamage; private set => SetProperty(ref _shearNMinDamage, value); }
        public double ShearNMaxDamage { get => _shearNMaxDamage; private set => SetProperty(ref _shearNMaxDamage, value); }
        public double ShearNMinUltimate { get => _shearNMinUltimate; private set => SetProperty(ref _shearNMinUltimate, value); }
        public double ShearNMaxUltimate { get => _shearNMaxUltimate; private set => SetProperty(ref _shearNMaxUltimate, value); }

        // [JsonIgnore] 必須: これらは GetNMRaw() で重い断面計算をトリガする computed プロパティ。
        // 保存時に System.Text.Json から getter が呼ばれると 1 杭セクションあたり数秒、
        // 全杭で 10 秒級のオーバーヘッドになる。読取専用なので load 時にも復元できない (廃棄値)。
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredServiceNMRaw => GetNMRaw(nameof(UnfactoredServiceNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredDamageNMRaw => GetNMRaw(nameof(UnfactoredDamageNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredUltimateNMRaw => GetNMRaw(nameof(UnfactoredUltimateNM));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredServiceNMRaw => GetNMRaw(nameof(FactoredServiceNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredDamageNMRaw => GetNMRaw(nameof(FactoredDamageNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredDamageNMLevel1Raw => GetNMRaw(nameof(FactoredDamageNMLevel1));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredUltimateNMRaw => GetNMRaw(nameof(FactoredUltimateNM));

        // --- 変更: NMプロパティをキャッシュ ---
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredServiceNM =>
            GetOrComputeCurve(ref _unfactoredServiceNMCache, () => (
                GetMultipliedListValues(UnfactoredServiceNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredServiceNMRaw.M, 1e-6)
            ));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredDamageNM =>
            GetOrComputeCurve(ref _unfactoredDamageNMCache, () => (
                GetMultipliedListValues(UnfactoredDamageNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredDamageNMRaw.M, 1e-6)
            ));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredUltimateNM =>
            GetOrComputeCurve(ref _unfactoredUltimateNMCache, () => (
                GetMultipliedListValues(UnfactoredUltimateNMRaw.N, 1e-3),
                GetMultipliedListValues(UnfactoredUltimateNMRaw.M, 1e-6)
            ));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredServiceNM =>
            GetOrComputeCurve(ref _factoredServiceNMCache, () => (
                GetMultipliedListValues(FactoredServiceNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredServiceNMRaw.M, 1e-6)
            ));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredDamageNM =>
            GetOrComputeCurve(ref _factoredDamageNMCache, () => (
                GetMultipliedListValues(FactoredDamageNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredDamageNMRaw.M, 1e-6)
            ));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredDamageNMLevel1 =>
            GetOrComputeCurve(ref _factoredDamageNMLevel1Cache, () => (
                GetMultipliedListValues(FactoredDamageNMLevel1Raw.N, 1e-3),
                GetMultipliedListValues(FactoredDamageNMLevel1Raw.M, 1e-6)
            ));

        /// <summary>
        /// レベル別の損傷限界 NM 曲線を返す。
        /// level == 1: β2 なし（β1 のみ）
        /// level == 2（デフォルト）: β1×β2
        /// </summary>
        public (List<double> N, List<double> M) GetFactoredDamageNM(int level)
            => level == 1 ? FactoredDamageNMLevel1 : FactoredDamageNM;

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) FactoredUltimateNM =>
            GetOrComputeCurve(ref _factoredUltimateNMCache, () => (
                GetMultipliedListValues(FactoredUltimateNMRaw.N, 1e-3),
                GetMultipliedListValues(FactoredUltimateNMRaw.M, 1e-6)
            ));

        // 降伏開始NM（Raw: N[N], M[Nmm]）をキャッシュ付きで返す
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) SteelYieldNMRaw
            => _steelYieldNMRawCache ??= ComputeSteelYieldNMRaw();

        // スケーリング後（kN, kNm）
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) SteelYieldNM => (
            GetMultipliedListValues(SteelYieldNMRaw.N, 1e-3),
            GetMultipliedListValues(SteelYieldNMRaw.M, 1e-6)
        );

        // 降伏開始NM（Raw: N[N], M[Nmm]）をキャッシュ付きで返す
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) CrackNMRaw
            => _crackNMRawCache ??= ComputeCrackNMRaw();

        // スケーリング後（kN, kNm）
        [System.Text.Json.Serialization.JsonIgnore]
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

        // Max/Min 系も NM 曲線にアクセスするため [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredServiceNMax => GetFilteredNMax(UnfactoredServiceNM);
        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredDamageNMax => GetFilteredNMax(UnfactoredDamageNM);
        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredUltimateNMax => GetFilteredNMax(UnfactoredUltimateNM);

        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredServiceNMin => GetFilteredNMin(UnfactoredServiceNM);
        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredDamageNMin => GetFilteredNMin(UnfactoredDamageNM);
        [System.Text.Json.Serialization.JsonIgnore] public double UnfactoredUltimateNMin => GetFilteredNMin(UnfactoredUltimateNM);

        [System.Text.Json.Serialization.JsonIgnore] public double FactoredServiceNMax => GetFilteredNMax(FactoredServiceNM);
        [System.Text.Json.Serialization.JsonIgnore] public double FactoredDamageNMax => GetFilteredNMax(FactoredDamageNM);
        [System.Text.Json.Serialization.JsonIgnore] public double FactoredUltimateNMax => GetFilteredNMax(FactoredUltimateNM);

        [System.Text.Json.Serialization.JsonIgnore] public double FactoredServiceNMin => GetFilteredNMin(FactoredServiceNM);
        [System.Text.Json.Serialization.JsonIgnore] public double FactoredDamageNMin => GetFilteredNMin(FactoredDamageNM);
        [System.Text.Json.Serialization.JsonIgnore] public double FactoredUltimateNMin => GetFilteredNMin(FactoredUltimateNM);

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
                    $"RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.RcSection) =>
                    $"SPRC-RC|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

                // 場所打ち鋼管RC杭 - 鋼管RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) =>
                    $"SPRC-SP|{PipeGrade}|{PipeDia}|{PipeTs}|{CorrosionDepth}|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}",

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
                _ => $"OTHER|{PileBodyType}|{PileSectionType}|{PipeGrade}|{PipeDia}|{PipeTs}|{CorrosionDepth}|{ConcreteOutDia}|{ConcreteGsi}|{ConcreteFc}|{MainBarDr}|{MainBarNum}|{MainBarSpec}|{MainBarSize}|N={axialNRounded}"
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
                    // ファイバーモデル M-φ オプション。
                    // 対応断面: 場所打ちRC / 場所打ち鋼管コンクリート（RC部・鋼管コンクリート部）/
                    //           PHC / PRC / SC / コンクリート充填鋼管部（すべて AbstractPileSection 系）。
                    // 鋼管杭の鋼管部は SteelPipeSection（別系統、section == null パス）のため対象外。
                    // 解けない場合（軸力が耐力範囲外等）は従来ポリリニアへフォールバック。
                    if (ConcreteModelOptions.UseFiberMPhi
                        && section is AbstractPileSection fiberSection
                        && fiberSection.GetMPhiRelationshipFiber(axialN_inN) is { } fiber)
                    {
                        // FEM ばねとして負勾配・零勾配とならないよう単調化＋最小勾配床
                        (phisRaw, msRaw) = MakeMonotonicForAnalysis(fiber.Phis, fiber.Moments);
                    }
                    else
                    {
                        (phisRaw, msRaw) = section.GetMPhiRelationship(axialN_inN);
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
            catch (Exception)
            {
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
        private static (List<double> Phis, List<double> Moments) MakeMonotonicForAnalysis(
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



        public string PileDescription
        {
            get
            {
                if (PileBodyType == PileTypeNames.InsituRc ||
                    (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSectionType == PileTypeNames.RcSection))
                {
                    return $"D={PileDiameter}({MainBarNum}-{MainBarSize})";
                }
                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSectionType == PileTypeNames.SteelPipeConcreteSection)
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
                else if (PileBodyType == PileTypeNames.PrecastConcrete)
                {
                    return SelectedPrecastPile.Name;
                }
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    return SelectedSteelPipePileName;
                }

                return string.Empty;
            }
        }

        private string _pileBodyType = PileTypeNames.InsituRc;
        public string PileBodyType
        {
            get => _pileBodyType;
            set
            {
                // 値のバリデーション
                var validTypes = new[]
                {
                    PileTypeNames.InsituRc,
                    PileTypeNames.InsituSteelPipeConcrete,
                    PileTypeNames.PrecastConcrete,
                    PileTypeNames.SteelPipe
                };
                bool isUnknown = !string.IsNullOrWhiteSpace(value) && !validTypes.Contains(value);
                var safeValue = string.IsNullOrWhiteSpace(value) || isUnknown
                    ? validTypes[0]
                    : value;

                // PileSectionType 側と同じ理由 (無言のデータ破損を避ける) でログに残す
                if (isUnknown)
                {
                    Serilog.Log.Warning(
                        "[PileSection] 未知の杭体タイプ '{Unknown}' を '{Fallback}' に差し替えました。" +
                        "杭種を追加した場合は PileSection.PileBodyType の validTypes への登録漏れを確認してください。",
                        value, safeValue);
                }

                if (SetProperty(ref _pileBodyType, safeValue))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>
        /// 杭体タイプに対する断面タイプの既定値。
        /// 未知の断面タイプを差し替えるときの行き先で、常に <c>鉄筋コンクリート部</c> に落とすと
        /// 既製コンクリート杭や鋼管杭が場所打ち RC 断面に化けてしまう。
        /// </summary>
        private string DefaultSectionTypeForBodyType => PileBodyType switch
        {
            PileTypeNames.PrecastConcrete => PileTypeNames.Phc,
            PileTypeNames.SteelPipe => PileTypeNames.SteelPipeSection,
            PileTypeNames.InsituSteelPipeConcrete => PileTypeNames.SteelPipeConcreteSection,
            _ => PileTypeNames.RcSection,
        };

        private string _pileSectionType = PileTypeNames.RcSection;
        public string PileSectionType
        {
            get => _pileSectionType;
            set
            {
                var validTypes = new[]
                {
                    PileTypeNames.RcSection,
                    PileTypeNames.SteelPipeConcreteSection,
                    PileTypeNames.Phc,
                    PileTypeNames.PhcNodular,
                    PileTypeNames.Prc,
                    PileTypeNames.PrcNodular,
                    PileTypeNames.PrcNodularPhcPart,
                    PileTypeNames.BfsHead,
                    PileTypeNames.BfsTip,
                    PileTypeNames.Sc,
                    PileTypeNames.SteelPipe,      // 旧互換 (鋼管杭 を細分化する前のサブタイプ名)
                    PileTypeNames.SteelPipeSection,
                    PileTypeNames.CftSection,
                };
                // null / 空の書き戻しは無視して現在値を保つ。
                // ComboBox の ItemsSource から現在値が外れると SelectedItem が null になり、
                // TwoWay バインドがその null を書き戻してくる。これを既定値に落とすと
                // ユーザーが何も操作していないのに断面タイプが差し替わってしまう。
                if (string.IsNullOrWhiteSpace(value)) return;

                bool isUnknown = !validTypes.Contains(value);
                var safeValue = isUnknown ? DefaultSectionTypeForBodyType : value;

                // 未知の断面タイプを黙って差し替えると、断面耐力が別物になったまま
                // 解析が通ってしまう (新しい断面タイプの登録漏れが「無言のデータ破損」になる)。
                // 互換のためフォールバック自体は残すが、必ず記録に残す。
                if (isUnknown)
                {
                    Serilog.Log.Warning(
                        "[PileSection] 未知の断面タイプ '{Unknown}' を '{Fallback}' に差し替えました。" +
                        "断面タイプを追加した場合は PileSection.PileSectionType の validTypes への登録漏れを確認してください。",
                        value, safeValue);
                }

                if (SetProperty(ref _pileSectionType, safeValue))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private string _selectedSteelPipe = string.Empty;
        public string SelectedSteelPipe
        {
            get => _selectedSteelPipe;
            set => SetProperty(ref _selectedSteelPipe, value);
        }

        public string[] InsituPileSectionTypesOption { get; } =
        [
            PileTypeNames.InsituRc,
            PileTypeNames.InsituSteelPipeConcrete,
        ];

        // 場所打ち鋼管コンクリート杭の部位 
        public string[] InsituSteelPileSectionTypeOption { get; } =
        [
            PileTypeNames.SteelPipeConcreteSection,
            PileTypeNames.RcSection,
        ];

        private bool _isTopSegment;
        /// <summary>
        /// この断面が杭体の最上段区間か。<see cref="PileBodyInput.PileBodySegmentsUpdate"/> が設定する。
        /// PHC節杭 は杭頭で継手を介して上杭に接合される下杭として使うため、
        /// 最上段区間では選択できないようにするのに使う。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsTopSegment
        {
            get => _isTopSegment;
            set
            {
                if (SetProperty(ref _isTopSegment, value))
                    OnPropertyChanged(nameof(PreCastConcretePileSectionTypeOption));
            }
        }

        /// <summary>
        /// 既製コンクリート杭の断面タイプ選択肢。区間の位置によらず全種類を出す。
        ///
        /// 以前は「節杭は上杭に継ぐ下杭として使う」という運用から最上段区間で節杭を除外していたが、
        /// 次の 2 点からやめた。
        /// <list type="bullet">
        /// <item>除外の判定に使う <see cref="IsTopSegment"/> は区間更新が走るまで false のままで、
        ///       同じ状態でも「初回は選べるが開き直すと消える」という不安定な挙動になっていた</item>
        /// <item>Smart-MAGNUM / Hybrid ニーディングは先端が節杭である前提の工法で、
        ///       1 区間だけでモデル化したい場合に節杭が選べなくなってしまう</item>
        /// </list>
        /// 「最上段に節杭が来ている」こと自体は施工上の注意なので、入力チェックの警告で知らせる
        /// （<c>CheckInputData.CheckPileBodyGeometry</c>）。
        /// </summary>
        public string[] PreCastConcretePileSectionTypeOption =>
        [
            PileTypeNames.Phc,
            PileTypeNames.PhcNodular,
            PileTypeNames.Prc,
            PileTypeNames.PrcNodular,
            PileTypeNames.PrcNodularPhcPart,
            PileTypeNames.BfsHead,
            PileTypeNames.BfsTip,
            PileTypeNames.Sc
        ];

        // 鋼管杭の部位
        // 場所打ち鋼管コンクリート杭の PileTypeNames.SteelPipeConcreteSection/PileTypeNames.RcSection と同じ思想で、
        // 鋼管杭でも杭頭部 (鉄筋定着工法用、コンクリート充填+鉄筋配置) と杭中下部 (鋼管のみ) を
        // 別サブタイプとして杭区間で選べるようにする。
        // - PileTypeNames.SteelPipeSection          : 純粋な鋼管 (M-φ 計算: SteelPipeSection)
        // - PileTypeNames.CftSection: 杭頭部、鋼管内コンクリート充填 + 鉄筋配置可
        //                       (但し M-φ の耐力には鉄筋を参入しない、長さ ≒ 鋼管外径)
        public string[] SteelPipePileSectionTypeOption { get; } =
        [
            PileTypeNames.CftSection,
            PileTypeNames.SteelPipeSection,
        ];

        // 杭径
        private double _pileDiameter = 1200.0;
        public double PileDiameter
        {
            get => _pileDiameter;
            set
            {
                if (!double.IsFinite(value)) return;
                if (SetProperty(ref _pileDiameter, value))
                {
                    RecalculatePileDia();
                }
            }
        }

        /// <summary>
        /// 表示用の杭径（腐食代を見込まない公称外径）[mm]。
        /// 解析で使う <see cref="PileDiameter"/> は鋼管系では腐食代を控除した有効径
        /// (PipeDia − 2×CorrosionDepth) だが、計算書の「杭径」表示では腐食前の
        /// 公称外径（鋼管外径 = PipeDia）を用いる。
        /// 鋼管を外面に持たない断面（場所打ち RC / 鉄筋コンクリート部 / PHC・PRC・SC）は
        /// PileDiameter が既に公称外径なのでそのまま返す。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NominalPileDiameter => (PileBodyType, PileSectionType) switch
        {
            (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) => PipeDia,
            (PileTypeNames.SteelPipe, _) => PipeDia,
            _ => PileDiameter
        };

        // ───── PHC節杭 固有の諸元 ─────
        // 断面耐力は軸部基準でストレート PHC 杭と同一なので、これらは断面計算には一切使わない。
        // 断面タイプが PHC節杭 でないときは 0。

        private double _nodeDiameter;
        /// <summary>
        /// 節部径 Do [mm]（PHC節杭のみ。それ以外は 0）。
        /// 現状は表示・計算書用で、支持力の周面抵抗は軸部径 <see cref="PileDiameter"/> で算定している
        /// （節の効果は未考慮＝安全側）。工法別の支持力式を実装する際にここを使う。
        /// </summary>
        public double NodeDiameter
        {
            get => _nodeDiameter;
            set => SetProperty(ref _nodeDiameter, value);
        }

        private double _catalogMassPerM;
        /// <summary>
        /// カタログ標準質量 [t/m]（PHC節杭のみ。それ以外は 0）。
        /// 節杭の自重 <see cref="W"/> はこの値から求める。節部体積を幾何的に積分しても
        /// カタログ質量は再現できない（最良フィットでも RMS 3.1%・最大 6.1% 相違）ため、
        /// メーカー公称値を唯一の出所とする。
        /// </summary>
        public double CatalogMassPerM
        {
            get => _catalogMassPerM;
            set => SetProperty(ref _catalogMassPerM, value);
        }

        private double _nodePitch;
        /// <summary>節ピッチ（節中心間距離）[mm]（PHC節杭のみ。それ以外は 0）。カタログ姿図の寸法記入値。</summary>
        public double NodePitch
        {
            get => _nodePitch;
            set => SetProperty(ref _nodePitch, value);
        }

        private double _nodeHeadOffset;
        /// <summary>杭頭から第 1 節中心までの距離 [mm]（PHC節杭のみ）。カタログ姿図の寸法記入値。</summary>
        public double NodeHeadOffset
        {
            get => _nodeHeadOffset;
            set => SetProperty(ref _nodeHeadOffset, value);
        }

        private double _nodeToeOffset;
        /// <summary>杭先端から最終節中心までの距離 [mm]（PHC節杭のみ）。カタログ姿図の寸法記入値。</summary>
        public double NodeToeOffset
        {
            get => _nodeToeOffset;
            set => SetProperty(ref _nodeToeOffset, value);
        }

        /// <summary>
        /// この断面が節杭か (PHC節杭 / PRC節杭 / PRC節杭(PHC部))。
        /// 節杭固有の諸元・姿図・杭頭タイプ判定はすべてこの判定で分岐する。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsNodularPile => PileTypeNames.IsNodularSection(PileSectionType);

        private double _nodeHeadDiameter;
        /// <summary>
        /// 拡頭部径 Dt [mm]（PHC節杭で拡頭タイプのときのみ。標準タイプ・節杭以外は 0）。
        /// 直上の杭区間の径から自動で決まる（<see cref="ResolveNodularHead"/>）。
        /// </summary>
        public double NodeHeadDiameter
        {
            get => _nodeHeadDiameter;
            set => SetProperty(ref _nodeHeadDiameter, value);
        }

        private double _nodeHeadLength;
        /// <summary>拡頭部長さ Lt [mm]（カタログ全行で 600mm）。標準タイプ・節杭以外は 0。</summary>
        public double NodeHeadLength
        {
            get => _nodeHeadLength;
            set => SetProperty(ref _nodeHeadLength, value);
        }

        private string _nodularHeadType = NodularHeadTypes.Standard;
        /// <summary>PHC節杭 の杭頭タイプ（標準タイプ / 拡頭中間径タイプ / 拡頭タイプ）。</summary>
        public string NodularHeadType
        {
            get => _nodularHeadType;
            set => SetProperty(ref _nodularHeadType, value);
        }

        private string _nodularHeadNote = string.Empty;
        /// <summary>杭頭タイプの自動判定の根拠・注意（諸元表に出す）。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string NodularHeadNote
        {
            get => _nodularHeadNote;
            set => SetProperty(ref _nodularHeadNote, value);
        }

        /// <summary>PHC節杭 の杭頭タイプ名。</summary>
        public static class NodularHeadTypes
        {
            public const string Standard = "標準タイプ";
            public const string IntermediateHead = "拡頭中間径タイプ";
            public const string EnlargedHead = "拡頭タイプ";
        }

        /// <summary>
        /// 直上の杭区間の径に合わせて PHC節杭 の杭頭タイプ（標準／拡頭中間径／拡頭）を決める。
        ///
        /// 継手で接合する以上、杭頭径は直上の区間の径と一致していなければならないので、
        /// カタログの拡頭形状一覧から Dt が一致する製品を選ぶ。一致するものが無ければ
        /// 標準タイプのままとし、その旨を <see cref="NodularHeadNote"/> に残す。
        /// </summary>
        /// <param name="diameterAbove">直上の杭区間の杭径 [mm]。最上段の区間なら null。</param>
        public void ResolveNodularHead(double? diameterAbove)
        {
            if (!IsNodularPile)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = string.Empty;
                return;
            }

            // BF.S は頭部軸部そのものが太いので「拡頭」という設定を持たない。
            if (PileSectionType is PileTypeNames.BfsHead or PileTypeNames.BfsTip)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = "頭部軸部と先端軸部で外径が変わる製品のため、拡頭の設定はありません";
                return;
            }

            const double tol = 0.5; // mm

            // 最上段、または直上が軸部径と同じなら拡頭不要
            if (diameterAbove is not double above || above <= PileDiameter + tol)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = diameterAbove == null
                    ? "最上段の区間のため標準タイプ"
                    : "直上区間が軸部径と同径のため標準タイプ";
                return;
            }

            var heads = CurrentNodularHeads();
            int idx = heads.FindIndex(h => Math.Abs(h.Dt - above) <= tol);

            if (idx < 0)
            {
                var available = heads.Select(h => $"{h.Dt:N0}").ToList();
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = available.Count > 0
                    ? $"直上区間の径 {above:N0}mm に一致する拡頭径がありません (選択可: {string.Join(" / ", available)}mm)。標準タイプとして扱います"
                    : $"この呼び名には拡頭タイプの設定がありません。標準タイプとして扱います";
                return;
            }

            var match = heads[idx];
            NodeHeadDiameter = match.Dt;
            NodeHeadLength = match.Lt;
            NodularHeadType = match.IsIntermediate
                ? NodularHeadTypes.IntermediateHead
                : NodularHeadTypes.EnlargedHead;
            NodularHeadNote = $"直上区間の径 {above:N0}mm に合わせて自動選択";
        }

        /// <summary>
        /// 選択中の節杭製品に対応する拡頭形状の一覧。
        /// NPH と NPRC で DTO が別なので、ここで共通の形に畳んでから判定に使う。
        /// </summary>
        private List<(double Dt, double Lt, bool IsIntermediate)> CurrentNodularHeads()
        {
            string name = NodularProductName();
            if (string.IsNullOrEmpty(name)) return [];

            return PileSectionType == PileTypeNames.PhcNodular
                ? [.. NodularPileHeads.Where(h => h.Name == name)
                                      .Select(h => (h.Dt, h.Lt, h.IsIntermediateHead))]
                : [.. NodularPrcPileHeads.Where(h => h.Name == name)
                                         .Select(h => (h.Dt, h.Lt, h.IsIntermediateHead))];
        }

        /// <summary>選択中の節杭製品の呼び名 (例: 440-300)。節杭でなければ空。</summary>
        private string NodularProductName()
        {
            if (!IsNodularPile) return string.Empty;
            string? name = SelectedPrecastPile?.Name;
            return PileSectionType == PileTypeNames.PhcNodular
                ? NodularPiles.FirstOrDefault(p => p.DisplayName == name)?.Name ?? string.Empty
                : FindNodularPrcProduct(name)?.Name ?? string.Empty;
        }

        /// <summary>
        /// 節の半径方向の高さ [mm]（＝ (Do − D)/2）。PHC節杭 以外は 0。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeRadialRise => IsNodularPile && NodeDiameter > PileDiameter
            ? (NodeDiameter - PileDiameter) * 0.5
            : 0.0;

        /// <summary>
        /// 節テーパーの軸方向長さ [mm]。カタログ姿図でテーパーは厳密に 45°（軸方向長 = 半径方向高さ）
        /// なので、Do と D から一意に決まる。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeTaperLength => NodeRadialRise;

        private double _catalogNodeFlatLength;
        /// <summary>
        /// 製品カタログに寸法記入がある場合の節部（平坦部）長さ [mm]。0 なら記入が無い。
        /// BF.S は寸法記入があり、節杭 (JP-NPH / JP-NPRC) は無い。
        /// </summary>
        public double CatalogNodeFlatLength
        {
            get => _catalogNodeFlatLength;
            set => SetProperty(ref _catalogNodeFlatLength, value);
        }

        /// <summary>
        /// 節部（最大径が一定の区間）の軸方向長さ [mm]。
        /// カタログに寸法記入がある製品 (BF.S) はその値、
        /// <b>記入が無い製品 (JP-NPH / JP-NPRC) は姿図の実測値（テーパー軸長と等長）に基づく推定値。</b>
        /// 図示専用で、断面耐力・自重・支持力には一切使わない。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeFlatLength =>
            CatalogNodeFlatLength > 0 ? CatalogNodeFlatLength : NodeRadialRise;

        /// <summary>節 1 個の軸方向全長 [mm]（テーパー + 節部 + テーパー）。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeTotalLength => 2.0 * NodeTaperLength + NodeFlatLength;

        /// <summary>
        /// 節中心からの相対位置で、節の外形を「(軸方向オフセット [mm], 半径 [mm])」の列として返す
        /// （上から下へ）。姿図・3D 表示が同じ形状を描くための唯一の定義。
        /// PHC節杭 以外や寸法が揃わない場合は空。
        /// </summary>
        public IReadOnlyList<(double OffsetFromCenter, double Radius)> NodeProfile()
        {
            if (!IsNodularPile || NodeRadialRise <= 0) return [];

            double rShaft = PileDiameter * 0.5;
            double rNode = NodeDiameter * 0.5;
            double halfFlat = NodeFlatLength * 0.5;
            double halfTotal = halfFlat + NodeTaperLength;

            // 上のテーパー始点 → 節部上端 → 節部下端 → 下のテーパー終点
            return
            [
                (+halfTotal, rShaft),
                (+halfFlat, rNode),
                (-halfFlat, rNode),
                (-halfTotal, rShaft),
            ];
        }

        /// <summary>
        /// PHC節杭 の区間外形を「(区間上端からの深さ [m], 半径 [mm])」の折れ線として上から順に返す。
        /// 姿図・3D 表示が同じ形状を描くための唯一の定義。
        ///
        /// 拡頭タイプでは上端から <see cref="NodeHeadLength"/> までが拡頭部径 Dt になる。
        /// 拡頭部と第 1 節は重なる（Lt = 杭頭〜第 1 節中心 = 600mm）ので、重なる範囲は
        /// 大きい方の径を採る。
        /// </summary>
        public IReadOnlyList<(double Depth, double Radius)> NodularOutline(double segmentLengthM)
        {
            if (!IsNodularPile || segmentLengthM <= 0) return [];

            double rShaft = PileDiameter * 0.5;
            var pts = new List<(double Depth, double Radius)>();

            // 拡頭部 (上端から Lt)
            bool hasHead = NodeHeadDiameter > PileDiameter && NodeHeadLength > 0;
            double headBottom = hasHead ? NodeHeadLength / 1000.0 : 0.0;
            if (hasHead)
            {
                double rHead = NodeHeadDiameter * 0.5;
                pts.Add((0.0, rHead));
                pts.Add((headBottom, rHead));
            }
            else
            {
                pts.Add((0.0, rShaft));
            }

            // 節
            foreach (double centerDepth in NodeCenterDepthsFromSegmentTop(segmentLengthM))
            {
                foreach (var (offset, radius) in NodeProfile())
                {
                    // offset は節中心から上が正 → 深さは中心深さ − offset
                    double depth = centerDepth - offset / 1000.0;
                    if (depth < 0 || depth > segmentLengthM) continue;
                    // 拡頭部の範囲内は拡頭部径と比べて大きい方を採る
                    double r = hasHead && depth <= headBottom
                        ? Math.Max(radius, NodeHeadDiameter * 0.5)
                        : radius;
                    pts.Add((depth, r));
                }
            }

            pts.Add((segmentLengthM, rShaft));

            // 深さ順に整列し、拡頭部内で潰れた重複点を除く
            var ordered = pts.OrderBy(p => p.Depth).ToList();
            var result = new List<(double Depth, double Radius)>(ordered.Count);
            foreach (var p in ordered)
            {
                if (result.Count > 0
                    && Math.Abs(result[^1].Depth - p.Depth) < 1e-9
                    && Math.Abs(result[^1].Radius - p.Radius) < 1e-9)
                    continue;
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// 杭区間の上端から測った節中心位置 [m] を上から順に返す。
        /// PHC節杭 以外、または寸法が未設定なら空。
        ///
        /// 節杭は 1 本の製品が 1 区間に対応する前提で、区間上端から <see cref="NodeHeadOffset"/>、
        /// 以降 <see cref="NodePitch"/> ごとに節が並び、最終節は区間下端から
        /// <see cref="NodeToeOffset"/> 上方に来る（カタログの杭長 4〜15m・1m ピッチと整合する）。
        ///
        /// <b>節の形状（節部長さ・テーパー長）はカタログに寸法記入が無く再現できないため、
        /// ここで返すのは位置だけである。</b>
        /// </summary>
        public IEnumerable<double> NodeCenterDepthsFromSegmentTop(double segmentLengthM)
        {
            if (!IsNodularPile || NodePitch <= 0 || segmentLengthM <= 0) yield break;

            const double tol = 1.0e-9;
            double pitch = NodePitch / 1000.0;
            double last = segmentLengthM - NodeToeOffset / 1000.0;

            for (double z = NodeHeadOffset / 1000.0; z <= last + tol; z += pitch)
            {
                if (z < -tol || z > segmentLengthM + tol) continue;
                yield return z;
            }
        }

        /// <summary>
        /// PC鋼材とプレストレスを持つ既製杭断面 (PHC杭 / PHC節杭 / PRC杭) か。
        /// 断面タイプの直積判定が各所に散らばると追加漏れが起きるため、意味のある単位で名前を付ける。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsPrestressedPrecastSection =>
            PileSectionType is PileTypeNames.Phc or PileTypeNames.PhcNodular or PileTypeNames.Prc;

        /// <summary>中空円形の既製コンクリート杭断面 (PHC杭 / PHC節杭 / PRC杭 / SC杭) か。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsHollowPrecastSection =>
            PileSectionType is PileTypeNames.Phc or PileTypeNames.PhcNodular
                            or PileTypeNames.Prc or PileTypeNames.PrcNodular
                            or PileTypeNames.PrcNodularPhcPart or PileTypeNames.Sc
                            or PileTypeNames.BfsHead or PileTypeNames.BfsTip;

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
                if (PileBodyType == PileTypeNames.InsituRc)
                {
                    PileDiameter = ConcreteOutDia;
                    ConcreteThickness = ConcreteOutDia * 0.5;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    PileSectionType = PileTypeNames.RcSection;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                }

                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
                {
                    if (PileSectionType == PileTypeNames.RcSection)
                    {
                        PileDiameter = ConcreteOutDia;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                    else if (PileSectionType == PileTypeNames.SteelPipeConcreteSection)
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                        ConcreteOutDia = PipeDia - PipeTs * 2.0;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                }
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    // 場所打ち鋼管コンクリート杭の鋼管コンクリート部と同じ計算で、
                    // 腐食代を考慮した有効径を PileDiameter とする。
                    // コンクリート充填鋼管部では充填コンクリート関連 (Dc, Tc, MainBarDr) も派生。
                    if (PileSectionType == PileTypeNames.CftSection)
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                        ConcreteOutDia = PipeDia - PipeTs * 2.0;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                    else // PileTypeNames.SteelPipeSection or 旧 PileTypeNames.SteelPipe
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageService.ShowError($"杭径再計算中にエラーが発生しました。", ex, "杭径再計算エラー"));
            }
        }

        //杭断面変更時（デフォルト）のメソッド
        public void ResetSectionProperties()
        {
            try
            {


                // 表示の即時更新用にクリア（後で GetNMRaw が再計算して再設定）
                UltimateLimitAxialForceThresholds = [];

                if (PileBodyType == PileTypeNames.InsituRc)
                {
                    PileSectionType = PileTypeNames.RcSection;
                    ConcreteOutDia = 1200.0;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();
                }
                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
                {
                    PileSectionType = PileTypeNames.SteelPipeConcreteSection;
                    ConcreteOutDia = 0.0;
                    PipeDia = 1200.0;
                    PipeTs = 16.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();

                }
                else if (PileBodyType == PileTypeNames.PrecastConcrete)
                {
                    PileSectionType = PileTypeNames.Phc;
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
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    // 杭頭側 (区間追加時の最初の区間) は PileTypeNames.CftSection を既定とする
                    // (鉄筋定着工法では杭頭が常にこの部位)。下方区間は手動で PileTypeNames.SteelPipeSection に切替える。
                    PileSectionType = PileTypeNames.CftSection;
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
                    MessageService.ShowError($"断面プロパティのリセット中にエラーが発生しました。", ex, "断面リセットエラー"));
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
            // Ec 算定用 ξ: オプション時は 1.0（強度側 Gsi·Fc 等は実 Gsi のまま）
            double gsiForEc = ConcreteModelOptions.UseUnitGsiForConcreteE ? 1.0 : ConcreteGsi;
            ConcreteE = 3.35 * Math.Pow(10, 4) * Math.Pow(ConcreteGamma / 24.0, 2.0) * Math.Pow(gsiForEc * ConcreteFc / 60.0, 1.0 / 3.0);
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

        /// <summary>
        /// 断面タイプに対応する既製杭ライブラリを返す。既製コンクリート杭以外は null。
        /// 節杭は専用 DTO を PrecastPile へ詰め替えたキャッシュを返すため、
        /// 呼び出し側は PHC/PRC/SC と同じ扱いができる。
        /// </summary>
        private static List<PrecastPile>? GetPrecastLibrary(string pileSectionType) => pileSectionType switch
        {
            PileTypeNames.Phc => PHCs,
            PileTypeNames.Prc => PRCs,
            PileTypeNames.Sc => SCs,
            PileTypeNames.PhcNodular => _cachedNodularAsPrecast.Value,
            PileTypeNames.PrcNodular => _cachedNodularPrcAsPrecast.Value,
            PileTypeNames.PrcNodularPhcPart => _cachedNodularPrcPhcPartAsPrecast.Value,
            PileTypeNames.BfsHead => _cachedBfsHeadAsPrecast.Value,
            PileTypeNames.BfsTip => _cachedBfsTipAsPrecast.Value,
            _ => null
        };

        /// <summary>
        /// SelectedPrecastPile.Name がライブラリ (PHCs/PRCs/SCs/節杭) に存在するかチェックする。副作用なし。
        /// 名前が未指定、または PileSectionType が既製コンクリート杭以外のときは true を返す (検査対象外)。
        /// </summary>
        public bool IsSelectedPrecastPileInLibrary()
        {
            string? name = SelectedPrecastPile?.Name;
            if (string.IsNullOrWhiteSpace(name)) return true;

            List<PrecastPile>? candidates = GetPrecastLibrary(PileSectionType);
            if (candidates == null) return true; // 既製コンクリート杭以外は対象外

            foreach (var p in candidates)
            {
                if (p.Name == name) return true;
            }
            return false;
        }

        /// <summary>
        /// 節杭固有の諸元 (節部径・カタログ標準質量・姿図寸法) を製品ライブラリから転記する。
        /// これらは <see cref="PrecastPile"/> に載らないので断面転記とは別経路になる。
        /// 断面タイプが節杭でない間は 0 のままにして、参照されても影響が出ないようにする。
        /// </summary>
        private void ApplyNodularProductGeometry()
        {
            string? name = SelectedPrecastPile?.Name;
            double do_ = 0.0, mass = 0.0, pitch = 0.0, head = 0.0, toe = 0.0, flat = 0.0;

            if (PileSectionType == PileTypeNames.PhcNodular)
            {
                var p = NodularPiles.FirstOrDefault(x => x.DisplayName == name);
                if (p != null)
                    (do_, mass, pitch, head, toe) = (p.Do, p.MassPerM, p.NodePitch, p.HeadOffset, p.ToeOffset);
            }
            else if (PileSectionType is PileTypeNames.BfsHead or PileTypeNames.BfsTip)
            {
                // 頭部軸部 / 先端軸部 は同じ製品の別部位。節部径・節寸法は共通で、
                // 軸部径だけが違う (= 節の出寸法が部位によって変わる)。
                // このカタログには標準質量表が無いので自重は軸部体積から求まる (節の分は未計上)。
                var p = FindBfsProduct(name);
                if (p != null)
                    (do_, pitch, head, toe, flat) =
                        (p.NodeDia, p.NodePitch, p.HeadOffset, p.ToeOffset, p.NodeFlatLength);
            }
            else if (PileTypeNames.IsNodularSection(PileSectionType))
            {
                // PRC部 / PHC部 は同じ製品の別断面なので、形状はどちらも製品行そのもの
                var p = FindNodularPrcProduct(name);
                if (p != null)
                    (do_, mass, pitch, head, toe) = (p.Do, p.MassPerM, p.NodePitch, p.HeadOffset, p.ToeOffset);
            }

            NodeDiameter = do_;
            CatalogMassPerM = mass;
            NodePitch = pitch;
            NodeHeadOffset = head;
            NodeToeOffset = toe;
            CatalogNodeFlatLength = flat;
        }

        /// <summary>表示名 (頭部軸部 / 先端軸部 のどちらでも) から BF.S の製品行を引く。</summary>
        private static BfsPile? FindBfsProduct(string? displayName) =>
            string.IsNullOrEmpty(displayName)
                ? null
                : BfsPiles.FirstOrDefault(
                    p => p.DisplayName == displayName || p.TipDisplayName == displayName);

        /// <summary>表示名 (PRC部 / PHC部 のどちらでも) から PRC節杭 の製品行を引く。</summary>
        private static NodularPrcPile? FindNodularPrcProduct(string? displayName) =>
            string.IsNullOrEmpty(displayName)
                ? null
                : NodularPrcPiles.FirstOrDefault(
                    p => p.DisplayName == displayName || p.PhcPartDisplayName == displayName);

        public void RecalculateSelectedPrecastPile()
        {
            List<PrecastPile> precastPiles = GetPrecastLibrary(PileSectionType) ?? [];

            ApplyNodularProductGeometry();

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

                    ApplyGuideYoungsModulusIfEnabled();

                    isFound = true;
                    break;
                }
            }

            if (!isFound && precastPiles.Count > 0)
            {
                // 断面タイプ切替（PHC⇔PRC⇔SC）などで旧選択名が新ライブラリに存在しない場合:
                // 旧選択と同径（同径が無ければ最も近い径）の杭を既定選択し、断面欄が空欄のまま
                // 旧タイプの諸元で計算が続くのを防ぐ。同径内はライブラリ順（No 昇順）の先頭を採る。
                double prevDia = SelectedPrecastPile?.PileDiameter ?? 0.0;
                PrecastPile fallback = precastPiles
                    .OrderBy(p => Math.Abs(p.PileDiameter - prevDia))
                    .ThenBy(p => p.No)
                    .First();

                Serilog.Log.Debug(
                    "[RecalculateSelectedPrecastPile] '{Old}' は {Type} ライブラリに存在しないため同径の '{New}' を既定選択",
                    SelectedPrecastPile?.Name, PileSectionType, fallback.Name);

                SelectedPrecastPile = new PrecastPile { Name = fallback.Name };
                RecalculateSelectedPrecastPile(); // 名前一致で全諸元を反映（必ず isFound になるため再帰は 1 回）
            }
            else if (!isFound)
            {
                Serilog.Log.Debug($"Error: SelectedPrecastPile.Name '{SelectedPrecastPile.Name}' not found in precastPiles.");
            }
        }

        // 選択したS杭
        private string _selectedSteelPipePileName = string.Empty;
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

        // PHC節杭（静的キャッシュを参照）
        public static ObservableCollection<string> NodularPileOption => _cachedNodularPileOption.Value;
        public static List<NodularPile> NodularPiles => _cachedNodularPiles.Value;
        /// <summary>拡頭中間径タイプ / 拡頭タイプ の形状一覧（呼び名 × 拡頭径 Dt）。</summary>
        public static List<NodularPileHead> NodularPileHeads => _cachedNodularPileHeads.Value;

        /// <summary>PRC節杭 (PRC部) の製品選択肢。</summary>
        public static ObservableCollection<string> NodularPrcOption => _cachedNodularPrcOption.Value;
        /// <summary>PRC節杭 (PHC部) の製品選択肢。</summary>
        public static ObservableCollection<string> NodularPrcPhcPartOption => _cachedNodularPrcPhcPartOption.Value;
        public static List<NodularPrcPile> NodularPrcPiles => _cachedNodularPrcPiles.Value;
        /// <summary>PRC節杭 の拡頭中間径タイプ / 拡頭タイプ の形状一覧。</summary>
        public static List<NodularPrcPileHead> NodularPrcPileHeads => _cachedNodularPrcPileHeads.Value;

        /// <summary>BF.S (頭部厚型節付き杭) 頭部軸部 の製品選択肢。</summary>
        public static ObservableCollection<string> BfsHeadOption => _cachedBfsHeadOption.Value;
        /// <summary>BF.S (頭部厚型節付き杭) 先端軸部 の製品選択肢。</summary>
        public static ObservableCollection<string> BfsTipOption => _cachedBfsTipOption.Value;
        public static List<BfsPile> BfsPiles => _cachedBfsPiles.Value;


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
        //
        // PHC節杭 はカタログ標準質量をそのまま用いる。節部の体積を推定形状から積分しても
        // カタログ質量は再現できない（既製杭の ConcreteGamma = 26 kN/m³ で RMS 3.4%・最大 7.0% 相違。
        // 密度と節長さを自由に振った最良フィットでも RMS 1.5% 止まりで、しかもその γ は 27.4 kN/m³ と
        // コンクリートとして非現実的。継手金物等を含むためと推定）。メーカー公称値が唯一正確な出所。
        // この結果、基本設定のコンクリート単位体積重量 ConcreteGamma は節杭の自重に効かない。
        //
        // ※ コンクリートの断面積は Ac をそのまま使う。Ac は定義の時点で
        //   主筋・テンドンを控除済みなので、ここで再度引くと鋼材ぶんを二重に控除して
        //   自重が過小になる (PHC で 0.5〜1.1%、PRC で 1.4〜5.5%)。
        //   押込み側は軸力の過小評価 = 危険側、引抜き側は抵抗の過小評価 = 安全側だった。
        public double W => IsNodularPile && CatalogMassPerM > 0
            ? CatalogMassPerM * UnitConversion.TON_TO_KN   // t/m -> kN/m
            : ((MainBarAg + TendonAp + PipeAs) * 78.5 + Ac * ConcreteGamma) * Math.Pow(10, -6);

        // 軸剛性 (kN)
        // 合成断面: コンクリート + 主筋 + PC鋼材 + 鋼管（Es·As）。鋼管を持たない断面では PipeAs=0 のため影響なし。
        public double EA => (ConcreteE * Ac + MainBarEr * MainBarAg + TendonEp * TendonAp + PipeEs * PipeAs) * 0.001;

        /// <summary>
        /// 「基礎部材の強度と変形性能」のヤング係数を使う設定のとき、
        /// 製品カタログから読み込んだ鋼材のヤング係数を指針の値に差し替える。
        ///
        /// カタログ値はメーカーで食い違う（異形棒鋼 200,000 / 205,000、鋼管 200,000 / 205,000）。
        /// 差し替えは<b>製品を断面へ反映した直後の 1 箇所</b>で行う。こうすると
        /// EI・EA だけでなく N-M 曲線・M-φ まで同じ E で一貫する。
        ///
        /// 既製杭は指針が E ではなく<b>ヤング係数比 n = 5</b> を規定しているため、
        /// E = n·Ec として与える。Ec が 40,000 以外（Fc 105 級など）でも n が 5 に保たれる。
        /// 鋼管は E = 205,000 の直接指定。
        /// </summary>
        private void ApplyGuideYoungsModulusIfEnabled()
        {
            if (!ConcreteModelOptions.UseGuideYoungsModulus) return;
            if (ConcreteE <= 0) return;

            if (PileTypeNames.IsPhcLikeSection(PileSectionType) || PileTypeNames.IsPrcLikeSection(PileSectionType))
            {
                // PHC杭・PRC杭 (節杭を含む): PC鋼材・鉄筋とも n = 5 固定
                double e = ConcreteModelOptions.GuideModularRatioForPrecast * ConcreteE;
                if (TendonAp > 0) TendonEp = e;
                if (MainBarAg > 0) MainBarEr = e;
            }
            else if (PileSectionType == PileTypeNames.Sc)
            {
                // SC杭の鋼管
                PipeEs = ConcreteModelOptions.GuideSteelYoungsModulus;
            }
        }

        // ───── 換算断面二次モーメント（曲げ剛性 EI の中身）─────
        //
        // Ie = I + (1/2)(np - 1)·Ap·(Dp/2)^2 + (1/2)(nr - 1)·Ag·(Dr/2)^2   [+ 鋼管項]
        //   np = Ep/Ec (PC鋼棒)   nr = Er/Ec (異形棒鋼)
        //
        // 鋼材ごとに配置円 (PCD) も換算比も分けている。JP-NPRC の PRC節杭は 232 行すべてで
        // PC鋼棒の PCD と異形棒鋼の PCD が異なり、分離するとカタログ Ie と最大 0.046% 一致するのに対し、
        // PC鋼棒の PCD で一括すると 1.41% ずれる。
        // 「基礎部材の強度と変形性能」の (Ap+Ag)·rp^2 は PCD が近いことを前提にした簡略式。

        /// <summary>コンクリート中空断面の断面二次モーメント (mm4)。</summary>
        private double ConcreteI =>
            Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 64.0;

        /// <summary>
        /// PC鋼棒の換算断面二次モーメント項 (mm4)。<c>(1/2)(np - 1)·Ap·(Dp/2)^2</c>。
        /// EA には <c>Ep·Ap</c> が入っているのに EI にはこの項が無く、
        /// カタログの換算断面二次モーメント Ie より PHC系で最大 5.9%、
        /// PRC節杭で最大 29.5% 小さくなっていた (2026-08-20 修正)。
        /// </summary>
        private double TendonIEquivalent =>
            ConcreteE > 0 && TendonAp > 0 && TendonDp > 0 && TendonEp > 0
                ? 0.5 * (TendonEp / ConcreteE - 1) * TendonAp * Math.Pow(TendonDp, 2) / 4.0
                : 0.0;

        /// <summary>
        /// 異形棒鋼の換算断面二次モーメント項 (mm4)。<c>(1/2)(nr - 1)·Ag·(Dr/2)^2</c>。
        /// <c>ConcreteOutDia - 2·MainBarCenterCover</c> は鉄筋配置直径 <see cref="MainBarDr"/> と一致する
        /// (製品ライブラリ適用時に <c>MainBarCenterCover = (ConcreteOutDia - MainBarDr)/2</c> と設定するため)。
        /// </summary>
        private double MainBarIEquivalent =>
            ConcreteE > 0 && MainBarAg > 0 && MainBarEr > 0
                ? 0.5 * (MainBarEr / ConcreteE - 1) * MainBarAg
                  * Math.Pow(ConcreteOutDia - 2 * MainBarCenterCover, 2) / 4.0
                : 0.0;

        // 曲げ剛性 (kNm2)
        // ※ 換算項の断面積は MainBarAg / TendonAp（鋼材断面積）を使用。
        //    A0（全断面積）を使用すると過大評価になるので注意
        // ※ コンクリート断面は中空断面（ConcreteOutDia - 2*ConcreteThickness = 内径）として計算
        public double EI => (ConcreteE * (ConcreteI + TendonIEquivalent + MainBarIEquivalent)
            + PipeEs * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 64.0) * Math.Pow(10, -9);

        // ===== 腐食代考慮の断面諸量（諸元の「両方記載」表示専用） =====
        // 腐食モデル: 鋼管外径を 2×腐食代 だけ縮小、管厚を腐食代だけ減じ、内径（コンクリート外径）は不変。
        // （解析用の PipeAs/A0/W/EA/EI は公称寸法のまま。ここは表示比較用）
        private double CorrodedPipeOuterDiaDisp => PipeDia - 2.0 * CorrosionDepth;
        private double PipeInnerDiaDisp => PipeDia - 2.0 * PipeTs; // = コンクリート外径（腐食で不変）

        // 腐食考慮 鋼管断面積 (mm2)
        public double PipeAsCorroded =>
            Math.PI / 4.0 * (Math.Pow(CorrodedPipeOuterDiaDisp, 2) - Math.Pow(PipeInnerDiaDisp, 2));

        // 腐食考慮 杭全断面積 (mm2)
        public double A0Corroded => Ac + MainBarAg + TendonAp + PipeAsCorroded;

        // 腐食考慮 杭単位長さ重量 (kN/m)
        public double WCorroded =>
            ((MainBarAg + TendonAp + PipeAsCorroded) * 78.5 + Ac * ConcreteGamma) * Math.Pow(10, -6);

        // 腐食考慮 軸剛性 (kN)
        public double EACorroded =>
            (ConcreteE * Ac + MainBarEr * MainBarAg + TendonEp * TendonAp + PipeEs * PipeAsCorroded) * 0.001;

        // 腐食考慮 曲げ剛性 (kNm2) — 鋼管項のみ腐食後外径で置換
        public double EICorroded => (ConcreteE * (ConcreteI + TendonIEquivalent + MainBarIEquivalent)
            + PipeEs * Math.PI * (Math.Pow(CorrodedPipeOuterDiaDisp, 4) - Math.Pow(PipeInnerDiaDisp, 4)) / 64.0) * Math.Pow(10, -9);

        // ねじり剛性 (kNm2)
        // ※ コンクリート断面は中空断面として計算
        // ねじり剛性 (kNm2)
        // 円形断面のねじり定数は断面二次極モーメント J = Ip = π(D^4 - d^4)/32。
        // 曲げの断面二次モーメント I = π(D^4 - d^4)/64 を使っていたため 2 倍過小だった。
        public double GJ => (GetG(ConcreteE, 0.2) * Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 32.0 +
            GetG(PipeEs, 0.3) * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 32.0) * Math.Pow(10, -9);

        // せん断剛性
        private static double GetG(double e, double nu)
        {
            return e / (2 * (1 + nu));
        }

        private ObservableCollection<Spec>? _selectedPileSectionSpecification;
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

        /// <summary>
        /// 断面タイプの補足（メーカー・製品系列と、断面耐力の算定がどの杭に準じるか）。
        /// 「BF.S先端軸部」のような製品名だけの断面タイプは、それが PHC節杭 であることが
        /// 名前から読み取れないため、諸元表の備考で補う。
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string SectionTypeNote => PileSectionType switch
        {
            PileTypeNames.PhcNodular => "ジャパンパイル JP-NPH パイル (PHC節杭)。断面耐力は PHC杭 と同一",
            PileTypeNames.PrcNodular => "ジャパンパイル JP-NPRC パイル (PRC節杭)。断面耐力は PRC杭 と同一",
            PileTypeNames.PrcNodularPhcPart => "ジャパンパイル JP-NPRC パイルの PHC部 (PRC節杭)。断面耐力は PHC杭 と同一",
            PileTypeNames.BfsHead => "三谷セキサン BF.S パイル (PHC節杭) の頭部軸部。断面耐力は PHC杭 と同一",
            PileTypeNames.BfsTip => "三谷セキサン BF.S パイル (PHC節杭) の先端軸部。節はこちらに付く。断面耐力は PHC杭 と同一",
            _ => "",
        };

        public void SetSpecs()
        {
            SelectedPileSectionSpecification = [
                new Spec("杭断面タイプ", "", PileSectionType, "", SectionTypeNote),
                new Spec("杭径", "D", $"{PileDiameter:N0}", "mm")];

            if (PileSectionType == PileTypeNames.SteelPipeConcreteSection || PileSectionType == PileTypeNames.Sc || PileSectionType == PileTypeNames.SteelPipe)
            {
                string notePipeDia =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia > 2700) ? "([強度と変形性能]4.1,3)2700より大" :
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia < 600) ? "([強度と変形性能]4.1,3)600未満" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia > 2000) ? "([強度と変形性能]8.1.4(1))2000より大" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia < 318.5) ? "([強度と変形性能]8.1.4(1))318.5未満" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管外径", "PipeDia", $"{PipeDia:N0}", "mm", notePipeDia));

                string notePipeTs =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeTs < 6) ? "([強度と変形性能]4.1,3)6未満" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeTs > 6) ? "([強度と変形性能]8.1.4(2))6未満" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管厚", "Ts", $"{PipeTs:N0}", "mm", notePipeTs));
                SelectedPileSectionSpecification.Add(
                    new Spec("腐食代", "", $"{CorrosionDepth:N1}", "mm"));
                string noteDonTs =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia / PipeTs > 125) ? "([強度と変形性能]4.1,3)125より大" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia / PipeTs > 100) ? "([強度と変形性能]8.1.4(2))100より大" : "";
                string pipeDiaTsValue = (PipeTs != 0) ? $"{PipeDia / PipeTs:N1}" : "N/A";
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管径厚比", "Tc/Ts", pipeDiaTsValue, "", noteDonTs));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管断面積(腐食非考慮)", "As", $"{PipeAs:N0}", "mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管断面積(腐食考慮)", "As'", $"{PipeAsCorroded:N0}", "mm2"));
                // 製品ライブラリの選択では鋼管規格は切り替わらない (杭径・鋼管厚だけ転記される)。
                // メーカー製品は規格が決まっているので、選択中の規格と食い違っていたら諸元表で知らせる。
                // 製品ライブラリの降伏点 (メーカー公称) と、鋼管規格の基準強度 F は別の量なので
                // (SKK490 なら JIS の規格降伏点 315 / 基準強度 F=325) わずかな差では知らせない。
                // 規格の選び間違い (SKK490 の製品に SKK400 を選ぶ等) だけを拾う。
                string noteGrade = "";
                double productF = SelectedPrecastPile?.Fts ?? 0.0;
                if (productF > 0)
                {
                    double gradeF = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400").F;
                    if (gradeF < productF * 0.95)
                        noteGrade = $"製品ライブラリの鋼管降伏点 {productF:N0} N/mm² に対し、" +
                                    $"選択中の鋼管規格 {PipeGrade} の基準強度は {gradeF:N0} N/mm² です。" +
                                    "製品選択では鋼管規格は切り替わりません。規格の選択をご確認ください";
                }
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管規格", "", PipeGrade, "", noteGrade));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管ヤング係数", "Es", $"{PipeEs:N0}", "N/mm2"));
            }

            if (PileSectionType != PileTypeNames.SteelPipe)
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート外径", "Dc", $"{ConcreteOutDia:N0}", "mm"));

            if (IsHollowPrecastSection)
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート肉厚", "Dt", $"{ConcreteThickness:N0}", "mm"));

            // 節杭 固有: 節部径とカタログ標準質量、および周面抵抗の扱いの明示
            if (IsNodularPile)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("節部径", "Do", $"{NodeDiameter:N0}", "mm",
                        "支持力の周面抵抗は軸部径で算定 (節の効果は未考慮)"));
                if (CatalogMassPerM > 0)
                    SelectedPileSectionSpecification.Add(
                        new Spec("カタログ標準質量", "m", $"{CatalogMassPerM:N3}", "t/m",
                            "自重はこの値による (コンクリート単位体積重量 γc は不適用)"));
                else
                    SelectedPileSectionSpecification.Add(
                        new Spec("自重の算定", "", "軸部断面 × γc", "",
                            "カタログに標準質量表が無いため軸部体積による (節の分は未計上)"));
                SelectedPileSectionSpecification.Add(
                    new Spec("杭頭タイプ", "", NodularHeadType, "", NodularHeadNote));
                if (NodeHeadDiameter > 0)
                {
                    SelectedPileSectionSpecification.Add(
                        new Spec("拡頭部径", "Dt", $"{NodeHeadDiameter:N0}", "mm"));
                    SelectedPileSectionSpecification.Add(
                        new Spec("拡頭部長さ", "Lt", $"{NodeHeadLength:N0}", "mm"));
                }
            }

            if (PileSectionType != PileTypeNames.SteelPipe)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート断面積", "Ac", $"{Ac:N0}", "mm2"));
                string noteFc =
                    PileSectionType == PileTypeNames.RcSection && ConcreteFc < 21 ? "([強度と変形性能]3.1,2)21N/mm2未満" :
                    PileSectionType == PileTypeNames.RcSection && ConcreteFc > 40 ? "([強度と変形性能]3.1,2)40N/mm2より大" :
                    PileSectionType == PileTypeNames.SteelPipeConcreteSection && ConcreteFc < 21 ? "([強度と変形性能]4.1,2)21N/mm2未満" :
      PileSectionType == PileTypeNames.SteelPipeConcreteSection && ConcreteFc > 40 ? "([強度と変形性能]4.1,2)40N/mm2より大" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート基準強度", "Fc", $"{ConcreteFc:N0}", "N/mm2", noteFc));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート単位体積重量", "γc", $"{ConcreteGamma:N1}", "kN/m3"));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート縦弾性係数", "Ec", $"{ConcreteE:N0}", "N/mm2"));
            }

            if (PileBodyType == PileTypeNames.InsituRc || PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート施工品質管理係数", "ξ", $"{ConcreteGsi:N2}", ""));
            }

            if (IsPrestressedPrecastSection)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリートプレストレス", "σe", $"{Prestress:N1}", "N/mm2"));
            }

            if (IsPrestressedPrecastSection)
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

            if (PileSectionType == PileTypeNames.RcSection || PileSectionType == PileTypeNames.Prc ||
                (PileSectionType == PileTypeNames.SteelPipeConcreteSection && MainBarAg > 0))
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋数-呼び径", "", MainBarNum.ToString() + "-" + MainBarSize, ""));
                // 鋼管規格と同じく、製品ライブラリの選択では鉄筋規格は切り替わらない
                // (本数・径・配筋径だけが転記される)。メーカー製品は規格が決まっているので、
                // 選択中の規格の降伏点と食い違っていたら諸元表で知らせる。
                string noteBarSpec = "";
                double productFtr = SelectedPrecastPile?.Ftr ?? 0.0;
                if (productFtr > 0)
                {
                    double specFy = MainBars.GradeYieldStrength(MainBarSpec);
                    if (Math.Abs(specFy - productFtr) > 1.0)
                        noteBarSpec = $"製品ライブラリの主筋降伏点は {productFtr:N0} N/mm² です" +
                                      $"(選択中の {MainBarSpec} は {specFy:N0} N/mm²)。" +
                                      "製品選択では鉄筋規格は切り替わりません。規格の選択をご確認ください";
                }
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋規格", "", $"{MainBarSpec}", "", noteBarSpec));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋断面積", "Ag", $"{MainBarAg:N0}", "mm2"));
                string notePg =
                    PileSectionType == PileTypeNames.RcSection && MainBarAg / A0 * 100 < 0.4 ? "([強度と変形性能]3.1,5(5))0.4%未満" : "";
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

            if (PileSectionType == PileTypeNames.RcSection || PileSectionType == PileTypeNames.Prc)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("せん断補強筋規格", "", $"{HoopSpec}", ""));

                if (PileSectionType == PileTypeNames.Prc)
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
                        PileSectionType == PileTypeNames.RcSection && HoopPw * 100 < 0.1 ? "([強度と変形性能]3.1,5(6))0.1%未満" : "";
                    string hoopPwValue = (HoopSpacing != 0 && ConcreteOutDia != 0) ? $"{HoopPw * 100:N2}" : "N/A";
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋比", "pw", hoopPwValue, "%", noteHoopPw));
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋重心かぶり厚", "", $"{HoopCenterCover:N0}", "mm"));
                }
            }

            //new Spec("PCリング定着筋呼び径", "", BarSize, ""),

            // 鋼管を持つ断面（鋼管コンクリート部/SC杭/鋼管杭）では腐食考慮/非考慮の両方を記載する。
            bool hasPipe = PipeAs > 0;
            void AddCorrodible(string name, string sym, string unit, string fmt, double nominal, double corroded)
            {
                if (hasPipe)
                {
                    SelectedPileSectionSpecification.Add(new Spec($"{name}(腐食非考慮)", sym, nominal.ToString(fmt), unit));
                    SelectedPileSectionSpecification.Add(new Spec($"{name}(腐食考慮)", sym + "'", corroded.ToString(fmt), unit));
                }
                else
                {
                    SelectedPileSectionSpecification.Add(new Spec(name, sym, nominal.ToString(fmt), unit));
                }
            }

            AddCorrodible("杭の全断面積", "A0", "mm2", "N0", A0, A0Corroded);
            AddCorrodible("杭の単位長さ重量", "W", "kN/m", "N2", W, WCorroded);
            AddCorrodible("杭の弾性軸剛性", "EA", "kN", "N0", EA, EACorroded);
            AddCorrodible("杭の弾性曲げ剛性", "EI", "kNm2", "N0", EI, EICorroded);
            //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            //return specs;
        }

        /// <summary>
        /// NM曲線上に、計算で求めた(nTarget, mCalc)の点を適切な位置に挿入する
        /// Mが最大となる区間の近くに挿入する
        /// </summary>
        private static void InsertCalculatedPointNM(List<double> ns, List<double> ms, double nTarget, double mCalc)
        {
            // nTargetを跨ぐ全区間を探し、既存Mが最大の区間に挿入
            int bestIdx = -1;
            double bestM = double.MinValue;
            for (int i = 0; i < ns.Count - 1; i++)
            {
                if ((ns[i] - nTarget) * (ns[i + 1] - nTarget) <= 0 && Math.Abs(ns[i + 1] - ns[i]) > 1e-10)
                {
                    double mMid = (ms[i] + ms[i + 1]) * 0.5;
                    if (mMid > bestM)
                    {
                        bestM = mMid;
                        bestIdx = i;
                    }
                }
            }
            if (bestIdx >= 0)
            {
                ns.Insert(bestIdx + 1, nTarget);
                ms.Insert(bestIdx + 1, mCalc);
            }
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
            {
                // 鋼管杭+鋼管部 は IPileSectionCalculation 実装を持たない。
                // 軸力制限はユーザ指定式で更新し、M-N 曲線もユーザ指定式 (Ms / Md) で構築。
                UpdateSteelPipeAxialThresholds();
                if (PileBodyType == PileTypeNames.SteelPipe && PileSectionType == PileTypeNames.SteelPipeSection)
                {
                    if (propertyName == nameof(UnfactoredServiceNM)
                        || propertyName == nameof(FactoredServiceNM))
                    {
                        return BuildSteelPipeServiceNMRaw();
                    }
                    if (propertyName == nameof(UnfactoredDamageNM)
                        || propertyName == nameof(FactoredDamageNM)
                        || propertyName == nameof(FactoredDamageNMLevel1))
                    {
                        return BuildSteelPipeDamageNMRaw();
                    }
                    if (propertyName == nameof(UnfactoredUltimateNM)
                        || propertyName == nameof(FactoredUltimateNM))
                    {
                        return BuildSteelPipeUltimateNMRaw();
                    }
                }
                return ([], []);
            }

            // 軸力閾値を更新
            UltimateLimitAxialForceThresholds = [.. section.UltimateLimitAxialForceThresholds];
            if (section is AbstractPileSection abs && abs.DamageLimitAxialForceThresholds != null)
                DamageLimitAxialForceThresholds = [.. abs.DamageLimitAxialForceThresholds];

            // 鋼管杭+コンクリート充填鋼管部 は M-N 曲線は SPRC と共有 (上記 section から取得済) するが、
            // 軸力制限は基礎部材の強度と変形性能 2022 の SteelPipeSection 系式で上書きする。
            bool isSteelFilledTube = PileBodyType == PileTypeNames.SteelPipe && PileSectionType == PileTypeNames.CftSection;
            if (isSteelFilledTube)
            {
                UpdateSteelPipeAxialThresholds();
            }
            // 使用限界軸力制限値を転送（PrecastPileSection用）
            if (section is PrecastPileSection precast)
            {
                ServiceLimitNMin = precast.ServiceLimitNMin;
                ServiceLimitNMax = precast.ServiceLimitNMax;
            }
            // せん断の軸力制限値を転送（既製杭用、N単位のまま、XAMLのMultiplyConverterで0.001倍）
            if (section is PrecastPileSection precastShear)
            {
                ShearNMinService = precastShear.ShearNMinService;
                ShearNMaxService = precastShear.ShearNMaxService;
                ShearNMinDamage = precastShear.ShearNMinDamage;
                ShearNMaxDamage = precastShear.ShearNMaxDamage;
                ShearNMinUltimate = precastShear.ShearNMinUltimate;
                ShearNMaxUltimate = precastShear.ShearNMaxUltimate;
            }

            // プロパティ名に応じた NM を取得
            var (n, m, _, _) = propertyName switch
            {
                nameof(UnfactoredServiceNM) => section.UnfactoredServiceNM,
                nameof(UnfactoredDamageNM) => section.UnfactoredDamageNM,
                nameof(UnfactoredUltimateNM) => section.UnfactoredUltimateNM,
                nameof(FactoredServiceNM) => section.FactoredServiceNM,
                nameof(FactoredDamageNM) => section.FactoredDamageNM,
                nameof(FactoredDamageNMLevel1) => section.FactoredDamageNMLevel1,
                nameof(FactoredUltimateNM) => section.FactoredUltimateNM,
                _ => (new List<double>(), new List<double>(), new List<double>(), new List<double>())
            };

            // 鋼管杭+コンクリート充填鋼管部 の使用限界 M-N はユーザ指定の Ms 式で上書きする
            // (SPRC のファイバー積分結果ではなく、鋼管降伏ベースの線形相互作用)。
            if (isSteelFilledTube && (propertyName == nameof(UnfactoredServiceNM)
                            || propertyName == nameof(FactoredServiceNM)))
            {
                var (svcN, svcM) = BuildSteelPipeServiceNMRaw();
                if (svcN.Count > 0)
                {
                    return (svcN, svcM);
                }
            }

            // 鋼管杭+コンクリート充填鋼管部 の損傷限界 M-N はユーザ指定の Md 式 (杭頭部) で上書き。
            // sMd (鋼管部寄与、5 ケース分岐) + cMd (充填コン寄与、Xn 依存) の合成。
            if (isSteelFilledTube && (propertyName == nameof(UnfactoredDamageNM)
                            || propertyName == nameof(FactoredDamageNM)
                            || propertyName == nameof(FactoredDamageNMLevel1)))
            {
                var (dmgN, dmgM) = BuildSteelPipeDamageNMRaw();
                if (dmgN.Count > 0)
                {
                    return (dmgN, dmgM);
                }
            }

            // 鋼管杭+コンクリート充填鋼管部 の安全限界 M-N はユーザ指定の Mu 式 (杭頭部) で上書き。
            // sMu = 4·srm²·t·sin(sθO)·sσU と cMu = (2/3)·cσIr·(cro³·sin³(cθO) − cri³·sin³(cθI)) の合成。
            if (isSteelFilledTube && (propertyName == nameof(UnfactoredUltimateNM)
                            || propertyName == nameof(FactoredUltimateNM)))
            {
                var (ultN, ultM) = BuildSteelPipeUltimateNMRaw();
                if (ultN.Count > 0)
                {
                    return (ultN, ultM);
                }
            }

            // 鋼管杭+コンクリート充填鋼管部: 低減後 (Factored*) 曲線を SteelPipeSection 系の
            // 新しい軸力閾値で再クリップする。低減前 (Unfactored*) は SPRC 計算のまま表示。
            if (isSteelFilledTube && propertyName.StartsWith("Factored"))
            {
                (List<double> uN, List<double> uM, _, _) = propertyName switch
                {
                    nameof(FactoredServiceNM) => section.UnfactoredServiceNM,
                    nameof(FactoredDamageNM) => section.UnfactoredDamageNM,
                    nameof(FactoredDamageNMLevel1) => section.UnfactoredDamageNM,
                    nameof(FactoredUltimateNM) => section.UnfactoredUltimateNM,
                    _ => (new List<double>(), new List<double>(), new List<double>(), new List<double>())
                };

                (double nMinN, double nMaxN) = propertyName switch
                {
                    nameof(FactoredServiceNM) =>
                        // ServiceLimitNMin/NMax は kN なので N に変換
                        (ServiceLimitNMin * UnitConversion.KN_TO_N, ServiceLimitNMax * UnitConversion.KN_TO_N),
                    nameof(FactoredDamageNM) or nameof(FactoredDamageNMLevel1) =>
                        DamageLimitAxialForceThresholds.Count >= 2
                            ? (DamageLimitAxialForceThresholds[0], DamageLimitAxialForceThresholds[1])
                            : (double.NaN, double.NaN),
                    nameof(FactoredUltimateNM) =>
                        UltimateLimitAxialForceThresholds.Count >= 2
                            ? (UltimateLimitAxialForceThresholds[0], UltimateLimitAxialForceThresholds[1])
                            : (double.NaN, double.NaN),
                    _ => (double.NaN, double.NaN)
                };

                if (uN.Count > 0 && double.IsFinite(nMinN) && double.IsFinite(nMaxN))
                {
                    (n, m) = ClipNMByThresholds(uN, uM, nMinN, nMaxN);
                }
            }

            // 低減前安全限界曲線に軸力閾値の計算点を挿入（曲線が閾値を正確に通るようにする）
            // 場所打ち鋼管コンクリート杭のみ（他の杭種ではNM曲線の非単調性で不正な点が挿入される）
            if (propertyName == nameof(UnfactoredUltimateNM) && n.Count > 1
                && section is InsituSteelPipeReinforcedConcreteSection sprcSection)
            {
                var thresholds = sprcSection.UltimateLimitAxialForceThresholds;
                if (thresholds != null)
                {
                    foreach (double nThreshold in thresholds)
                    {
                        var (mCalc, _) = sprcSection.GetUltimateMomentForSpecificN(nThreshold);
                        if (mCalc > 0)
                            InsertCalculatedPointNM(n, m, nThreshold, mCalc);
                    }
                }
            }

            return (n, m);
        }

        /// <summary>
        /// NQ曲線を取得し、スケーリング後（kN, kN）で返す
        /// </summary>
        private (List<double> N, List<double> Q) GetNQScaled(string propertyName)
        {
            var section = CreateSectionCalculator();
            if (section is not AbstractPileSection absSection)
            {
                // 鋼管杭 (鋼管部 / コンクリート充填鋼管部) は IPileSectionCalculation を実装しないため
                // 通常パスでは ([], []) になり、せん断力グラフで限界状態線が描画されない。
                // NM と同じく Build* メソッドで N-Q 曲線を構築する。
                if (PileBodyType == PileTypeNames.SteelPipe
                    && (PileSectionType == PileTypeNames.SteelPipeSection || PileSectionType == PileTypeNames.CftSection))
                {
                    UpdateSteelPipeAxialThresholds();
                    var (sN, sQ) = propertyName switch
                    {
                        nameof(UnfactoredServiceNQ) or nameof(FactoredServiceNQ)
                            => BuildSteelPipeServiceNQRaw(),
                        nameof(UnfactoredDamageNQ) or nameof(FactoredDamageNQ)
                            => BuildSteelPipeDamageNQRaw(),
                        nameof(UnfactoredUltimateNQ) or nameof(FactoredUltimateNQ)
                            => BuildSteelPipeUltimateNQRaw(),
                        _ => ((List<double>)[], (List<double>)[])
                    };
                    if (sN.Count == 0) return ([], []);
                    return (
                        GetMultipliedListValues(sN, 1e-3),
                        GetMultipliedListValues(sQ, 1e-3)
                    );
                }
                return ([], []);
            }

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

        /// <summary>
        /// せん断耐力の内訳曲線 1 本。<see cref="ComputeQNShearComponents"/> が返す。
        /// </summary>
        /// <param name="LimitState">"使用限界" / "損傷限界" / "安全限界"</param>
        /// <param name="Mode">"斜め引張破壊" / "ウェブ破壊"</param>
        public sealed record ShearComponentCurve(
            string LimitState, string Mode, List<double> N, List<double> Q);

        /// <summary>
        /// せん断耐力の内訳（斜め引張破壊 / ウェブ破壊）の QN 曲線を返す（kN 単位、β 低減前）。
        ///
        /// PHC・PRC のせん断耐力は 2 式の小さい方で決まるが、採用値だけではどちらが
        /// 効いているか分からない。図に点線で重ねられるよう内訳を返す。
        /// 2 本の小さい方が「低減前」の曲線に一致する。
        /// 対象外の杭種（SC・場所打ち系・鋼管杭）では空リストを返す。
        /// </summary>
        public List<ShearComponentCurve> ComputeQNShearComponents(double monQd, int iCount = 100)
        {
            var result = new List<ShearComponentCurve>();
            // PHC / PRC のみ。SC は式が別系統、場所打ち系は 2 式に分かれない。
            if (CreateSectionCalculator() is not PrecastPileSection precast
                || precast is SCSection)
                return result;

            void Add(string limitState, (double DiagonalTension, double Web) c,
                     double nMin, double nMax)
            {
                foreach (var (mode, q) in new[]
                         { ("斜め引張破壊", c.DiagonalTension), ("ウェブ破壊", c.Web) })
                {
                    if (!double.IsFinite(q) || q <= 0.0) continue;
                    var ns = new List<double>(iCount);
                    var qs = new List<double>(iCount);
                    for (int i = 0; i < iCount; i++)
                    {
                        ns.Add((nMin * (iCount - i) + nMax * i) / iCount * 1e-3);
                        qs.Add(q * 1e-3);
                    }
                    result.Add(new ShearComponentCurve(limitState, mode, ns, qs));
                }
            }

            Add("使用限界", precast.GetServiceLimitShearComponents(monQd),
                precast.ShearNMinService, precast.ShearNMaxService);
            Add("損傷限界", precast.GetDamageLimitShearComponents(monQd),
                precast.ShearNMinDamage, precast.ShearNMaxDamage);
            Add("安全限界", precast.GetUltimateLimitShearComponents(monQd),
                precast.ShearNMinUltimate, precast.ShearNMaxUltimate);
            return result;
        }

        /// <summary>
        /// 指定したM/Qdで全6種のQN曲線を再計算して返す（kN単位）
        /// </summary>
        public (
            (List<double> N, List<double> Q) UnfactoredService,
            (List<double> N, List<double> Q) FactoredService,
            (List<double> N, List<double> Q) UnfactoredDamage,
            (List<double> N, List<double> Q) FactoredDamage,
            (List<double> N, List<double> Q) UnfactoredUltimate,
            (List<double> N, List<double> Q) FactoredUltimate
        ) ComputeQNForMonQd(double monQd, int damageLevel = 2)
        {
            var section = CreateSectionCalculator();
            if (section is not AbstractPileSection absSection)
                return default;

            (List<double> N, List<double> Q) Scale((List<double>, List<double>) raw)
            {
                var (q, n) = raw; // AbstractPileSection stores (Q, N)
                return (
                    GetMultipliedListValues(n, 1e-3),
                    GetMultipliedListValues(q, 1e-3)
                );
            }

            // 場所打ち鋼管コンクリート杭はmonQd非依存のQN（引数なし版）を使用、damageLevel も無視（β=1.0）
            if (absSection is InsituSteelPipeReinforcedConcreteSection sprc)
            {
                return (
                    Scale(sprc.GetServiceLimitQNInteraction()),
                    Scale(sprc.GetServiceLimitQNInteraction()),
                    Scale(sprc.GetDamageLimitQNInteraction()),
                    Scale(sprc.GetDamageLimitQNInteraction()),
                    Scale(sprc.GetUltimateQNInteraction()),
                    Scale(sprc.GetUltimateQNInteraction())
                );
            }

            // 場所打ち鉄筋コンクリート杭はpw, sigmaWyが追加で必要
            if (absSection is InsituReinforcedConcreteSection rcSec)
            {
                double pw = HoopPw;
                double sigmaWy = HoopSigmay;
                return (
                    Scale(rcSec.GetServiceLimitQNInteraction(monQd, false)),
                    Scale(rcSec.GetServiceLimitQNInteraction(monQd, true)),
                    Scale(rcSec.GetDamageLimitQNInteraction(monQd, false, damageLevel)),
                    Scale(rcSec.GetDamageLimitQNInteraction(monQd, true, damageLevel)),
                    Scale(rcSec.GetUltimateQNInteraction(monQd, pw, sigmaWy, false)),
                    Scale(rcSec.GetUltimateQNInteraction(monQd, pw, sigmaWy, true))
                );
            }

            // サブクラス（PHC/PRC/SC）の GetXxxQNInteraction を dynamic で呼び出す
            dynamic d = absSection;
            try
            {
                return (
                    Scale(d.GetServiceLimitQNInteraction(monQd, false)),
                    Scale(d.GetServiceLimitQNInteraction(monQd, true)),
                    Scale(d.GetDamageLimitQNInteraction(monQd, false, damageLevel)),
                    Scale(d.GetDamageLimitQNInteraction(monQd, true, damageLevel)),
                    Scale(d.GetUltimateQNInteraction(monQd, false)),
                    Scale(d.GetUltimateQNInteraction(monQd, true))
                );
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("Q-N 曲線の算定（→空）", ex,
                    $"PileBodyType={PileBodyType}, PileSectionType={PileSectionType}");
                return default;
            }
        }

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
        /// 鋼管杭 (鋼管部 / コンクリート充填鋼管部) の使用・損傷・安全限界軸力を
        /// SteelPipeSection から直接算定し、ServiceLimitNMin/NMax,
        /// DamageLimitAxialForceThresholds, UltimateLimitAxialForceThresholds
        /// に反映する。β1=β2=1.0 として以下の式に従う:
        ///
        ///   使用限界:
        ///     Nsc = β1·sNsc1 = β1·sfc1·sAp
        ///     Nst = β1·sNst  = β1·sft·sAp
        ///
        ///   損傷限界 (鋼管部):
        ///     Ndc = β1·sNdc1 = β1·1.5·sfc1·sAp
        ///     Ndt = β1·sNdt  = β1·1.5·sft·sAp
        ///
        ///   損傷限界 (コンクリート充填鋼管部):
        ///     Ndc = β1·(sNdc1 + cNdc),  cNdc = cσck·Air
        ///     Ndt = β1·sNdt
        ///
        ///   安全限界 (両方共通):
        ///     Nuc = β1·β2·sNuc1 = β1·β2·sσCy1·sAp,  sσCy1 = 1.1·1.5·sfc1
        ///     Nut = β1·β2·sNut  = β1·β2·sσTy ·sAp,  sσTy  = 1.1·1.5·sft
        ///
        /// 単位: ServiceLimitNMin/NMax は kN、Damage/Ultimate Thresholds は N。
        /// 必要入力が欠落している場合は何もしない。
        /// </summary>
        /// <summary>
        /// 鋼管杭用の SteelPipeSection (基礎部材の強度と変形性能 2022 準拠) を構築する。
        /// PileSectionType=PileTypeNames.SteelPipeSection は Fc=0、PileTypeNames.CftSection は Fc=ConcreteFc を渡す。
        /// β1=1.0、e=205000 N/mm² 固定。必要入力が欠落していれば null。
        /// </summary>
        private SteelPipeSection? TryCreateSteelPipeSection()
        {
            if (PileBodyType != PileTypeNames.SteelPipe) return null;
            if (PileSectionType != PileTypeNames.SteelPipeSection && PileSectionType != PileTypeNames.CftSection) return null;
            if (PileDiameter <= 0 || CorrodedPipeTs <= 0) return null;

            var (sigmaU, f) = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400");
            double fcForSection = PileSectionType == PileTypeNames.CftSection ? ConcreteFc : 0.0;

            return new SteelPipeSection(
                PileDiameter, CorrodedPipeTs, f,
                _beta1: 1.0,
                fc: fcForSection,
                sigmaB: sigmaU,
                e: 205000.0);
        }

        /// <summary>
        /// 鋼管杭の損傷限界 M-N 曲線を構築する。
        /// ユーザ指定式に従い β1 = 1.0:
        ///
        /// 鋼管部 (杭中間部・杭下部、コンクリート充填なし):
        ///   Md = β1·(1.5·sfc1 − |Ndd|/sAp)·sZe
        ///
        /// コンクリート充填鋼管部 (杭頭部):
        ///   Ndd &lt; 0:                    Md = β1·sMd
        ///   0 ≤ Ndd ≤ cNdc:              Md = β1·(sMd + cMd)
        ///   cNdc &lt; Ndd:                 Md = β1·sMd
        ///
        ///   sMd: 鋼管部寄与 (5 ケース分岐、ユーザ指定式)
        ///   cMd: 充填コンクリート部寄与 (Xn 依存、ユーザ指定式)
        ///   cNdc = cσck·Air, cσck = (2/3)·α·√(sApf/(zn·Atr))·Fc
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeDamageNMRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            // 軸力スイープ範囲 = 損傷限界軸力閾値 (UpdateSteelPipeAxialThresholds で設定済)
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            Func<double, double> getMd = isFilledTube
                ? sps.GetDamageLimitMomentHead
                : sps.GetDamageLimitMomentMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Ndd = nMin + (nMax - nMin) * i / nDiv;
                double m = Math.Max(0.0, getMd(Ndd));
                ns.Add(Ndd);
                ms.Add(m);
            }
            return (ns, ms);
        }

        /// <summary>
        /// 鋼管杭の安全限界 M-N 曲線を構築する。
        /// ユーザ指定式に従い β1 = β2 = 1.0:
        ///
        /// 鋼管部 (杭中間部・杭下部):
        ///   |Nud|/sNuc &gt; 0.2:  Mu = β1·β2·1.25·sσCy1·(1 − |Nud|/sNuc)·sZe
        ///   |Nud|/sNuc ≤ 0.2:   Mu = β1·β2·sσCy1·sZp        (塑性プラトー)
        ///   sNuc = min(sNuc1, sNuc2)
        ///
        /// コンクリート充填鋼管部 (杭頭部):
        ///   Mu = β1·β2·(sMu + cMu)
        ///   sMu = 4·srm²·t·sin(sθO)·sσU,   sσU = ((π−sθO)·sσTy + sθO·sσCy1)/π
        ///   cMu = (2/3)·cσIr·(cro³·sin³(cθO) − cri³·sin³(cθI))
        ///   sθO, cθO, cθI は中立軸位置 Xn から導出 (Xn は Ns(Xn) + Nc(Xn) = Nud から二分法で解く)。
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeUltimateNMRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            if (UltimateLimitAxialForceThresholds == null
                || UltimateLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = UltimateLimitAxialForceThresholds[0];
            double nMax = UltimateLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            Func<double, double> getMu = isFilledTube
                ? sps.GetUltimateLimitMomentHead
                : sps.GetUltimateLimitMomentMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Nud = nMin + (nMax - nMin) * i / nDiv;
                double m = Math.Max(0.0, getMu(Nud));
                ns.Add(Nud);
                ms.Add(m);
            }
            return (ns, ms);
        }

        /// <summary>
        /// 鋼管杭 (鋼管部 / コンクリート充填鋼管部 共通) の使用限界 M-N 曲線を構築する。
        /// ユーザ指定式:
        ///   Ms = β1·(sf − |Nsd|/sAp)·sZe,   β1 = 1
        ///   sf = sfc1 (Nsd ≥ 0)、sft (Nsd &lt; 0)
        ///   sZe = π·(D⁴ − (D−2t)⁴) / (32·D)   (腐食代考慮済み有効断面係数)
        ///   sAp = π·(D² − (D−2t)²) / 4
        ///   sft = F/1.5
        ///   sfc1 = F/1.5            (D/t ≤ 25)
        ///        = F/1.5·(0.8 + 5/(D/t))  (D/t &gt; 25、局部座屈低減)
        /// 戻り値は (N [N単位], M [N·mm単位])。範囲外で M&lt;0 は 0 にクランプ。
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeServiceNMRaw()
        {
            if (PileBodyType != PileTypeNames.SteelPipe) return ([], []);
            if (PileSectionType != PileTypeNames.SteelPipeSection && PileSectionType != PileTypeNames.CftSection) return ([], []);
            double D = PileDiameter;
            double t = CorrodedPipeTs;
            if (D <= 0 || t <= 0 || D <= 2 * t) return ([], []);

            (_, double F) = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400");

            double sAp = Math.PI * (D * D - (D - 2 * t) * (D - 2 * t)) / 4.0;
            double sZe = Math.PI * (Math.Pow(D, 4) - Math.Pow(D - 2 * t, 4)) / (32.0 * D);
            double sft = F / 1.5;
            double sfc1 = D / t > 25.0
                ? F / 1.5 * (0.8 + 5.0 / (D / t))
                : F / 1.5;

            const double beta1 = 1.0;
            double Nst = beta1 * sft  * sAp;   // 引張容量 (正値)
            double Nsc = beta1 * sfc1 * sAp;   // 圧縮容量

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            double nMin = -Nst;
            double nMax =  Nsc;
            for (int i = 0; i <= nDiv; i++)
            {
                double Nsd = nMin + (nMax - nMin) * i / nDiv;
                double sf = (Nsd >= 0) ? sfc1 : sft;
                double m = beta1 * (sf - Math.Abs(Nsd) / sAp) * sZe;
                ns.Add(Nsd);
                ms.Add(Math.Max(0.0, m));
            }
            return (ns, ms);
        }

        // ========== 鋼管杭 N-Q 曲線 ==========
        // SteelPipeSection は AbstractPileSection を継承していないため、GetNQScaled の
        // 通常パスでは ([], []) が返り、せん断力グラフで限界状態線が描画されなかった。
        // 以下 3 メソッドで NM と同じ建付けで N-Q 曲線を構築する。
        // 戻り値は (N [N単位], Q [N単位]) — GetNQScaled 側で N→kN, Q→kN に変換される。

        /// <summary>
        /// 鋼管杭 (鋼管部・コンクリート充填鋼管部 共通) の使用限界 N-Q 曲線を構築する。
        /// Qs = β1 × sfs × sAp / κ は軸力非依存なので、損傷限界軸力範囲で水平線を引く。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeServiceNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            double q = sps.GetServiceLimitShear();
            return ([nMin, nMax], [q, q]);
        }

        /// <summary>
        /// 鋼管杭 の損傷限界 N-Q 曲線。Qd = 1.5 × Qs (軸力非依存) → 水平線。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeDamageNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            double q = sps.GetDamageLimitShear();
            return ([nMin, nMax], [q, q]);
        }

        /// <summary>
        /// 鋼管杭 の安全限界 N-Q 曲線。
        /// 鋼管部 (中間部・下部): Qu = β1 β2 × sQ0 × √(1 − η²),  η = Nud / sNy
        /// コンクリート充填鋼管部 (杭頭部): 上式に Mu/sMu 比を乗じる
        /// 軸力依存なので安全限界軸力範囲で多点プロット。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeUltimateNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (UltimateLimitAxialForceThresholds == null
                || UltimateLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = UltimateLimitAxialForceThresholds[0];
            double nMax = UltimateLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            Func<double, double> getQu = isFilledTube
                ? sps.GetUltimateLimitShearHead
                : sps.GetUltimateLimitShearMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var qs = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Nud = nMin + (nMax - nMin) * i / nDiv;
                double q = Math.Max(0.0, getQu(Nud));
                ns.Add(Nud);
                qs.Add(q);
            }
            return (ns, qs);
        }

        private void UpdateSteelPipeAxialThresholds()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return;

            const double beta1 = 1.0;
            const double beta2 = 1.0;

            // 使用限界 (kN)
            double sNsc1 = sps.Sfc1 * sps.sAp;   // = sfc1·sAp [N]
            double sNst  = sps.sft  * sps.sAp;   // = sft ·sAp [N]
            ServiceLimitNMin = -beta1 * sNst  * 1e-3;  // 引張 (負)
            ServiceLimitNMax =  beta1 * sNsc1 * 1e-3;  // 圧縮

            // 損傷限界 (N)
            double sNdc1 = 1.5 * sps.Sfc1 * sps.sAp;
            double sNdtPos = 1.5 * sps.sft  * sps.sAp;
            double Ndt = -beta1 * sNdtPos;
            double Ndc = PileSectionType == PileTypeNames.CftSection
                ? beta1 * (sNdc1 + sps.cNdc)
                : beta1 * sNdc1;
            DamageLimitAxialForceThresholds = [Ndt, Ndc];

            // 安全限界 (N)
            // 鋼管部 (杭中間部・杭下部):
            //   Nuc = β1·β2·min(sNuc1, sNuc2),    sNuci = sσCyi·sAp,  sσCyi = 1.1·1.5·sfci
            //   Nut = β1·β2·sNut,                 sNut  = sσTy·sAp,   sσTy  = 1.1·1.5·sft
            // コンクリート充填鋼管部 (杭頭部):
            //   Nuc = β1·β2·(sNuc1 + cNuc),       cNuc = cσIr·Air, cσIr = α·√(sApf/(zn·Air))·Fc
            //   Nut = β1·β2·sNut1,                sNut1 = σB·sAp   (ultimate tensile strength)
            double Nuc, Nut;
            if (PileSectionType == PileTypeNames.CftSection)
            {
                Nuc = beta1 * beta2 * (sps.sNuc1 + sps.cNuc);
                Nut = beta1 * beta2 * sps.sNut1;
            }
            else
            {
                Nuc = beta1 * beta2 * Math.Min(sps.sNuc1, sps.sNuc2);
                Nut = beta1 * beta2 * sps.sNut;
            }
            UltimateLimitAxialForceThresholds = [-Nut, Nuc];
        }

        /// <summary>
        /// 軸力制限 [nMin, nMax] (どちらも N 単位) で N-M 曲線をクリップする。
        /// 範囲外では M=0、境界では (nMin, 0) → (nMin, M_補間) → ... → (nMax, M_補間) → (nMax, 0)
        /// と垂直降下/上昇する点列を返す。PrecastPileSection.ApplyAxialForceLimitsToNM と同等。
        /// </summary>
        private static (List<double> ns, List<double> ms) ClipNMByThresholds(
            List<double> uN, List<double> uM, double nMin, double nMax)
        {
            var ns = new List<double>();
            var ms = new List<double>();

            ns.Add(nMin);
            ms.Add(0.0);

            double mAtMin = InterpolateMAtN(uN, uM, nMin);
            if (mAtMin > 0)
            {
                ns.Add(nMin);
                ms.Add(mAtMin);
            }

            for (int i = 0; i < uN.Count; i++)
            {
                if (uN[i] >= nMin && uN[i] <= nMax)
                {
                    ns.Add(uN[i]);
                    ms.Add(uM[i]);
                }
            }

            double mAtMax = InterpolateMAtN(uN, uM, nMax);
            if (mAtMax > 0)
            {
                ns.Add(nMax);
                ms.Add(mAtMax);
            }

            ns.Add(nMax);
            ms.Add(0.0);

            return (ns, ms);
        }

        /// <summary>
        /// N-M 曲線上で軸力 nTarget に対応する M を線形補間で算定する。
        /// 単調でない曲線でも複数交点のうち最大の M を返す (耐力包絡線として安全側)。
        /// </summary>
        private static double InterpolateMAtN(List<double> ns, List<double> ms, double nTarget)
        {
            double maxM = 0.0;
            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                if ((n0 - nTarget) * (n1 - nTarget) <= 0 && n0 != n1)
                {
                    double t = (nTarget - n0) / (n1 - n0);
                    double mInterp = ms[i] + t * (ms[i + 1] - ms[i]);
                    maxM = Math.Max(maxM, mInterp);
                }
            }
            return maxM;
        }

        /// <summary>
        /// 現在の断面パラメータに基づいて断面計算オブジェクトを生成します。
        /// </summary>
        /// <returns>断面計算オブジェクト。生成できない場合は null。</returns>
        internal IPileSectionCalculation? CreateSectionCalculator()
        {
            return (PileBodyType, PileSectionType) switch
            {
                // 場所打ちRC杭
                (PileTypeNames.InsituRc, _) =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // 場所打ち鋼管RC杭 - RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.RcSection) =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // 場所打ち鋼管RC杭 - 鋼管RC部
                // 終局ひずみ 5,000μ オプション時はコンクリートにも同じ εcu を渡す。
                // 断面側 (UltimateCompressiveStrain) だけ広げても、材料側の有効範囲が 0.003 のままだと
                // ε>0.003 で σ=0 に脱落して終局曲げが静かに小さくなるため、必ずセットで渡すこと。
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) =>
                    new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth),
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc,
                            epsilonCu: ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                                ? SectionDesignConstants.KCTB_ULTIMATE_COMPRESSIVE_STRAIN
                                : SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // PHC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Phc) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PHC節杭
                // カタログの断面性能はすべて軸部の中空円形断面基準なので、断面耐力の計算は
                // PHC杭 と完全に同一。専用クラスは作らず PHCSection をそのまま使う。
                // (節部径 NodeDiameter は断面耐力には一切効かない)
                (PileTypeNames.PrecastConcrete, PileTypeNames.PhcNodular) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC節杭 (PHC部)。異形棒鋼を持たない区間なので PHC杭 と同一。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodularPhcPart) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // BF.S (頭部厚型節付き杭)。PC 鋼棒のみの中空断面なので PHC杭 と同一。
                // 先端軸部はカタログに耐力の記載が無く、ここで計算した値がそのまま設計値になる。
                (PileTypeNames.PrecastConcrete, PileTypeNames.BfsHead or PileTypeNames.BfsTip)
                        when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC節杭 (PRC部)。断面性能は軸部基準なので PRC杭 と同一。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodular) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PRCSection(
                        new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Prc) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PRCSection(
                        new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // SC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Sc) when PileDiameter != 2 * ConcreteThickness =>
                    new SCSection(
                        new PrecastSCConcrete(PileDiameter - 2 * PipeTs, PileDiameter - 2 * PipeTs - 2 * ConcreteThickness, ConcreteFc),
                        new PrecastSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth)),

                // 鋼管杭 - コンクリート充填鋼管部 (杭頭部、鉄筋定着工法用)
                // 鋼管 + 充填コンクリートの SPRC 断面として扱う。鉄筋は配置可だが
                // M-φ の耐力には参入しない方針のため MainBarNum=0 で渡す。
                // (杭頭ノード固定の RC 円形 NM 計算は PileTop 側で 2 段断面として別途実施)
                (PileTypeNames.SteelPipe, PileTypeNames.CftSection) =>
                    new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth),
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc),
                        new MainBars(MainBarDr, 0, MainBarSpec, MainBarSize),
                        // 鋼管杭は鋼管 1.1F 完全バイリニア型オプションの対象外
                        isInsituSteelPipeConcretePile: false),

                // 鋼管杭 - 鋼管部 / 旧サブ名 (純粋な鋼管区間 → 既存挙動に合わせ M-φ は null)
                (PileTypeNames.SteelPipe, _) => null,

                _ => null
            };
        }
        public static void DebugDumpProperties()
        {
        }
    }
}
