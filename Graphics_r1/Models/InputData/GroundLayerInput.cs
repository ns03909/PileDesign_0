using PileDesign.ViewModels;
using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    public class GroundLayerInput : BaseViewModel
    {
        // 入力データのエラーチェック
        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set
            {
                if (_isError != value)
                {
                    _isError = value;
                    OnPropertyChanged(nameof(IsError));
                }
            }
        }

        // 番号
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        // 下端深さ
        //private double _bottomGLDepth;
        //public double BottomGLDepth
        //{
        //    get => _bottomGLDepth;
        //    set => SetProperty(ref _bottomGLDepth, value);
        //}
        private double _bottomGLDepth;
        public double BottomGLDepth
        {
            get => _bottomGLDepth;
            set
            {
                if (_bottomGLDepth != value)
                {
                    _bottomGLDepth = value;
                    OnPropertyChanged(nameof(BottomGLDepth));
                }
            }
        }

        // 層厚
        private double _layerThickness;
        public double LayerThickness
        {
            get => _layerThickness;
            set => SetProperty(ref _layerThickness, value);
        }

        // 下端Z
        private double _bottomAltitude;
        public double BottomAltitude
        {
            get => _bottomAltitude;
            set => SetProperty(ref _bottomAltitude, value);
        }


        // 土層名
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }


        // 土層分類
        private string _granularityClass;
        public string GranularityClass
        {
            get => _granularityClass;
            set
            {
                if (_granularityClass != value)
                {
                    _granularityClass = value;
                    OnPropertyChanged(nameof(GranularityClass));
                }
            }
            //// set => SetProperty(ref _granularityClass, value);
        }

        // 土層分類選択肢
        public static List<string> GranularityClassOption { get; } =
        [
            "粘性土",
            "砂質土",
            "礫質土"
        ];

        // 土層単位体積重量
        private double _density;
        public double Density
        {
            get => _density;
            set => SetProperty(ref _density, value);
        }

        // 土層年代
        public static List<string> AgeCategoryOption { get; } =
        [
            "沖積層",
            "洪積層"
        ];

        // 選択土層年代
        private string _ageCategory;
        public string AgeCategory
        {
            get => _ageCategory;
            set => SetProperty(ref _ageCategory, value);
        }

        // 工学的基盤か否か
        private bool _isEngineeringBedrock;
        public bool IsEngineeringBedrock
        {
            get => _isEngineeringBedrock;
            set => SetProperty(ref _isEngineeringBedrock, value);
        }

        // N値
        private double _nValue;
        public double NValue
        {
            get => _nValue;
            //set => SetProperty(ref _nValue, value);
            set
            {
                // NaN / Infinity → 0, 範囲 0～1000 にクランプ（必要に応じて上限変更）
                if (SetFiniteClampedDouble(ref _nValue, value, min: 0.0, max: 1000.0, fallback: 0.0))
                {
                    // 依存再計算があればここで呼ぶ (例: RecalculateSomething();)
                }
            }
        }

        // 粘着力
        private double _cohesive;
        public double Cohesive
        {
            get => _cohesive;
            set => SetProperty(ref _cohesive, value);
        }

        // Vs
        private double _vs;
        public double Vs
        {
            get => _vs;
            set => SetProperty(ref _vs, value);
        }

        // 変形係数
        private double _es;
        public double Es
        {
            get => _es;
            set => SetProperty(ref _es, value);
        }

        // 押込み方向周面抵抗
        private bool _isPositiveCircumResistance;
        public bool IsPositiveCircumResistance
        {
            get => _isPositiveCircumResistance;
            set => SetProperty(ref _isPositiveCircumResistance, value);
        }

        // 引抜き方向周面抵抗
        private bool _isNegativeCircumResistance;
        public bool IsNegativeCircumResistance
        {
            get => _isNegativeCircumResistance;
            set => SetProperty(ref _isNegativeCircumResistance, value);
        }

        // 沈下検討用変形係数
        private bool _settleEs;
        public bool SettleEs
        {
            get => _settleEs;
            set => SetProperty(ref _settleEs, value);
        }

        // 沈下検討用ポアソン比
        private bool _settleNus;
        public bool SettleNus
        {
            get => _settleNus;
            set => SetProperty(ref _settleNus, value);
        }

        // コンストラクタ
        public GroundLayerInput()
        {
            No = 0;
            BottomGLDepth = -20.0;
            LayerThickness = 20.0;
            BottomAltitude = -20.0;  // デフォルトでの描画のため。
            Name = "(AS#)";
            GranularityClass = "砂質土";
            Density = 17.0;
            AgeCategory = "沖積層";
            IsEngineeringBedrock = false;
            NValue = 0.0;
            Cohesive = 0.0;
            Vs = 0.0;
            Es = 0.0;
            IsPositiveCircumResistance = true;
            IsNegativeCircumResistance = true;
        }

        // 深いコピーを作成するメソッド
        public GroundLayerInput DeepCopy()
        {
            // プロパティ変更イベントを持たないクリーンな新規インスタンスを生成し、
            // バッキングフィールドへ直接代入して正確なスナップショットを作る
            var copy = new GroundLayerInput();

            copy._isError = _isError;
            copy._no = _no;

            copy._bottomGLDepth = _bottomGLDepth;
            copy._layerThickness = _layerThickness;
            copy._bottomAltitude = _bottomAltitude;

            copy._name = _name;
            copy._granularityClass = _granularityClass;

            copy._density = _density;
            copy._ageCategory = _ageCategory;
            copy._isEngineeringBedrock = _isEngineeringBedrock;

            copy._nValue = _nValue;               // 検証/クランプを再実行しない
            copy._cohesive = _cohesive;
            copy._vs = _vs;
            copy._es = _es;

            copy._isPositiveCircumResistance = _isPositiveCircumResistance;
            copy._isNegativeCircumResistance = _isNegativeCircumResistance;

            copy._settleEs = _settleEs;
            copy._settleNus = _settleNus;

            return copy;
        }
        //{
        //    var copy = (GroundLayerInput)this.MemberwiseClone();
        //    return copy;
        //}
    }
}



