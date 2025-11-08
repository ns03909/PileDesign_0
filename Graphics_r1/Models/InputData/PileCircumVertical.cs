using System;

namespace PileDesign.Models.InputData
{
    // PileCircumVerticalクラス
    public class PileCircumVertical : BaseModel
    {
        public double Top { get; set; }
        public double Bottom { get; set; }
        public PileBodySegment PileBodySegment { get; set; }
        public GroundLayerInput GroundLayer { get; set; }


        // 第1折点変位
        private double _s1;
        public double S1
        {
            get => _s1;
            set => SetProperty(ref _s1, value);
        }

        // 第2折点変位
        private double _s2;
        public double S2
        {
            get => _s2;
            set => SetProperty(ref _s2, value);
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

        // 第1折点周面抵抗
        private double _tau1;
        public double Tau1
        {
            get => _tau1;
            set => SetProperty(ref _tau1, value);
        }

        // 第2折点周面抵抗 
        private double _tau2;
        public double Tau2
        {
            get => _tau2;
            set => SetProperty(ref _tau2, value);
        }

        // 極限引き抜き周面抵抗 
        private double _tauT;
        public double TauT
        {
            get => _tauT;
            set => SetProperty(ref _tauT, value);
        }

        // 杭径 m
        public double D => PileBodySegment.PileSection.PileDiameter / 1000.0;

        // 区間長 m
        public double L => Top - Bottom;

        // 周長 m
        public double Psi => Math.PI * D;

        // 杭周面積 m2
        public double PsiL => Psi * L;

        // 極限周面抵抗 kN
        public double Rf => Tau2 * L * Psi;

        // 最大引き抜き抵抗力 kN
        public double Rtu => TauT * L * Psi;

        // 残留引抜き抵抗力 kN
        public double Rtr => (1.0 / 1.2) * TauT * L * Psi;

        // 降伏引抜き抵抗力 kN
        public double Rty => (2.0 / 3.0) * TauT * L * Psi;

        public PileCircumVertical DeepCopy()
        {
            return new PileCircumVertical
            {
                Top = this.Top,
                Bottom = this.Bottom,
                PileBodySegment = this.PileBodySegment.DeepCopy(),
                GroundLayer = this.GroundLayer.DeepCopy(),
                S1 = this.S1,
                S2 = this.S2,
                Tau1 = this.Tau1,
                Tau2 = this.Tau2,
                TauT = this.TauT,

                IsPositiveCircumResistance = this.IsPositiveCircumResistance,
                IsNegativeCircumResistance = this.IsNegativeCircumResistance
            };
        }
    }
}
