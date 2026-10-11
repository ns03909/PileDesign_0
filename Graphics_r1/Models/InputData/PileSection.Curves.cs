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
    // PileSection のうち、N-M・N-Q 曲線とそのキャッシュ、軸力制限値、せん断の内訳。
    public partial class PileSection
    {
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
                DropCachesIfMaterialOptionsChanged();
                cache ??= compute();
                return cache.Value;
            }
        }

        /// <summary>キャッシュを作ったときの材料モデル化オプションの版数。</summary>
        private int _cacheOptionsVersion = -1;

        /// <summary>
        /// 材料のモデル化オプションが変わっていたら、この断面のキャッシュを捨てる。
        /// <b>呼び出し側が <see cref="_curveCacheLock"/> を持っていること。</b>
        ///
        /// オプションは静的 (<see cref="ConcreteModelOptions"/>) なので、変えると
        /// <b>生きているすべての断面</b>の曲線が変わる。以前は入力モデルをたどって
        /// キャッシュを捨てて回っていたが、ダイアログが編集している<b>複製</b>の断面は
        /// その走査から漏れる。実際、杭断面ウィンドウでオプションを変えても
        /// N-M・N-Q が変わらないという不具合が出た (杭体ウィンドウは PileBodies の複製を編集し、
        /// 断面ウィンドウはその複製側の断面を受け取るため)。
        ///
        /// 捨てて回るのをやめ、<b>使うときに自分で古さを判定する</b>。
        /// これなら断面がどのモデルにぶら下がっていても取り違えない。
        /// M-φ の静的キャッシュは同じ理由でキーに署名を含めている。
        /// </summary>
        private void DropCachesIfMaterialOptionsChanged()
        {
            int version = ConcreteModelOptions.Version;
            if (_cacheOptionsVersion == version) return;

            InvalidateAllCaches();
            _cacheOptionsVersion = version;
        }

        /// <summary>
        /// 断面パラメータ変更時にすべてのキャッシュを一括で無効化します。
        /// </summary>
        /// <summary>
        /// この断面の NM / 降伏 / ひび割れキャッシュを無効化します。
        ///
        /// 材料のモデル化オプション (<see cref="ConcreteModelOptions"/>) の変更では<b>呼ぶ必要がありません</b>。
        /// 断面が曲線を使うときに版数を見て自分で捨てます
        /// (<c>DropCachesIfMaterialOptionsChanged</c>)。たどって捨てて回る方式は、
        /// ダイアログが編集している複製の断面に届かないためやめました。
        /// 断面の値そのものが変わったとき (Ec の再計算など) に使います。
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

        // ここから下の軸力制限値は、断面計算オブジェクトから転送される派生値。
        //
        // [JsonIgnore] 必須: セッターが private なので、保存はされても読み戻されない。
        // とくに List のほうは、保存時に他と同じ実体を指して "$ref" で書かれるのに、
        // 読込では別の空リストになるため参照の共有まで壊れる。値は GetNMRaw() の
        // 転送で入れ直されるので、保存する意味がない。
        // (SaveLoadFirstCycleTests が 1 周目の保存→読込でこれを見張る)

        // 新: バッキングフィールド＋通知
        private List<double> _ultimateLimitAxialForceThresholds = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public List<double> UltimateLimitAxialForceThresholds
        {
            get => _ultimateLimitAxialForceThresholds;
            private set => SetProperty(ref _ultimateLimitAxialForceThresholds, value);
        }

        private List<double> _damageLimitAxialForceThresholds = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public List<double> DamageLimitAxialForceThresholds
        {
            get => _damageLimitAxialForceThresholds;
            private set => SetProperty(ref _damageLimitAxialForceThresholds, value);
        }

        // 使用限界軸力制限値（kN単位、PrecastPileSectionから転送）
        private double _serviceLimitNMin;
        [System.Text.Json.Serialization.JsonIgnore]
        public double ServiceLimitNMin
        {
            get => _serviceLimitNMin;
            private set => SetProperty(ref _serviceLimitNMin, value);
        }

        private double _serviceLimitNMax;
        [System.Text.Json.Serialization.JsonIgnore]
        public double ServiceLimitNMax
        {
            get => _serviceLimitNMax;
            private set => SetProperty(ref _serviceLimitNMax, value);
        }

        // せん断の軸力制限値（PHC杭用、N単位 → GetNMRawで転送）
        private double _shearNMinService, _shearNMaxService;
        private double _shearNMinDamage, _shearNMaxDamage;
        private double _shearNMinUltimate, _shearNMaxUltimate;
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShearNMinService { get => _shearNMinService; private set => SetProperty(ref _shearNMinService, value); }
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShearNMaxService { get => _shearNMaxService; private set => SetProperty(ref _shearNMaxService, value); }
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShearNMinDamage { get => _shearNMinDamage; private set => SetProperty(ref _shearNMinDamage, value); }
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShearNMaxDamage { get => _shearNMaxDamage; private set => SetProperty(ref _shearNMaxDamage, value); }
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShearNMinUltimate { get => _shearNMinUltimate; private set => SetProperty(ref _shearNMinUltimate, value); }
        [System.Text.Json.Serialization.JsonIgnore]
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
        {
            get
            {
                lock (_curveCacheLock)
                {
                    DropCachesIfMaterialOptionsChanged();
                    return _steelYieldNMRawCache ??= ComputeSteelYieldNMRaw();
                }
            }
        }

        // スケーリング後（kN, kNm）
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) SteelYieldNM => (
            GetMultipliedListValues(SteelYieldNMRaw.N, 1e-3),
            GetMultipliedListValues(SteelYieldNMRaw.M, 1e-6)
        );

        // ひび割れNM（Raw: N[N], M[Nmm]）をキャッシュ付きで返す
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) CrackNMRaw
        {
            get
            {
                lock (_curveCacheLock)
                {
                    DropCachesIfMaterialOptionsChanged();
                    return _crackNMRawCache ??= ComputeCrackNMRaw();
                }
            }
        }

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
        /// <param name="Mode">"斜めひび割れ" / "縦ひび割れ"</param>
        public sealed record ShearComponentCurve(
            string LimitState, string Mode, List<double> N, List<double> Q);

        /// <summary>
        /// せん断耐力の内訳（斜めひび割れ / 縦ひび割れ）の QN 曲線を返す（kN 単位、β 低減前）。
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

            // 内訳も軸力ごとに評価する。σ0e = N/Ae が σG に入るので、本線と同じく N で変わる。
            void Add(string limitState,
                     Func<double, (double DiagonalTension, double Web)> components,
                     double nMin, double nMax)
            {
                var nsD = new List<double>(iCount);
                var qsD = new List<double>(iCount);
                var nsW = new List<double>(iCount);
                var qsW = new List<double>(iCount);
                for (int i = 0; i < iCount; i++)
                {
                    double n = (nMin * (iCount - i) + nMax * i) / iCount;
                    double sigma0E = precast.Ae > 0.0 ? n / precast.Ae : 0.0;
                    var c = components(sigma0E);
                    if (double.IsFinite(c.DiagonalTension) && c.DiagonalTension > 0.0)
                    {
                        nsD.Add(n * 1e-3);
                        qsD.Add(c.DiagonalTension * 1e-3);
                    }
                    if (double.IsFinite(c.Web) && c.Web > 0.0)
                    {
                        nsW.Add(n * 1e-3);
                        qsW.Add(c.Web * 1e-3);
                    }
                }
                if (nsD.Count > 0)
                    result.Add(new ShearComponentCurve(limitState, "斜めひび割れ", nsD, qsD));
                if (nsW.Count > 0)
                    result.Add(new ShearComponentCurve(limitState, "縦ひび割れ", nsW, qsW));
            }

            Add("使用限界", s => precast.GetServiceLimitShearComponents(monQd, s),
                precast.ShearNMinService, precast.ShearNMaxService);
            Add("損傷限界", s => precast.GetDamageLimitShearComponents(monQd, s),
                precast.ShearNMinDamage, precast.ShearNMaxDamage);
            Add("安全限界", s => precast.GetUltimateLimitShearComponents(monQd, s),
                precast.ShearNMinUltimate, precast.ShearNMaxUltimate);
            return result;
        }

        /// <summary>
        /// N-M / Q-N 曲線から、指定軸力に対応する限界値を線形補間で求める。
        ///
        /// <b>限界曲線は軸力について 1 価とは限らない。</b>圧縮側と引張側で 2 度、
        /// 軸力制限を入れた曲線では境界の垂直線でさらに複数回、同じ N を横切る。
        /// 検定に使うのは<b>包絡</b>なので、横切ったすべての区間のうち最大値を採る。
        ///
        /// 範囲外の軸力では NaN を返す。端の値へ丸めると、軸力制限の外にある断面に
        /// 「端の耐力がある」と読める線を引いてしまう
        /// (計算書の限界線がそうなっていた。検定は 0 を返して項目を作らない扱いだった)。
        /// </summary>
        public static double InterpolateLimitAtAxialForce(
            List<double>? ns, List<double>? values, double targetN)
            => LocateLimitAtAxialForce(ns, values, targetN).Value;

        /// <summary>
        /// <see cref="InterpolateLimitAtAxialForce"/> と同じ値に加え、採った曲線の区間 (両端の軸力と値) を返す。
        /// 検定の根拠に「曲線のどの 2 点のあいだを補間したか」を書くために使う。範囲外なら値は NaN。
        /// </summary>
        public static (double Value, double N0, double V0, double N1, double V1) LocateLimitAtAxialForce(
            List<double>? ns, List<double>? values, double targetN)
        {
            var none = (double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
            if (ns == null || values == null) return none;
            if (ns.Count < 2 || ns.Count != values.Count) return none;

            var best = none;
            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                if (targetN < Math.Min(n0, n1) || targetN > Math.Max(n0, n1)) continue;

                double v0 = values[i], v1 = values[i + 1];
                double dn = n1 - n0;
                double v = Math.Abs(dn) < 1e-10
                    ? Math.Max(v0, v1)                       // 垂直な区間 (軸力制限の境界)
                    : v0 + (v1 - v0) * (targetN - n0) / dn;

                if (double.IsNaN(best.Item1) || v > best.Item1) best = (v, n0, v0, n1, v1);
            }
            return best;
        }

        /// <summary>
        /// 軸力 <paramref name="axialKN"/> [kN] のときのせん断の限界値を、式と係数の内訳つきで返す (検定の根拠)。
        /// 断面の種類ごとの式 (場所打ち RC・その工法・場所打ち鋼管コンクリート・PHC・PRC・SC) の内訳を返す。作れなければ null。
        /// 断面の計算をやり直すので重い。検定の全行ではなく、計算書に載せる行だけで呼ぶこと。
        /// </summary>
        public ShearLimitBasis? DescribeShearLimit(SectionLimitState limit, int damageLevel, bool isFactored, double monQd, double axialKN)
        {
            try
            {
                return CreateSectionCalculator() is AbstractPileSection calculator
                    ? calculator.DescribeShearLimit(limit, damageLevel, isFactored, monQd, axialKN * 1e3, HoopPw, HoopSigmay)
                    : null;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("せん断の限界値の根拠 (→なし)", ex,
                    $"PileBodyType={PileBodyType}, PileSectionType={PileSectionType}");
                return null;
            }
        }

        /// <summary>
        /// 検定・グラフ・計算書が共通で使う M/(Q·d)。
        /// せん断耐力は M/(Q·d) に依存する (場所打ちRC の (M/Qd + 1.7)、既製杭の α)。
        /// 画面のグラフだけは利用者が値を変えられるが、既定はこの値。
        /// </summary>
        public const double DefaultMonQd = 3.0;

        /// <summary>
        /// <b>荷重レベルに応じた Q-N 曲線</b>を返す。限界曲線を引く経路はすべてここを通すこと。
        ///
        /// 損傷限界はレベルで低減係数が変わる (レベル1 は β2 を乗じない、レベル2 は β1×β2)。
        /// キャッシュ済みの <see cref="FactoredDamageNQ"/> 等はレベルを持たないため、
        /// それを直接読むと<b>画面・計算書・検定で違う曲線</b>になる (実際にそうなっていた)。
        ///
        /// 鋼管杭のように <see cref="ComputeQNForMonQd"/> が曲線を返せない断面では、
        /// キャッシュ済みプロパティに落とす (鋼管杭の損傷限界はレベルに依存しない)。
        /// </summary>
        public (
            (List<double> N, List<double> Q) UnfactoredService,
            (List<double> N, List<double> Q) FactoredService,
            (List<double> N, List<double> Q) UnfactoredDamage,
            (List<double> N, List<double> Q) FactoredDamage,
            (List<double> N, List<double> Q) UnfactoredUltimate,
            (List<double> N, List<double> Q) FactoredUltimate
        ) GetQNCurvesForLevel(int damageLevel, double monQd = DefaultMonQd)
        {
            var curves = ComputeQNForMonQd(monQd, damageLevel);
            if (curves.UnfactoredService.N != null) return curves;

            return (
                UnfactoredServiceNQ, FactoredServiceNQ,
                UnfactoredDamageNQ, FactoredDamageNQ,
                UnfactoredUltimateNQ, FactoredUltimateNQ
            );
        }

        /// <summary>
        /// 限界状態の N-M 曲線 (N [kN], M [kN·m]) を 1 本選ぶ。<b>検定・画面のグラフ・計算書の図はすべてここを通す。</b>
        /// 限界状態と損傷限界のレベルは <see cref="Services.LimitStateRule"/> で決める。
        ///
        /// 以前は同じ選び方が検定 (列挙型で)・画面 (「低減後損傷限界状態」などの文字列で)・計算書 (それぞれの図で) の
        /// 3 系統に書き写されていて、損傷限界のレベルの取り方が写しごとに違った。
        /// </summary>
        /// <param name="damageLevel">損傷限界の曲線のレベル (1: β2 を乗じない / 2: β1×β2)。損傷限界の低減後にだけ効く。</param>
        public (List<double> N, List<double> M) GetLimitNMCurve(bool factored, SectionLimitState limit, int damageLevel)
            => (factored, limit) switch
            {
                (true, SectionLimitState.Damage) => GetFactoredDamageNM(damageLevel),
                (true, SectionLimitState.Service) => FactoredServiceNM,
                (true, _) => FactoredUltimateNM,
                (false, SectionLimitState.Damage) => UnfactoredDamageNM,
                (false, SectionLimitState.Service) => UnfactoredServiceNM,
                (false, _) => UnfactoredUltimateNM,
            };

        /// <summary>
        /// 限界状態の Q-N 曲線 (N [kN], Q [kN]) を 1 本選ぶ。<see cref="GetLimitNMCurve"/> と対。
        /// 曲線は <see cref="GetQNCurvesForLevel"/> から取る (損傷限界のレベルと M/(Q·d) が反映される)。
        /// </summary>
        public (List<double> N, List<double> Q) GetLimitNQCurve(bool factored, SectionLimitState limit, int damageLevel, double monQd = DefaultMonQd)
        {
            var curves = GetQNCurvesForLevel(damageLevel, monQd);
            return (factored, limit) switch
            {
                (false, SectionLimitState.Service) => curves.UnfactoredService,
                (false, SectionLimitState.Damage) => curves.UnfactoredDamage,
                (false, _) => curves.UnfactoredUltimate,
                (true, SectionLimitState.Service) => curves.FactoredService,
                (true, SectionLimitState.Damage) => curves.FactoredDamage,
                (true, _) => curves.FactoredUltimate,
            };
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

    }
}
