using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 荷重組合せ 1 通り分の係数。
    ///
    /// プロパティ名と画面・計算書の記号の対応 (名前からは読み取れないので注意):
    ///   Alpha1 = αL … 杭曲げモーメント最大時の地盤変位 / 地盤変位の最大値
    ///   Beta1  = βU … 杭曲げモーメント最大時の上部構造慣性力 / その最大値
    ///   Beta2  = βL … 杭曲げモーメント最大時の基礎部慣性力 / その最大値
    ///
    /// 杭体応力の低減係数 β₁ / β₂ (基礎指針) とは別の量。
    /// 表示で β₁ / β₂ を使うとそちらと見分けがつかないので、
    /// 画面・グラフ・計算書のいずれも αL / βU / βL で統一する。
    /// </summary>
    public class LoadCombination(int no, double alpha1, double beta1, double beta2) : INotifyPropertyChanged
    {
        private bool _isApplicable = true;
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

        private int _no = no;
        public int No
        {
            get => _no;
            set
            {
                if (_no != value)
                {
                    _no = value;
                    OnPropertyChanged(nameof(No));
                }
            }
        }

        private double _alpha1 = alpha1;
        public double Alpha1
        {
            get => _alpha1;
            set
            {
                if (_alpha1 != value)
                {
                    _alpha1 = value;
                    OnPropertyChanged(nameof(Alpha1));
                }
            }
        }
        private double _beta1 = beta1;
        public double Beta1
        {
            get => _beta1;
            set
            {
                if (_beta1 != value)
                {
                    _beta1 = value;
                    OnPropertyChanged(nameof(Beta1));
                }
            }
        }


        private double _beta2 = beta2;
        public double Beta2
        {
            get => _beta2;
            set
            {
                if (_beta2 != value)
                {
                    _beta2 = value;
                    OnPropertyChanged(nameof(Beta2));
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
        /// 未解析時は IsApplicable=true でも未チェック表示にする (UX)。
        /// 実体 (_isApplicable) は変更しないので CanExecuteAnalysis 等の解析ロジックに影響しない。
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

        public string Name => "α\u2097:" + Alpha1.ToString("F2") + "/β\u1d64:" + Beta1.ToString("F2") + "/β\u2097:" + Beta2.ToString("F2");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        public string GetName()
        {
            return Alpha1.ToString("F2") + "/" + Beta1.ToString("F2") + "/" + Beta2.ToString("F2");
        }

        // 深いコピーを作成するメソッド
        public LoadCombination DeepCopy()
        {
            return (LoadCombination)this.MemberwiseClone();
        }
    }

    // 名前からLoadCombinationを返すメソッド
    public static class LoadCombinations
    {
        public static LoadCombination GetLoadCombination(ObservableCollection<LoadCombination> loadCombinations, string name)
        {
            foreach (var loadCombination in loadCombinations)
            {
                if (name == loadCombination.GetName())
                {
                    return loadCombination;
                }
            }
            return null;
        }
    }
}


