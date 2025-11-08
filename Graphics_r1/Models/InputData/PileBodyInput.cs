using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PileDesign.Models.InputData
{
    public class PileBodyInput : BaseModel
    {
        private ObservableCollection<PileBodySegment> _pileBodySegments;
        public ObservableCollection<PileBodySegment> PileBodySegments
        {
            get => _pileBodySegments;
            set => SetProperty(ref _pileBodySegments, value);
        }

        // 杭区間の更新メソッド
        public void PileBodySegmentsUpdate()
        {
            for (int i = 0; i < PileBodySegments.Count; ++i)
            {
                PileBodySegments[i].SegmentDepth = 0.0;
                PileBodySegments[i].No = i + 1; // セグメント番号を更新

                for (int j = 0; j <= i; ++j)
                {
                    PileBodySegments[i].SegmentDepth += PileBodySegments[j].SegmentLength;
                }
            }
        }

        private string _pileBodyRef;
        public string PileBodyRef
        {
            get => _pileBodyRef;
            set => SetProperty(ref _pileBodyRef, value);
        }

        public static ObservableCollection<string> PileBodyTypeOption { get; } =
        [
            "場所打ち鉄筋コンクリート杭",
            "場所打ち鋼管コンクリート杭",
            "既製コンクリート杭",
            "鋼管杭"
        ];

        private string _pileConstructionType;
        public string PileConstructionType
        {
            get => _pileConstructionType;
            set => SetProperty(ref _pileConstructionType, value);
        }

        // 施工タイプオプション
        private ObservableCollection<string> _pileConstructionTypeOption;
        public ObservableCollection<string> PileConstructionTypeOption
        {
            get => _pileConstructionTypeOption;
            set => SetProperty(ref _pileConstructionTypeOption, value);
        }

        public static ObservableCollection<string> InsituPileConstructionTypeOption { get; } =
        ["場所打ちコンクリート杭"];

        public static ObservableCollection<string> PrecastPileConstructionTypeOption { get; } =
        [
            "埋込み杭（プレボーリング）",
            "埋込み杭（中掘り）",
            "打込み杭"
        ];

        public static ObservableCollection<string> SteelPileConstructionTypeOption { get; } =
        [
            "埋込み杭（プレボーリング）",
            "埋込み杭（中掘り）",
            "回転貫入杭"
        ];

        // 杭頭タイプ
        private string _pileTopType;
        public string PileTopType
        {
            get => _pileTopType;
            set => SetProperty(ref _pileTopType, value);
        }

        // 鉄筋コンクリート場所打ち杭　杭頭タイプオプション
        public static ObservableCollection<string> InsituReinforcedConcretePileTopTypeOption { get; } =
        [
            "鉄筋定着工法",
            "キャプテンパイル工法"
            ];

        // 鋼管コンクリート場所打ち杭　杭頭タイプオプション
        public static ObservableCollection<string> InsituSteelPipedConcretePileTopTypeOption { get; } =
        ["鉄筋定着工法"];

        // 既製コンクリート杭　杭頭タイプオプション
        public static ObservableCollection<string> PrecastConcretePileTopTypeOption { get; } =
        [
            "鉄筋定着工法",
            "定着筋方式",
            "埋込み方式",
            "FT-Pile構法"
        ];

        // 鋼管杭　杭頭タイプオプション
        public static ObservableCollection<string> SteelPileTopTypeOption { get; } =
        ["鉄筋定着工法"];

        // 杭頭タイプオプション
        private ObservableCollection<string> _pileTopTypeOption;
        public ObservableCollection<string> PileTopTypeOption
        {
            get => _pileTopTypeOption;
            set => SetProperty(ref _pileTopTypeOption, value);
        }

        // 杭先端径
        private double _pileToeDia;
        public double PileToeDia
        {
            get => _pileToeDia;
            set => SetProperty(ref _pileToeDia, value);
        }

        // 既製コンクリート杭根固め部先端立ち上がり比
        private double _precastConcretePileToeHeightRatio = 2.0;
        public double PrecastConcretePileToeHeightRatio
        {
            get => _precastConcretePileToeHeightRatio;
            set
            {
                if (value < 0 || 5 < value) return;
                SetProperty(ref _precastConcretePileToeHeightRatio, value);
            }
        }

        // 場所打ち拡底杭先端立ち上がり (mm)
        private double _insituPileToeHeight = 300;
        public double InsituPileToeHeight
        {
            get => _insituPileToeHeight;
            set
            {
                if (value < 0 || 1 < value) return;
                SetProperty(ref _insituPileToeHeight, value);
            }
        }

        // 場所打ち拡底杭先端角度
        private double _insituPileToeAngle = 12;
        public double InsituPileToeAngle
        {
            get => _insituPileToeAngle;
            set
            {
                if (value < 10 || 45 < value) return;
                SetProperty(ref _insituPileToeAngle, value);
            }
        }

        // 杭先端閉塞性
        private double _tipNonPermability;
        public double TipNonPermability
        {
            get => _tipNonPermability;
            set => SetProperty(ref _tipNonPermability, value);
        }

        public ObservableCollection<string> TipStyleOption { get; } =
        ["開端杭", "閉端杭",];

        // 杭先端スタイル
        private string _pileTipStyle;
        public string PileTipStyle
        {
            get => _pileTipStyle;
            set => SetProperty(ref _pileTipStyle, value);
        }

        // 支持層への根入れ深さLB(m)
        private double _embedmentIntoBearingSoil;
        public double EmbedmentIntoBearingSoil
        {
            get => _embedmentIntoBearingSoil;
            set => SetProperty(ref _embedmentIntoBearingSoil, value);
        }

        // 杭の内径dI(m)
        private double _pileInnerDia;
        public double PileInnerDia
        {
            get => _pileInnerDia;
            set => SetProperty(ref _pileInnerDia, value);
        }

        private double _settlePileToeDia;
        public double SettlePileToeDia
        {
            get => _settlePileToeDia;
            set => SetProperty(ref _settlePileToeDia, value);
        }

        private double _settleAlpha;
        public double SettleAlpha
        {
            get => _settleAlpha;
            set => SetProperty(ref _settleAlpha, value);
        }


        private string _settleAlphaString;
        public string SettleAlphaString
        {
            get => _settleAlphaString;
            set
            {
                if (SetProperty(ref _settleAlphaString, value))
                {
                    if (double.TryParse(value, out double result))
                    {
                        SettleAlpha = result;
                    }
                }
            }
        }

        private double _settleN;
        public double SettleN
        {
            get => _settleN;
            set => SetProperty(ref _settleN, value);
        }

        private string _settleNString;
        public string SettleNString
        {
            get => _settleNString;
            set
            {
                if (SetProperty(ref _settleNString, value))
                {
                    if (double.TryParse(value, out double result))
                    {
                        SettleN = result;
                    }
                }
            }
        }

        // 名前、alphaの値、nの値を格納する構造体
        public struct PileTipSettlementPresetParameter(string name, string soilType, double alpha, double n)
        {
            public string Name = name;
            public string SoilType = soilType;
            public double Alpha = alpha;
            public double N = n;
        }

        // 構造体を使ってデータのセットを作成
        public List<PileTipSettlementPresetParameter> PileTipSettlementPresetParameters = [];

        // 新しいプロパティを追加
        public ObservableCollection<string> PileTipSettlementPresetParameterNames { get; set; }

        public PileTop PileTop = new();

        public string PileBodyType { get; set; }

        // コンストラクタ
        public PileBodyInput()
        {
            PileBodyRef = "(PB1)";
            PileBodyType = PileBodyTypeOption[0];
            PileTopType = InsituReinforcedConcretePileTopTypeOption[0];
            PileTopTypeOption = InsituReinforcedConcretePileTopTypeOption;

            PileConstructionType = InsituPileConstructionTypeOption[0];
            PileConstructionTypeOption = InsituPileConstructionTypeOption;
            PileToeDia = 1500.0;
            TipNonPermability = 0.0;
            EmbedmentIntoBearingSoil = 1.0;
            PileInnerDia = 0.0;
            PileTipStyle = TipStyleOption[1];
            SettlePileToeDia = 1500.0;
            SettleAlpha = 0.3;
            SettleN = 2;

            PileBodySegments = [new PileBodySegment() { No = 1, }];

            // データを読み込む
            PileTipSettlementPresetParameterNames = [];
            LoadPresetSettlementParameters();
        }

        // CSVからデータを読み込む
        private void LoadPresetSettlementParameters()
        {
            // アプリケーションの実行ディレクトリを取得
            //string basePath = AppDomain.CurrentDomain.BaseDirectory;

            //string relativePath = "../../PileLibrary/PresetSettlementParameterSet.csv";
            //string csvFilePath = @"C:/Users/keisu/source/repos/PileDesign/PileDesign/PileLibrary/PresetSettlementParameterSet.csv";
            // 絶対パスを生成
            //string csvFilePath = Path.GetFullPath(Path.Combine(basePath, relativePath));
            // 実行ファイルのディレクトリを基準にパスを組み立てる
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string csvFilePath = Path.Combine(baseDir, "Models", "PileLibrary", "PresetSettlementParameterSet.csv");


            using StreamReader reader = new(csvFilePath, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split(',');
                if (parts.Length == 4)
                {
                    string name = parts[0].Trim();
                    string soilType = parts[1].Trim();
                    double alpha = double.Parse(parts[2].Trim());
                    double n = double.Parse(parts[3].Trim());

                    PileTipSettlementPresetParameters.Add(new PileTipSettlementPresetParameter(name, soilType, alpha, n));
                    PileTipSettlementPresetParameterNames.Add(name + "-" + soilType + " ,α=" + $"{alpha:N2}" + " ,n=" + $"{n:N2}");
                }
            }
        }

        public virtual PileHeadRotationDef GetMThetaRelationship(double axialN)
        {
            // まず杭頭タイプ/杭体タイプで優先分岐
            string pileTopType = PileTopType ?? string.Empty;
            string pileBodyType = PileBodyType ?? string.Empty;

            // 1) キャプテンパイル工法 → PileTop.CaptainPile の M-θ を採用
            if (pileTopType.Contains("キャプテンパイル工法"))
            {
                var cp = PileTop?.CaptainPile;
                var curve = TryCallMThetaRelationship(cp, axialN);
                if (curve != null) return PileHeadRotationDef.Combined(curve);
                // 失敗時は後段の汎用ロジックへフォールバック
            }

            // 2) FT-Pile 構法 → PileTop.FTPile の M-θ を採用
            if (pileTopType.Contains("FT-Pile構法"))
            {
                var ft = PileTop?.FTPile;
                var curve = TryCallMThetaRelationship(ft, axialN);
                if (curve != null) return PileHeadRotationDef.Combined(curve);
                // 失敗時は後段の汎用ロジックへフォールバック
            }

            // 3) 定着筋方式/埋込み方式 → 完全剛
            if (pileTopType.Contains("定着筋方式") || pileTopType.Contains("埋込み方式"))
            {
                return PileHeadRotationDef.Rigid();
            }

            // 4) 鉄筋定着工法
            if (pileTopType.Contains("鉄筋定着工法"))
            {
                // 場所打ち鋼管コンクリート杭/既製コンクリート杭 → 完全剛
                if (pileBodyType.Contains("場所打ち鋼管コンクリート杭") || pileBodyType.Contains("既製コンクリート杭"))
                {
                    return PileHeadRotationDef.Rigid();
                }

                // 場所打ち鉄筋コンクリート杭 → 最上段セグメントの Section から M-θ を取得
                if (pileBodyType.Contains("場所打ち鉄筋コンクリート杭"))
                {
                    var section = PileBodySegments?.FirstOrDefault()?.PileSection;
                    var curve = TryCallMThetaRelationship(section, axialN);
                    if (curve != null) return PileHeadRotationDef.Combined(curve);
                    // 失敗時は後段の汎用ロジックへフォールバック
                }
            }

            // ===== ここから汎用ロジック（従来実装） =====
            var pileTop = this.PileTop as object;
            if (pileTop == null) return PileHeadRotationDef.CombinedLinear(0.0);

            // 完全剛フラグ
            var rigidProp = pileTop.GetType().GetProperty("IsRigidHead") ??
                            pileTop.GetType().GetProperty("RigidHead") ??
                            pileTop.GetType().GetProperty("IsRotationRigid");
            if (rigidProp?.GetValue(pileTop) is bool isRigid && isRigid)
            {
                return PileHeadRotationDef.Rigid();
            }

            // 合成XY（N依存のファミリ）
            var famXY = pileTop.GetType().GetProperty("MthetaXY_ByN")?.GetValue(pileTop);
            if (famXY != null)
            {
                var c = AxialCurveFamily.ResolveMTheta(famXY, axialN);
                if (c != null) return PileHeadRotationDef.Combined(c);
            }

            // 個別（N依存）
            var famX = pileTop.GetType().GetProperty("MthetaX_ByN")?.GetValue(pileTop);
            var famY = pileTop.GetType().GetProperty("MthetaY_ByN")?.GetValue(pileTop);
            var cx = famX != null ? AxialCurveFamily.ResolveMTheta(famX, axialN) : null;
            var cy = famY != null ? AxialCurveFamily.ResolveMTheta(famY, axialN) : null;
            if (cx != null || cy != null) return PileHeadRotationDef.Separate(cx, cy);

            // 線形K
            double? kxy = TryGetDouble(pileTop, "KthetaXY", "KΘXY", "Kxy");
            if (kxy.HasValue) return PileHeadRotationDef.CombinedLinear(kxy.Value);

            double? kx = TryGetDouble(pileTop, "KthetaX", "Kθx", "Kx");
            double? ky = TryGetDouble(pileTop, "KthetaY", "Kθy", "Ky");
            if (kx.HasValue || ky.HasValue) return PileHeadRotationDef.Separate(null, null, kx, ky);

            // データ無し
            return PileHeadRotationDef.CombinedLinear(0.0);
        }

        // 反射で obj.GetMThetaRelationship(N) を呼び、(theta[], M[]) から曲線を生成
        private static MomentRotationCurve? TryCallMThetaRelationship(object obj, double axialN)
        {
            if (obj == null) return null;

            var mi = obj.GetType().GetMethod("GetMThetaRelationship", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return null;

            object result;
            try
            {
                // 引数 (N) または (N, beta) 等があれば (N,1.0) で呼ぶ
                var ps = mi.GetParameters();
                result = ps.Length switch
                {
                    1 => mi.Invoke(obj, new object[] { axialN }),
                    2 => mi.Invoke(obj, new object[] { axialN, 1.0 }),
                    _ => mi.Invoke(obj, new object[] { axialN })
                };
            }
            catch
            {
                return null;
            }
            if (result == null) return null;

            // 戻り値は (Thetas, Moments) を想定（IEnumerable<double>）
            var t = result.GetType();
            var item1 = t.GetProperty("Item1")?.GetValue(result) as System.Collections.IEnumerable;
            var item2 = t.GetProperty("Item2")?.GetValue(result) as System.Collections.IEnumerable;
            if (item1 == null || item2 == null) return null;

            var th = item1.Cast<object>().Select(Convert.ToDouble).ToList();
            var mm = item2.Cast<object>().Select(Convert.ToDouble).ToList();
            if (th.Count < 2 || th.Count != mm.Count) return null;

            var pts = new List<(double theta, double moment)>(th.Count);
            for (int i = 0; i < th.Count; i++) pts.Add((th[i], mm[i]));
            return new MomentRotationCurve(pts);
        }

        private static double? TryGetDouble(object target, params string[] names)
        {
            foreach (var n in names)
            {
                var p = target.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p?.GetValue(target) is IConvertible cv) return Convert.ToDouble(cv);
            }
            return null;
        }


        // 深いコピーを作成するメソッド
        public PileBodyInput DeepCopy()
        {
            var copy = (PileBodyInput)this.MemberwiseClone();
            copy.PileBodySegments = new ObservableCollection<PileBodySegment>(this.PileBodySegments.Select(segment => segment.DeepCopy()));
            copy.PileTop = this.PileTop.DeepCopy();
            return copy;
        }
    }
}
