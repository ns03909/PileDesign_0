using System.Collections.Generic;
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

        /// <summary>ケース番号の名前 (画面・メッセージ用)。</summary>
        public static string CaseName(int caseIndex) => caseIndex switch
        {
            0 => "L1 非液状化",
            1 => "L1 液状化",
            2 => "L2 非液状化",
            3 => "L2 液状化",
            _ => $"ケース{caseIndex}",
        };

        /// <summary>荷重レベル (1/2) と液状化の別に当たるケース番号。</summary>
        public static int CaseIndexOf(int level, bool isLiquefaction) => (level == 2 ? 2 : 0) + (isLiquefaction ? 1 : 0);

        /// <summary>
        /// 1 つのプロファイルの形の誤り (無ければ空)。<see cref="Interpolate"/> が前提にしている形を確かめる。
        /// <list type="bullet">
        /// <item>標高・変位が数値 (NaN・無限大でない)</item>
        /// <item>標高が上から下へ<b>厳密に</b>下がっていく (逆順の点があるとその点を飛ばした区間で補間して入力と違う値になり、
        ///   区間が見つからなければ 0 mm を返す。同じ標高が 2 点あるとどちらの変位か決まらない)</item>
        /// </list>
        /// 空かどうかはここでは見ない (使うケースかどうかで扱いが違うので呼び出し側が決める)。
        /// </summary>
        public static List<string> DescribeProblems(IList<DisplacementPoint>? profile)
        {
            var problems = new List<string>();
            if (profile == null) return problems;
            for (int i = 0; i < profile.Count; i++)
            {
                var p = profile[i];
                if (p == null) { problems.Add($"{i + 1} 点目のデータがありません"); continue; }
                if (!double.IsFinite(p.Z) || !double.IsFinite(p.Displacement))
                    problems.Add($"{i + 1} 点目の標高・変位が数値ではありません (標高 {p.Z} / 変位 {p.Displacement})");
                if (i == 0 || profile[i - 1] == null || !double.IsFinite(p.Z) || !double.IsFinite(profile[i - 1].Z)) continue;
                double above = profile[i - 1].Z;
                if (System.Math.Abs(p.Z - above) < 1e-9)
                    problems.Add($"{i} 点目と {i + 1} 点目が同じ標高 ({p.Z:F3} m) です");
                else if (p.Z > above)
                    problems.Add($"{i + 1} 点目 (標高 {p.Z:F3} m) が {i} 点目 ({above:F3} m) より高く、上から下の順に並んでいません");
            }
            return problems;
        }

        /// <summary>
        /// 指定標高での線形補間による変位値を返す（mm）
        /// プロファイルが空または標高が範囲外の場合は0を返す。
        /// 点は標高の高い順に並んでいる前提 (<see cref="DescribeProblems"/> で解析前に確かめる)。
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
