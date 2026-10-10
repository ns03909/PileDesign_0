using System.Collections.ObjectModel;
using System.Linq;

using PileDesign.Common;

namespace PileDesign.Models.InputData
{
    public class ElementDivision : ObservableModel
    {
        private ObservableCollection<SoilPile> _soilPiles;
        public ObservableCollection<SoilPile> SoilPiles
        {
            get => _soilPiles;
            set => SetProperty(ref _soilPiles, value);
        }

        /// <summary>
        /// PropertyChanged を発火せずに _soilPiles を直接書換える (Undo スナップショット時の一時退避用)。
        /// DataGrid (ItemsSource バインディング) の再構築を防ぎ、セル編集中に値がリセットされる問題を回避する。
        /// </summary>
        internal void SetSoilPilesSilently(ObservableCollection<SoilPile> value)
        {
            _soilPiles = value;
        }

        /// <summary>
        /// 土層-杭セットを空にした浅い写し (変更通知の購読者は写さない)。InputModel.CopyForHistory が、
        /// 土層-杭セットを除いて JSON で複製するために使う。<b>元は書き換えない。</b>
        /// 以前は元の SoilPiles を一時的に空へ差し替えて複製しており、複製は Task.Run でも走るので、
        /// その間に画面・自動保存が空の一覧を読みえた。
        /// </summary>
        internal ElementDivision ShellWithoutSoilPiles()
        {
            var shell = CloneWithoutSubscribers<ElementDivision>();
            shell._soilPiles = [];
            return shell;
        }

        public void UpdateSoilPileNumberOption()
        {
            SoilPileNumberOption = new ObservableCollection<int>(Enumerable.Range(1, SoilPiles.Count));
        }

        private ObservableCollection<int> _soilPileNumberOption;
        public ObservableCollection<int> SoilPileNumberOption
        {
            get => _soilPileNumberOption;
            set => SetProperty(ref _soilPileNumberOption, value);
        }

        // 根入部が無いとき (計算例の読込で根入れを外したときなど) は null
        private SoilEmbedment? _soilEmbedment;
        public SoilEmbedment? SoilEmbedment
        {
            get => _soilEmbedment;
            set => SetProperty(ref _soilEmbedment, value);
        }

        private double _firstDistance;
        public double FirstDistance
        {
            get => _firstDistance;
            set => SetProperty(ref _firstDistance, value);
        }

        private double _maxPileSpacing;
        public double MaxPileSpacing
        {
            get => _maxPileSpacing;
            set => SetProperty(ref _maxPileSpacing, value);
        }

        private double _maxEmbedmentSpacing;
        public double MaxEmbedmentSpacing
        {
            get => _maxEmbedmentSpacing;
            set => SetProperty(ref _maxEmbedmentSpacing, value);
        }

        private int _pileGroundNo;
        public int PileGroundNo
        {
            get => _pileGroundNo;
            set => SetProperty(ref _pileGroundNo, value);
        }

        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private double _pileBottomAltitude;
        public double PileBottomAltitude
        {
            get => _pileBottomAltitude;
            set => SetProperty(ref _pileBottomAltitude, value);
        }


        // 根入部が無いときは null (SoilEmbedment と同じ)
        private DoatsuGoryokuBane? _doatsuGoryokuBane;
        public DoatsuGoryokuBane? DoatsuGoryokuBane
        {
            get => _doatsuGoryokuBane;
            set => SetProperty(ref _doatsuGoryokuBane, value);
        }

        // コンストラクタ
        public ElementDivision()
        {
            PileGroundNo = 1;
            PileBodyNo = 1;
            SoilPiles = [];
            FirstDistance = 0.1;
            MaxPileSpacing = 1.0;
            MaxEmbedmentSpacing = 5.0;
        }
    }
}
