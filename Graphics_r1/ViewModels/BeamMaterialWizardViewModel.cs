using System;

namespace PileDesign.ViewModels
{
    public class BeamMaterialWizardViewModel : BaseViewModel
    {
        // 名称
        private string _name = "FC30";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        // コンクリート強度 Fc (N/mm²)
        private double _fc = 30.0;
        public double Fc
        {
            get => _fc;
            set
            {
                if (SetProperty(ref _fc, value))
                {
                    CalculateProperties();
                }
            }
        }

        // 単位重量 γ (kN/m³)
        private double _gamma = 24.0;
        public double Gamma
        {
            get => _gamma;
            set
            {
                if (SetProperty(ref _gamma, value))
                {
                    CalculateProperties();
                }
            }
        }

        // ポアソン比
        private double _poissonRatio = 0.2;
        public double PoissonRatio
        {
            get => _poissonRatio;
            set
            {
                if (SetProperty(ref _poissonRatio, value))
                {
                    CalculateProperties();
                }
            }
        }

        // ヤング係数 Ec (N/mm²)
        private double _ec;
        public double Ec
        {
            get => _ec;
            private set => SetProperty(ref _ec, value);
        }

        // せん断弾性係数 G (N/mm²)
        private double _g;
        public double G
        {
            get => _g;
            private set => SetProperty(ref _g, value);
        }

        public BeamMaterialWizardViewModel()
        {
            CalculateProperties();
        }

        private void CalculateProperties()
        {
            // ヤング係数の計算式（土木学会コンクリート標準示方書）
            // Ec = 3.35 × 10⁴ × (γ/24)² × (Fc/60)^(1/3)  (N/mm²)
            // ここで、γ は kN/m³、Fc は N/mm²

            double gammaRatio = Gamma / 24.0;
            double fcRatio = Fc / 60.0;

            // Ec (N/mm²)
            Ec = 3.35e4 * Math.Pow(gammaRatio, 2) * Math.Pow(fcRatio, 1.0 / 3.0);

            // せん断弾性係数の計算
            // G = Ec / (2 × (1 + ν))
            G = Ec / (2.0 * (1.0 + PoissonRatio));
        }
    }
}
