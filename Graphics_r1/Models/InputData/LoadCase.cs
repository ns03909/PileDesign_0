using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class LoadCase : BaseModel
    {
        // MainWindowViewModelはデシリアライズ後にセットする
        private MainWindowViewModel? _mainWindowViewModel;
        // [JsonIgnore]: InputModel への back-reference は循環参照を生むため JSON シリアライズ対象外
        // (InputModel → LoadCasesInput → LoadCases[] → LoadCase → InputModel の循環)
        [JsonIgnore]
        public InputModel? InputModel => _mainWindowViewModel?.CurrentInputModel;

        // プロパティ
        private bool _isApplicable;
        /// <summary>荷重ケースの適用状態（表示・出力用）</summary>
        public bool IsApplicable
        {
            get => _isApplicable;
            set
            {
                if (_isApplicable != value)
                {
                    _isApplicable = value;
                    OnPropertyChanged(nameof(IsApplicable));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        private bool _isAnalyzed = true;
        /// <summary>水平解析が実施済みかどうか（DocxOutputWindow表示時にセット）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set
            {
                if (_isAnalyzed != value)
                {
                    _isAnalyzed = value;
                    OnPropertyChanged(nameof(IsAnalyzed));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        /// <summary>
        /// DocxOutputWindow の CheckBox 表示専用プロパティ。
        /// 「未解析時は IsApplicable=true でも未チェック表示」という UX を実現するため、
        /// getter で IsAnalyzed を AND する。実体 (_isApplicable) は変更しないので
        /// CanExecuteAnalysis (F9 ボタン) など解析選択ロジックには影響しない。
        /// setter は IsAnalyzed=true の時だけ IsApplicable に反映 (灰色時はクリック自体できないが念のため)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsApplicableForDocxDisplay
        {
            get => _isApplicable && _isAnalyzed;
            set
            {
                if (_isAnalyzed && _isApplicable != value)
                {
                    _isApplicable = value;
                    OnPropertyChanged(nameof(IsApplicable));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        private bool _isAnalysisTarget;
        /// <summary>解析対象フラグ（解析ループで使用。IsApplicable とは独立）</summary>
        public bool IsAnalysisTarget
        {
            get => _isAnalysisTarget;
            set
            {
                if (_isAnalysisTarget != value)
                {
                    _isAnalysisTarget = value;
                    OnPropertyChanged(nameof(IsAnalysisTarget));
                }
            }
        }

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private int _level;
        public int Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        private string _loadName = string.Empty;
        public string LoadName
        {
            get => _loadName;
            set => SetProperty(ref _loadName, value);
        }

        private double _loadAngle;
        public double LoadAngle
        {
            get => _loadAngle;
            set => SetProperty(ref _loadAngle, value);
        }

        private SoilNonlinearityMode _soilNonlinearityMode = SoilNonlinearityMode.KhReductionWithPy;
        /// <summary>
        /// 水平地盤ばね (p-y) の非線形性の考慮段階。
        /// 旧 <c>IsSoilNonLinear</c> (bool) を 3 段階に拡張したもの。
        /// </summary>
        public SoilNonlinearityMode SoilNonlinearityMode
        {
            get => _soilNonlinearityMode;
            set
            {
                if (SetProperty(ref _soilNonlinearityMode, value))
                    OnPropertyChanged(nameof(IsSoilNonLinear));
            }
        }

        /// <summary>
        /// 旧 API 互換: 地盤非線形を考慮するか (= <see cref="SoilNonlinearityMode"/> が Linear 以外)。
        /// setter は true → kh 低減 + py 頭打ち、false → 線形 にマップする。
        /// 保存 JSON には出さない (旧ファイル読込専用のシムは <see cref="LegacyIsSoilNonLinear"/>)。
        /// </summary>
        [JsonIgnore]
        public bool IsSoilNonLinear
        {
            get => _soilNonlinearityMode.IsNonLinear();
            set => SoilNonlinearityMode = value
                ? SoilNonlinearityMode.KhReductionWithPy
                : SoilNonlinearityMode.Linear;
        }

        /// <summary>
        /// 旧 JSON (地盤非線形が bool だった版) 読込用シム。
        /// getter は常に null を返すため、新しい保存ファイルには書き出されない
        /// (新ファイルは <see cref="SoilNonlinearityMode"/> のみを持つ)。
        /// </summary>
        [JsonPropertyName("IsSoilNonLinear")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyIsSoilNonLinear
        {
            get => null;
            set { if (value.HasValue) IsSoilNonLinear = value.Value; }
        }

        private bool _isPileNonLinear;
        public bool IsPileNonLinear
        {
            get => _isPileNonLinear;
            set => SetProperty(ref _isPileNonLinear, value);
        }

        private double _upperMassForce;
        public double UpperMassForce
        {
            get => _upperMassForce;
            set
            {
                if (_upperMassForce != value)
                {
                    _upperMassForce = value;
                    OnPropertyChanged(nameof(UpperMassForce));
                }
            }
        }

        private double _foundationMassForce;
        public double FoundationMassForce
        {
            get => _foundationMassForce;
            set
            {
                if (_foundationMassForce != value)
                {
                    _foundationMassForce = value;
                    OnPropertyChanged(nameof(FoundationMassForce));
                }
            }
        }

        private double _forceActionPointX;
        public double ForceActionPointX
        {
            get => _forceActionPointX;
            set => SetProperty(ref _forceActionPointX, value);
        }

        private double _forceActionPointY;
        public double ForceActionPointY
        {
            get => _forceActionPointY;
            set => SetProperty(ref _forceActionPointY, value);
        }

        private double _forceActionPointAltitude;
        public double ForceActionPointAltitude
        {
            get => _forceActionPointAltitude;
            set => SetProperty(ref _forceActionPointAltitude, value);
        }


        public Point3D ForceActionPoint
        {
            get => new(ForceActionPointX, ForceActionPointY, ForceActionPointAltitude);
            set
            {
                ForceActionPointX = value.X;
                ForceActionPointY = value.Y;
                ForceActionPointAltitude = value.Z;
                OnPropertyChanged();
            }
        }

        // デフォルトコンストラクタ（デシリアライズ用）
        public LoadCase() { }

        // MainWindowViewModel付きコンストラクタ（アプリ内生成用）
        public LoadCase(
            MainWindowViewModel mainWindowViewModel,
            bool isApplicable, int level, int no, string loadName, double loadAngle,
            SoilNonlinearityMode soilNonlinearityMode, bool isPileNonLinear,
            double upperMassForce, double foundationMassForce,
            double forceActionPointX, double forceActionPointY, double forceActionPointAltitude)
        {
            _mainWindowViewModel = mainWindowViewModel;
            IsApplicable = isApplicable;
            Level = level;
            No = no;
            LoadName = loadName;
            LoadAngle = loadAngle;
            SoilNonlinearityMode = soilNonlinearityMode;
            IsPileNonLinear = isPileNonLinear;
            UpperMassForce = upperMassForce;
            FoundationMassForce = foundationMassForce;
            ForceActionPointX = forceActionPointX;
            ForceActionPointY = forceActionPointY;
            ForceActionPointAltitude = forceActionPointAltitude;
        }

        // MainWindowViewModelを後からセットするメソッド
        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }

        // 重複チェック
        private bool IsDuplicateLoadName(string value)
        {
            var allLoadCases = InputModel?.LoadCasesInput.AllLoadCases;
            if (allLoadCases == null) return false;
            foreach (var loadCase in allLoadCases)
            {
                if (loadCase != this && loadCase.LoadName == value)
                    return true;
            }
            return false;
        }

        public string GetLoadName() => LoadName;

        /// <summary>杭軸力の合計 ΣV (kN) — 全杭配置の地震時軸力を合算</summary>
        public double SumV
        {
            get
            {
                var pileLayouts = InputModel?.PileLayoutItems;
                if (pileLayouts == null || pileLayouts.Count == 0) return 0;
                double sum = 0;
                int idx = No - 1;
                foreach (var p in pileLayouts)
                {
                    var forces = Level == 1 ? p.AxialForceLevel1s : p.AxialForceLevel2s;
                    if (forces != null && idx >= 0 && idx < forces.Count)
                        sum += forces[idx];
                }
                return sum;
            }
        }

        /// <summary>代表節点への水平力の合計 ΣH (kN) — 上部構造慣性力＋基礎構造慣性力</summary>
        public double SumH => UpperMassForce + FoundationMassForce;

        /// <summary>ΣH/ΣV 比</summary>
        public string SumHOverSumVText
        {
            get
            {
                double v = SumV;
                if (Math.Abs(v) < 1e-6) return "-";
                return (SumH / Math.Abs(v)).ToString("F3");
            }
        }

        /// <summary>ΣV, ΣH, ΣH/ΣV の変更通知を発行</summary>
        public void RaiseForceSummaryChanged()
        {
            OnPropertyChanged(nameof(SumV));
            OnPropertyChanged(nameof(SumH));
            OnPropertyChanged(nameof(SumHOverSumVText));
        }

        // 深いコピー
        public LoadCase DeepCopy()
        {
            return (LoadCase)this.MemberwiseClone();
        }
    }

    public static class LoadCases
    {
        public static LoadCase? GetLoadCase(ObservableCollection<LoadCase> loadCases, string loadName)
        {
            foreach (var loadCase in loadCases)
            {
                if (loadName == loadCase.GetLoadName())
                {
                    return loadCase;
                }
            }
            return null;
        }
    }
}
