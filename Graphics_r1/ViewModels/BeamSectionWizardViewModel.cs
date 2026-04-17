using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace PileDesign.ViewModels
{
    public partial class BeamSectionWizardViewModel : BaseViewModel
    {
        // 名称
        [ObservableProperty]
        private string _name = "500x800";

        // 断面寸法
        [ObservableProperty]
        private double _width = 0.5;

        partial void OnWidthChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _height = 0.8;

        partial void OnHeightChanged(double value) => CalculateProperties();

        // 増減係数（デフォルト1.0）
        [ObservableProperty]
        private double _aFactor = 1.0;

        partial void OnAFactorChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _ayFactor = 1.0;

        partial void OnAyFactorChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _azFactor = 1.0;

        partial void OnAzFactorChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _ixxFactor = 1.0;

        partial void OnIxxFactorChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _iyyFactor = 1.0;

        partial void OnIyyFactorChanged(double value) => CalculateProperties();

        [ObservableProperty]
        private double _izzFactor = 1.0;

        partial void OnIzzFactorChanged(double value) => CalculateProperties();

        // 計算結果（読み取り専用）
        private double _area;
        public double Area
        {
            get => _area;
            private set => SetProperty(ref _area, value);
        }

        private double _shearAreaY;
        public double ShearAreaY
        {
            get => _shearAreaY;
            private set => SetProperty(ref _shearAreaY, value);
        }

        private double _shearAreaZ;
        public double ShearAreaZ
        {
            get => _shearAreaZ;
            private set => SetProperty(ref _shearAreaZ, value);
        }

        private double _torsionalMoment;
        public double TorsionalMoment
        {
            get => _torsionalMoment;
            private set => SetProperty(ref _torsionalMoment, value);
        }

        private double _momentOfInertiaYY;
        public double MomentOfInertiaYY
        {
            get => _momentOfInertiaYY;
            private set => SetProperty(ref _momentOfInertiaYY, value);
        }

        private double _momentOfInertiaZZ;
        public double MomentOfInertiaZZ
        {
            get => _momentOfInertiaZZ;
            private set => SetProperty(ref _momentOfInertiaZZ, value);
        }

        public BeamSectionWizardViewModel()
        {
            CalculateProperties();
        }

        private void CalculateProperties()
        {
            // 断面積 A = b × h
            Area = Width * Height * AFactor;

            // せん断断面積（矩形断面の場合、実断面積の5/6）
            // Ay = (5/6) × b × h
            ShearAreaY = (5.0 / 6.0) * Width * Height * AyFactor;

            // Az = (5/6) × b × h
            ShearAreaZ = (5.0 / 6.0) * Width * Height * AzFactor;

            // 断面ねじりモーメント Ixx（矩形断面の場合）
            // Ixx = β × b × h³
            // β は b/h の比に依存（簡略化のため、標準的な値を使用）
            double ratio = Width / Height;
            double beta;
            if (ratio <= 1.0)
            {
                beta = (1.0 / 3.0) - 0.21 * ratio * (1.0 - Math.Pow(ratio, 4) / 12.0);
            }
            else
            {
                double invRatio = Height / Width;
                beta = (1.0 / 3.0) - 0.21 * invRatio * (1.0 - Math.Pow(invRatio, 4) / 12.0);
                // 入れ替え: Ixx = β × h × b³
                TorsionalMoment = beta * Height * Math.Pow(Width, 3) * IxxFactor;
                MomentOfInertiaYY = Width * Math.Pow(Height, 3) / 12.0 * IyyFactor;
                MomentOfInertiaZZ = Height * Math.Pow(Width, 3) / 12.0 * IzzFactor;
                return;
            }

            TorsionalMoment = beta * Width * Math.Pow(Height, 3) * IxxFactor;

            // 断面二次モーメント
            // Iyy = b × h³ / 12（y軸周り、z方向の曲げ）
            MomentOfInertiaYY = Width * Math.Pow(Height, 3) / 12.0 * IyyFactor;

            // Izz = h × b³ / 12（z軸周り、y方向の曲げ）
            MomentOfInertiaZZ = Height * Math.Pow(Width, 3) / 12.0 * IzzFactor;
        }
    }
}
