using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    public class ElementDivision : BaseViewModel
    {
        private ObservableCollection<SoilPile> _soilPiles;
        public ObservableCollection<SoilPile> SoilPiles
        {
            get => _soilPiles;
            set => SetProperty(ref _soilPiles, value);
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

        private SoilEmbedment _soilEmbedment;
        public SoilEmbedment SoilEmbedment
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


        private DoatsuGoryokuBane _doatsuGoryokuBane;
        public DoatsuGoryokuBane DoatsuGoryokuBane
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
