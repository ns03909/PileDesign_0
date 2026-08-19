using PileDesign.ViewModels;
using System;
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

            copy.LoadCombinationFactor = this.LoadCombinationFactor;
            copy.LoadCombinations = [.. this.LoadCombinations.Select(combination => combination.DeepCopy())];
            copy.LoadCombinationsPlus = [.. this.LoadCombinationsPlus.Select(combination => combination.DeepCopy())];
            copy.LoadCaseLevel1Common = this.LoadCaseLevel1Common.DeepCopy();
            copy.LoadCaseLevel2Common = this.LoadCaseLevel2Common.DeepCopy();
            copy.LoadCasesLevel1 = [.. this.LoadCasesLevel1.Select(loadCase => loadCase.DeepCopy())];
            copy.LoadCasesLevel2 = [.. this.LoadCasesLevel2.Select(loadCase => loadCase.DeepCopy())];
            return copy;
        }
    }
}
