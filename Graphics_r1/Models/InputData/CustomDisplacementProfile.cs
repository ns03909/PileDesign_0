using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 任意地盤変位プロファイルの1点（標高-変位ペア）
    /// </summary>
    public class DisplacementPoint : BaseModel
    {
        private double _z;
        [JsonPropertyName("z")]
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private double _displacement;
        [JsonPropertyName("displacement")]
        public double Displacement
        {
            get => _displacement;
            set => SetProperty(ref _displacement, value);
        }

        public DisplacementPoint() { }
        public DisplacementPoint(double z, double displacement)
        {
            Z = z;
            Displacement = displacement;
        }

        public DisplacementPoint DeepCopy() => new(Z, Displacement);
    }

    /// <summary>
    /// 任意地盤変位プロファイル（4ケース: レベル1/2 × 液状化なし/あり）
    /// </summary>
    public class CustomDisplacementProfile : BaseModel
    {
        private bool _isEnabled = false;
        [JsonPropertyName("isEnabled")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private ObservableCollection<DisplacementPoint> _level1NonLiq = [];
        [JsonPropertyName("level1NonLiq")]
        public ObservableCollection<DisplacementPoint> Level1NonLiq
        {
            get => _level1NonLiq;
            set => SetProperty(ref _level1NonLiq, value);
        }

        private ObservableCollection<DisplacementPoint> _level1Liq = [];
        [JsonPropertyName("level1Liq")]
        public ObservableCollection<DisplacementPoint> Level1Liq
        {
            get => _level1Liq;
            set => SetProperty(ref _level1Liq, value);
        }

        private ObservableCollection<DisplacementPoint> _level2NonLiq = [];
        [JsonPropertyName("level2NonLiq")]
        public ObservableCollection<DisplacementPoint> Level2NonLiq
        {
            get => _level2NonLiq;
            set => SetProperty(ref _level2NonLiq, value);
        }

        private ObservableCollection<DisplacementPoint> _level2Liq = [];
        [JsonPropertyName("level2Liq")]
        public ObservableCollection<DisplacementPoint> Level2Liq
        {
            get => _level2Liq;
            set => SetProperty(ref _level2Liq, value);
        }

        /// <summary>
        /// ケース番号で取得（0=L1非液状化, 1=L1液状化, 2=L2非液状化, 3=L2液状化）
        /// </summary>
        public ObservableCollection<DisplacementPoint> GetProfile(int caseIndex) => caseIndex switch
        {
            0 => Level1NonLiq,
            1 => Level1Liq,
            2 => Level2NonLiq,
            3 => Level2Liq,
            _ => Level1NonLiq
        };

        /// <summary>
        /// 指定標高での線形補間による変位値を返す（mm）
        /// プロファイルが空または標高が範囲外の場合は0を返す
        /// </summary>
        public double Interpolate(ObservableCollection<DisplacementPoint> profile, double z)
        {
            if (profile == null || profile.Count == 0) return 0;
            if (profile.Count == 1) return profile[0].Displacement;

            // Z降順（上から下）にソートされている前提
            // 範囲外: 最上/最下の値を返す
            if (z >= profile[0].Z) return profile[0].Displacement;
            if (z <= profile[^1].Z) return profile[^1].Displacement;

            // 2点間の線形補間
            for (int i = 0; i < profile.Count - 1; i++)
            {
                double z1 = profile[i].Z;
                double z2 = profile[i + 1].Z;
                if (z <= z1 && z >= z2)
                {
                    double t = (z1 - z) / (z1 - z2);
                    return profile[i].Displacement * (1 - t) + profile[i + 1].Displacement * t;
                }
            }

            return 0;
        }

        public CustomDisplacementProfile DeepCopy()
        {
            return new CustomDisplacementProfile
            {
                IsEnabled = IsEnabled,
                Level1NonLiq = new(Level1NonLiq.Select(p => p.DeepCopy())),
                Level1Liq = new(Level1Liq.Select(p => p.DeepCopy())),
                Level2NonLiq = new(Level2NonLiq.Select(p => p.DeepCopy())),
                Level2Liq = new(Level2Liq.Select(p => p.DeepCopy())),
            };
        }
    }
}
