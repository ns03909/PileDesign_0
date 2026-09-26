using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    public class LoadCasesInput : BaseModel
    {
        // フィールド

        private MainWindowViewModel _mainWindowViewModel;

        private double _loadCombinationFactor;
        public double LoadCombinationFactor
        {
            get => _loadCombinationFactor;
            set => SetProperty(ref _loadCombinationFactor, value);
        }

        private ObservableCollection<LoadCombination> _loadCombinations;
        public ObservableCollection<LoadCombination> LoadCombinations
        {
            get => _loadCombinations;
            set
            {
                if (SetProperty(ref _loadCombinations, value))
                {
                    _loadCombinations.CollectionChanged += (s, e) => OnPropertyChanged(nameof(LoadCombinations));
                }
            }
        }

        //画面表示用4つのデータをもつ
        private ObservableCollection<LoadCombination> _loadCombinationsPlus;
        public ObservableCollection<LoadCombination> LoadCombinationsPlus
        {
            get => _loadCombinationsPlus;
            set
            {
                if (SetProperty(ref _loadCombinationsPlus, value))
                {
                    _loadCombinationsPlus.CollectionChanged += (s, e) => OnPropertyChanged(nameof(LoadCombinationsPlus));
                }
            }
        }

        private LoadCase _loadCaseVL0;
        public LoadCase LoadCaseVL0
        {
            get => _loadCaseVL0;
            set => SetProperty(ref _loadCaseVL0, value);
        }

        private LoadCase _loadCaseVLadd;
        public LoadCase LoadCaseVLadd
        {
            get => _loadCaseVLadd;
            set => SetProperty(ref _loadCaseVLadd, value);
        }

        private LoadCase _loadCaseVL;
        public LoadCase LoadCaseVL
        {
            get => _loadCaseVL;
            set => SetProperty(ref _loadCaseVL, value);
        }

        private LoadCaseCommon _loadCaseLevel1Common;
        public LoadCaseCommon LoadCaseLevel1Common
        {
            get => _loadCaseLevel1Common;
            set => SetProperty(ref _loadCaseLevel1Common, value);
        }

        private LoadCaseCommon _loadCaseLevel2Common;
        public LoadCaseCommon LoadCaseLevel2Common
        {
            get => _loadCaseLevel2Common;
            set => SetProperty(ref _loadCaseLevel2Common, value);
        }

        private ObservableCollection<LoadCase> _loadCasesLevel1;
        public ObservableCollection<LoadCase> LoadCasesLevel1
        {
            get => _loadCasesLevel1;
            set
            {
                // 古いコレクションのイベント解除
                UnsubscribeLoadCaseEvents(_loadCasesLevel1);

                if (SetProperty(ref _loadCasesLevel1, value))
                {
                    // 新しいコレクションのイベント購読
                    SubscribeLoadCaseEvents(_loadCasesLevel1);
                    RaiseAllLoadCasesChanged();
                }
            }
        }

        private ObservableCollection<LoadCase> _loadCasesLevel2;
        public ObservableCollection<LoadCase> LoadCasesLevel2
        {
            get => _loadCasesLevel2;
            set
            {
                // 古いコレクションのイベント解除
                UnsubscribeLoadCaseEvents(_loadCasesLevel2);

                if (SetProperty(ref _loadCasesLevel2, value))
                {
                    // 新しいコレクションのイベント購読
                    SubscribeLoadCaseEvents(_loadCasesLevel2);
                    RaiseAllLoadCasesChanged();
                }
            }
        }

        // LoadCase の IsApplicable 変更を監視するためのヘルパーメソッド
        private void SubscribeLoadCaseEvents(ObservableCollection<LoadCase> collection)
        {
            if (collection == null) return;
            foreach (var lc in collection)
            {
                lc.PropertyChanged += LoadCase_PropertyChanged;
            }
            collection.CollectionChanged += LoadCasesCollection_Changed;
        }

        private void UnsubscribeLoadCaseEvents(ObservableCollection<LoadCase> collection)
        {
            if (collection == null) return;
            foreach (var lc in collection)
            {
                lc.PropertyChanged -= LoadCase_PropertyChanged;
            }
            collection.CollectionChanged -= LoadCasesCollection_Changed;
        }

        private void LoadCasesCollection_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 削除されたアイテムのイベント解除
            if (e.OldItems != null)
            {
                foreach (LoadCase lc in e.OldItems)
                {
                    lc.PropertyChanged -= LoadCase_PropertyChanged;
                }
            }
            // 追加されたアイテムのイベント購読
            if (e.NewItems != null)
            {
                foreach (LoadCase lc in e.NewItems)
                {
                    lc.PropertyChanged += LoadCase_PropertyChanged;
                }
            }
            RaiseAllLoadCasesChanged();
        }

        private void LoadCase_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.IsApplicable))
            {
                RaiseAllLoadCasesChanged();
            }
            if (e.PropertyName == nameof(LoadCase.IsAnalysisTarget))
            {
                OnPropertyChanged(nameof(AnalysisTargetSeismicLoadCases));
            }
        }

        private void RaiseAllLoadCasesChanged()
        {
            OnPropertyChanged(nameof(AllSeismicLoadCases));
            OnPropertyChanged(nameof(AllLoadCases));
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<LoadCase> AllSeismicLoadCases
        {
            get
            {
                var allSeismicLoadCases = new ObservableCollection<LoadCase>();
                //if (LoadCasesLevel1 != null)
                //    foreach (var lc in LoadCasesLevel1) allSeismicLoadCases.Add(lc);
                //if (LoadCasesLevel2 != null)
                //    foreach (var lc in LoadCasesLevel2) allSeismicLoadCases.Add(lc);
                if (LoadCasesLevel1 != null)
                    foreach (var lc in LoadCasesLevel1.Where(x => x.IsApplicable))
                        allSeismicLoadCases.Add(lc);
                if (LoadCasesLevel2 != null)
                    foreach (var lc in LoadCasesLevel2.Where(x => x.IsApplicable))
                        allSeismicLoadCases.Add(lc);
                return allSeismicLoadCases;
            }
        }

        /// <summary>
        /// 保持している荷重ケースを<b>絞らずに</b>すべて返す。配線 (親の固定・VM の再セット) 用。
        ///
        /// <see cref="AllLoadCases"/> は IsApplicable で絞るので配線には使えない。
        /// 絞ると、いま適用外の荷重ケースだけ配線されず、あとで適用に切り替えたときに
        /// 親を持たないまま残る。
        /// </summary>
        /// <summary>
        /// レベル 1・2 の荷重ケースの番号を、一覧の並び順 (1 始まり) に揃える。振り直したケースの説明を返す (無ければ空)。
        ///
        /// 杭の地震時軸力は、荷重ケースの<b>並び順</b>で対応させている所 (杭配置の表・計算書の杭配置図) と、
        /// <b>番号</b>で引いている所 (<see cref="PileLayoutDataItem.GetSeismicAxialForce"/>: 解析・グラフ・ΣV) がある。
        /// 番号は画面では変えられず、並び順どおりに振られるので、通常は食い違わない。
        /// 手で編集したファイルなどで番号が重複したり並び順と合わなかったりすると、解析が<b>別のケースの軸力</b>を
        /// 黙って使い、MGT 出力では同じ番号のケースが欠けた。画面に見えている並び順を正として揃える。
        /// 荷重組合せの番号も同じく揃える (結果・検定は組合せを番号で見分ける)。
        /// </summary>
        internal IReadOnlyList<string> NormalizeLoadCaseNumbers()
        {
            var changes = new List<string>();
            Normalize(LoadCasesLevel1, 1);
            Normalize(LoadCasesLevel2, 2);

            // 荷重組合せも番号で見分ける (LoadCombination.IsSameCombination)。画面では係数から番号どおりに作られるので、
            // 食い違うのは手で編集したファイルなど
            if (LoadCombinations != null)
            {
                for (int i = 0; i < LoadCombinations.Count; i++)
                {
                    var comb = LoadCombinations[i];
                    if (comb == null || comb.No == i + 1) continue;
                    changes.Add($"荷重組合せ「{comb.Name}」: 番号 {comb.No} → {i + 1}");
                    comb.No = i + 1;
                }
            }
            return changes;

            void Normalize(IList<LoadCase>? cases, int level)
            {
                if (cases == null) return;
                for (int i = 0; i < cases.Count; i++)
                {
                    var lc = cases[i];
                    if (lc == null || lc.No == i + 1) continue;
                    changes.Add($"レベル{level}「{lc.LoadName}」: 番号 {lc.No} → {i + 1}");
                    lc.No = i + 1;
                }
            }
        }

        /// <summary>
        /// 地震時の荷重ケース名が空欄・重複していないかを調べる (変えない)。問題のある荷重ケースの説明を返す。
        ///
        /// 画面の荷重ケースの選択 (グラフ・表・メイン画面) は名前で行っているので、名前が空欄や重複だと
        /// レベル1 とレベル2 を選び分けられず、別のケースの結果が出る。鉛直荷重のケース名 (VL0・VLadd・VL) と
        /// 同じ名前も重複とみなす (解析は名前「VL」で鉛直荷重のケースを見分けている)。
        /// </summary>
        internal IReadOnlyList<string> DescribeInvalidLoadCaseNames() => CheckLoadCaseNames(fix: false);

        /// <summary>
        /// 空欄・重複した荷重ケース名を、重ならない名前に付け直す。付け直したケースの説明を返す (無ければ空)。
        /// 空欄は「L{レベル}-{番号}」、重複は「元の名前 (L{レベル}-{番号})」にする。読込・計算例の読込で呼ぶ
        /// (同梱の計算例は荷重ケース名がすべて空欄)。
        /// </summary>
        internal IReadOnlyList<string> NormalizeLoadCaseNames() => CheckLoadCaseNames(fix: true);

        private List<string> CheckLoadCaseNames(bool fix)
        {
            var messages = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vl in new[] { LoadCaseVL0, LoadCaseVLadd, LoadCaseVL })
                if (!string.IsNullOrWhiteSpace(vl?.LoadName)) used.Add(vl!.LoadName.Trim());

            Check(LoadCasesLevel1, 1);
            Check(LoadCasesLevel2, 2);
            return messages;

            void Check(IList<LoadCase>? cases, int level)
            {
                if (cases == null) return;
                foreach (var lc in cases)
                {
                    if (lc == null) continue;
                    string name = lc.LoadName?.Trim() ?? "";
                    if (name.Length > 0 && used.Add(name)) continue;

                    if (!fix)
                    {
                        messages.Add(name.Length == 0
                            ? $"レベル{level} の {lc.No} 番目: 荷重ケース名が空欄です"
                            : $"レベル{level} の {lc.No} 番目: 荷重ケース名「{name}」が他のケースと重複しています");
                        continue;
                    }
                    string code = $"L{level}-{lc.No}";
                    string fresh = name.Length == 0 ? code : $"{name} ({code})";
                    for (int k = 2; !used.Add(fresh); k++) fresh = $"{code}_{k}";
                    messages.Add(name.Length == 0
                        ? $"レベル{level} の {lc.No} 番目: 空欄 → 「{fresh}」"
                        : $"レベル{level} の {lc.No} 番目: 「{name}」(重複) → 「{fresh}」");
                    lc.LoadName = fresh;
                }
            }
        }

        internal IEnumerable<LoadCase> EveryLoadCase
        {
            get
            {
                if (LoadCaseVL0 != null) yield return LoadCaseVL0;
                if (LoadCaseVLadd != null) yield return LoadCaseVLadd;
                if (LoadCaseVL != null) yield return LoadCaseVL;
                foreach (var lc in LoadCasesLevel1 ?? []) if (lc != null) yield return lc;
                foreach (var lc in LoadCasesLevel2 ?? []) if (lc != null) yield return lc;
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<LoadCase> AllLoadCases
        {
            get
            {
                var allLoadCases = new ObservableCollection<LoadCase>();
                if (LoadCaseVL0 != null)
                    allLoadCases.Add(LoadCaseVL0);
                if (LoadCaseVLadd != null)
                    allLoadCases.Add(LoadCaseVLadd);
                if (LoadCaseVL != null)
                    allLoadCases.Add(LoadCaseVL);
                //if (LoadCasesLevel1 != null)
                //    foreach (var lc in LoadCasesLevel1) allLoadCases.Add(lc);
                //if (LoadCasesLevel2 != null)
                //    foreach (var lc in LoadCasesLevel2) allLoadCases.Add(lc);
                if (LoadCasesLevel1 != null)
                    foreach (var lc in LoadCasesLevel1.Where(x => x.IsApplicable))
                        allLoadCases.Add(lc);
                if (LoadCasesLevel2 != null)
                    foreach (var lc in LoadCasesLevel2.Where(x => x.IsApplicable))
                        allLoadCases.Add(lc);
                return allLoadCases;
            }
        }

        /// <summary>
        /// 解析対象の地震荷重ケース（IsAnalysisTarget=true のもの）
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<LoadCase> AnalysisTargetSeismicLoadCases
        {
            get
            {
                var result = new ObservableCollection<LoadCase>();
                if (LoadCasesLevel1 != null)
                    foreach (var lc in LoadCasesLevel1.Where(x => x.IsAnalysisTarget))
                        result.Add(lc);
                if (LoadCasesLevel2 != null)
                    foreach (var lc in LoadCasesLevel2.Where(x => x.IsAnalysisTarget))
                        result.Add(lc);
                return result;
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<LoadCombination> AllLoadCombinations
        {
            get
            {
                var allLoadCombinations = new ObservableCollection<LoadCombination>();
                if (LoadCombinations != null)
                    foreach (var lc in LoadCombinations.Where(x => x.IsApplicable))
                        allLoadCombinations.Add(lc);
                return allLoadCombinations;
            }
        }

        // コンストラクタ
        public LoadCasesInput()
        {

        }

        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
            LoadCombinationFactor = 1;
            LoadCombinations = [new LoadCombination(1, 1.0, 1.0, 1.0)];
            LoadCombinationsPlus = [
                new LoadCombination(1, 1.0, 1.0, 1.0),
                new LoadCombination(1, 1.0, 1.0, 1.0),
                new LoadCombination(1, 1.0, 1.0, 1.0),
                new LoadCombination(1, 1.0, 1.0, 1.0)
                ];
            // 鉛直荷重ケース (VL 系) は水平解析の対象外だが、地盤ばねの扱いは
            // 地震時ケースと同じ既定 (kh 低減 + py 頭打ち) に揃えておく。
            const SoilNonlinearityMode vlSoilMode = SoilNonlinearityMode.KhReductionWithPy;
            LoadCaseVL0 = new LoadCase(_mainWindowViewModel, true, 0, 1, "VL0", 0.0, vlSoilMode, false, 0.0, 0.0, 0.0, 0.0, 0.0);
            LoadCaseVLadd = new LoadCase(_mainWindowViewModel, true, 0, 2, "VLadd", 0.0, vlSoilMode, false, 0.0, 0.0, 0.0, 0.0, 0.0);
            LoadCaseVL = new LoadCase(_mainWindowViewModel, true, 0, 3, "VL", 0.0, vlSoilMode, false, 0.0, 0.0, 0.0, 0.0, 0.0);

            double x = 0;
            double y = 0;
            double z = 1;
            const SoilNonlinearityMode soilMode = SoilNonlinearityMode.KhReductionWithPy;
            bool isPileNonLinear1 = false;
            bool isPileNonLinear2 = true;
            double upperMassForce1 = 1000;
            double foundationMassForce1 = 800;
            double upperMassForce2 = 2000;
            double foundationMassForce2 = 1600;

            LoadCaseLevel1Common = new LoadCaseCommon(soilMode, isPileNonLinear1,
                upperMassForce1, foundationMassForce1, x, y, z);

            LoadCasesLevel1 = [
            new LoadCase
                (_mainWindowViewModel, true, 1, 1, "VL+E1", 0.0, soilMode, isPileNonLinear1,
                upperMassForce1, foundationMassForce1, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 1, 2, "VL+E2", 90.0, soilMode, isPileNonLinear1,
                upperMassForce1, foundationMassForce1, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 1, 3, "VL-E1", 180.0, soilMode, isPileNonLinear1,
                upperMassForce1, foundationMassForce1, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 1, 4, "VL-E2", 270.0, soilMode, isPileNonLinear1,
                upperMassForce1, foundationMassForce1,  x, y, z),
            ];

            LoadCaseLevel2Common = new LoadCaseCommon(soilMode, isPileNonLinear2,
                upperMassForce2, foundationMassForce2, x, y, z);

            LoadCasesLevel2 = [
            new LoadCase
                (_mainWindowViewModel, true, 2, 1, "U1", 0.0, soilMode, isPileNonLinear2,
                upperMassForce2, foundationMassForce2, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 2, 2, "U2", 90.0, soilMode, isPileNonLinear2,
                upperMassForce2, foundationMassForce2, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 2, 3, "U5", 180.0, soilMode, isPileNonLinear2,
                upperMassForce2, foundationMassForce2, x, y, z),
            new LoadCase
                (_mainWindowViewModel, true, 2, 4, "U6", 270.0, soilMode, isPileNonLinear2,
                upperMassForce2, foundationMassForce2, x, y, z),
            ];

            // 解析対象のデフォルト: ケース2-1 (U1) のみ
            LoadCasesLevel2[0].IsAnalysisTarget = true;
        }

        // 深いコピーを作成するメソッド
        public LoadCasesInput DeepCopy()
        {
            var copy = (LoadCasesInput)this.MemberwiseClone();

            // null のコレクションは null のまま写す。
            //
            // 以前は無条件に辿っていたため、いずれかが未設定だと複製が例外になった。
            // 保存が複製を書くようになってからは、そこで落ちると保護が黙って効かなくなる。
            // 空のリストに置き換えると保存ファイルの中身が変わるので、形は保つこと。
            copy.LoadCombinationFactor = this.LoadCombinationFactor;
            copy.LoadCombinations = this.LoadCombinations == null
                ? null! : [.. this.LoadCombinations.Select(combination => combination.DeepCopy())];
            copy.LoadCombinationsPlus = this.LoadCombinationsPlus == null
                ? null! : [.. this.LoadCombinationsPlus.Select(combination => combination.DeepCopy())];
            copy.LoadCaseLevel1Common = this.LoadCaseLevel1Common?.DeepCopy()!;
            copy.LoadCaseLevel2Common = this.LoadCaseLevel2Common?.DeepCopy()!;
            copy.LoadCasesLevel1 = this.LoadCasesLevel1 == null
                ? null! : [.. this.LoadCasesLevel1.Select(loadCase => loadCase.DeepCopy())];
            copy.LoadCasesLevel2 = this.LoadCasesLevel2 == null
                ? null! : [.. this.LoadCasesLevel2.Select(loadCase => loadCase.DeepCopy())];
            // 鉛直荷重ケース (常時・付加・合計)。地震時ケースだけ写して、この 3 つが
            // 抜けていた。元と同じ実体を指すので、鉛直荷重の編集が Ctrl+Z で戻らない。
            copy.LoadCaseVL0 = this.LoadCaseVL0?.DeepCopy()!;
            copy.LoadCaseVLadd = this.LoadCaseVLadd?.DeepCopy()!;
            copy.LoadCaseVL = this.LoadCaseVL?.DeepCopy()!;
            return copy;
        }
    }
}
