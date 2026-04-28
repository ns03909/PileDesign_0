using Newtonsoft.Json;
using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;

using Serilog;
namespace PileDesign.Models.InputData
{
    public class PileBodyInput : BaseModel
    {
        // 静的キャッシュ（CSVデータは一度だけ読み込む）
        private static readonly Lazy<(List<PileTipSettlementPresetParameter> Parameters, List<string> Names)> _cachedPresetParameters
            = new(() => LoadPresetSettlementParametersFromCsv());

        private static (List<PileTipSettlementPresetParameter> Parameters, List<string> Names) LoadPresetSettlementParametersFromCsv()
        {
            var parameters = new List<PileTipSettlementPresetParameter>();
            var names = new List<string>();

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string csvFilePath = Path.Combine(baseDir, "Models", "PileLibrary", "PresetSettlementParameterSet.csv");

                if (!File.Exists(csvFilePath))
                {
                    return (parameters, names);
                }

                using StreamReader reader = new(csvFilePath, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 4)
                    {
                        string name = parts[0].Trim();
                        string soilType = parts[1].Trim();
                        if (!double.TryParse(parts[2].Trim(), out double alpha) ||
                            !double.TryParse(parts[3].Trim(), out double n))
                        {
                            continue;
                        }

                        parameters.Add(new PileTipSettlementPresetParameter(name, soilType, alpha, n));
                        names.Add($"{name}-{soilType} ,α={alpha:N2} ,n={n:N2}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PileBodyInput] プリセット沈下パラメータ CSV の読込に失敗しました");
            }

            return (parameters, names);
        }

        // 追加: セクション既定値リセット抑止フラグ
        private bool _suppressSectionReset = false;

        private ObservableCollection<PileBodySegment> _pileBodySegments;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<PileBodySegment> PileBodySegments
        {
            get => _pileBodySegments;
            set
            {
                if (_pileBodySegments == value) return;

                // 既存の購読解除
                if (_pileBodySegments != null)
                    _pileBodySegments.CollectionChanged -= PileBodySegments_CollectionChanged;

                // 値を設定（SetProperty を使って通知）
                SetProperty(ref _pileBodySegments, value);

                // 新しいコレクションに購読を追加し、既存アイテムに親タイプを反映
                if (_pileBodySegments != null)
                {
                    _pileBodySegments.CollectionChanged += PileBodySegments_CollectionChanged;
                    foreach (var seg in _pileBodySegments)
                    {
                        //if (seg?.PileSection != null)
                        //{
                        //    seg.PileSection.PileBodyType = this.PileBodyType;
                        //    // セクションの既定値を再設定するかは抑止フラグで制御
                        //    if (!_suppressSectionReset)
                        //    {
                        //        seg.PileSection.ResetSectionProperties();
                        //    }
                        //}
                        if (seg?.PileSection != null)
                        {
                            // PileBodyTypeがnullや空文字の場合は既定値をセット
                            var safeType = string.IsNullOrWhiteSpace(this.PileBodyType)
                                ? PileBodyTypeOption.FirstOrDefault() ?? "場所打ち鉄筋コンクリート杭"
                                : this.PileBodyType;
                            seg.PileSection.PileBodyType = safeType;

                            if (!_suppressSectionReset)
                            {
                                seg.PileSection.ResetSectionProperties();
                            }
                        }
                    }
                }
            }
        }

        // 杭区間の更新メソッド
        public void PileBodySegmentsUpdate()
        {
            try
            {
                if (PileBodySegments == null || PileBodySegments.Count == 0)
                    return;

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
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"杭区間情報の更新中にエラーが発生しました。\n{ex.Message}", "区間更新エラー", MessageBoxButton.OK, MessageBoxImage.Error));
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
                if (!double.IsFinite(value)) return;
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
                if (!double.IsFinite(value)) return;
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
                if (!double.IsFinite(value)) return;
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

        //public string PileBodyType { get; set; }
        private string _pileBodyType;
        public string PileBodyType
        {
            get => _pileBodyType;
            set
            {
                try
                {
                    if (SetProperty(ref _pileBodyType, value))
                    {
                        if (PileBodySegments != null)
                        {
                            foreach (var seg in PileBodySegments)
                            {
                                if (seg?.PileSection != null)
                                    seg.PileSection.PileBodyType = value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        MessageBox.Show($"杭体タイプ設定中にエラーが発生しました。\n{ex.Message}", "プロパティエラー", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }
        }

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

            //Pil/*eBodySegments = [new PileBodySegment() { No = 1, }];*/

            //// コンストラクタ内で PileBodySegments 初期化直後に追加
            //PileBodySegments = new ObservableCollection<PileBodySegment> { new PileBodySegment() { No = 1 } };
            //PileBodySegments.CollectionChanged += PileBodySegments_CollectionChanged;
            // コンストラクタの該当部分を以下のようにして一度だけ初期化してください
            PileBodySegments = [new() { No = 1 }];

            // 静的キャッシュからデータを参照（毎回CSVを読み込まない）
            var cached = _cachedPresetParameters.Value;
            PileTipSettlementPresetParameters = new List<PileTipSettlementPresetParameter>(cached.Parameters);
            PileTipSettlementPresetParameterNames = new ObservableCollection<string>(cached.Names);
        }

        private void PileBodySegments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.NewItems == null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is PileBodySegment seg && seg.PileSection != null)
                    {
                        seg.PileSection.PileBodyType = this.PileBodyType;
                        if (!_suppressSectionReset)
                        {
                            seg.PileSection.ResetSectionProperties();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"杭区間コレクション変更時にエラーが発生しました。\n{ex.Message}", "コレクションエラー", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        public virtual PileHeadRotationDef GetMThetaRelationship(double axialN)
        {
            // 単位系の説明:
            // - 断面計算側（InsituReinforcedConcreteSection等）の出力:
            //   θ: [rad] (無次元)
            //   M: [N·mm]
            // - FEM解析側（MomentRotationCurve）の期待値:
            //   θ: [rad]
            //   M: [kNm]
            // よって、M のみ ×10⁻⁶ の変換が必要

            // まず杭頭タイプ/杭体タイプで優先分岐
            string pileTopType = PileTopType ?? string.Empty;
            string pileBodyType = PileBodyType ?? string.Empty;

            // 1) キャプテンパイル工法 → PileTop.CaptainPile の M-θ を採用
            if (pileTopType.Contains("キャプテンパイル工法"))
            {
                var cpObj = PileTop?.CaptainPile;
                // 直接呼べるなら直接呼ぶ（internal にした場合は可能）
                if (cpObj is CaptainPile cp)
                {
                    try
                    {
                        // axialN は kN、CaptainPile は N を期待するため×1000
                        var (thetasObs, msObs) = cp.GetMThetaRelationship(axialN * 1000);
                        var thetas = thetasObs?.Cast<double>().ToList();
                        var msRaw = msObs?.Cast<double>().ToList();
                        if (thetas != null && msRaw != null && thetas.Count >= 2 && thetas.Count == msRaw.Count)
                        {
                            // 単位変換: M [N·mm] → [kNm] (×10⁻⁶)
                            var pts = new List<(double theta, double moment)>(thetas.Count);
                            for (int i = 0; i < thetas.Count; i++)
                                pts.Add((thetas[i], msRaw[i] * 1e-6));
                            return PileHeadRotationDef.Combined(new MomentRotationCurve(pts));
                        }
                    }
                    catch
                    {
                        // 直接呼び出し失敗したら反射フォールバックへ
                    }
                }

                // 反射フォールバック（既存のヘルパをそのまま利用）
                // axialN は kN、計算クラスは N を期待するため×1000
                var curve = TryCallMThetaRelationship(cpObj, axialN * 1000);
                if (curve != null) return PileHeadRotationDef.Combined(curve);
            }

            // 2) FT-Pile 構法 → PileTop.FTPile の M-θ を採用
            if (pileTopType.Contains("FT-Pile構法"))
            {
                var ftObj = PileTop?.FTPile;
                if (ftObj is FTPile ft)
                {
                    try
                    {
                        // axialN は kN、FTPile は N を期待するため×1000
                        var (thetasObs, msObs) = ft.GetMThetaRelationship(axialN * 1000);
                        var thetas = thetasObs?.Cast<double>().ToList();
                        var msRaw = msObs?.Cast<double>().ToList();
                        if (thetas != null && msRaw != null && thetas.Count >= 2 && thetas.Count == msRaw.Count)
                        {
                            // 単位変換: M [N·mm] → [kNm] (×10⁻⁶)
                            var pts = new List<(double theta, double moment)>(thetas.Count);
                            for (int i = 0; i < thetas.Count; i++)
                                pts.Add((thetas[i], msRaw[i] * 1e-6));
                            return PileHeadRotationDef.Combined(new MomentRotationCurve(pts));
                        }
                    }
                    catch
                    {
                        // フォールバック
                    }
                }

                // axialN は kN、FTPile は N を期待するため×1000
                var curve = TryCallMThetaRelationship(ftObj, axialN * 1000);
                if (curve != null) return PileHeadRotationDef.Combined(curve);
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

                // 場所打ち鉄筋コンクリート杭 → InsituReinforcedConcreteSection.GetMThetaRelationship() を直接呼ぶ
                if (pileBodyType.Contains("場所打ち鉄筋コンクリート杭"))
                {
                    var pileSection = PileBodySegments?.FirstOrDefault()?.PileSection;
                    if (pileSection != null)
                    {
                        // 基礎指針: 軸応力度 σ0 = N/Ae が -Ft ≤ σ0 ≤ (1/4)·ξ·Fc の範囲でのみ
                        // M-θ を考慮する。範囲外 (強引張 or 高圧縮) は杭頭を剛結として検討。
                        if (!pileSection.IsWithinMThetaValidAxialRange(axialN))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[GetMThetaRelationship] 場所打ちRC杭: N={axialN:F1} kN 軸力範囲外 " +
                                $"(σ0 ∉ [-Ft, ξFc/4]) → Rigid()");
                            return PileHeadRotationDef.Rigid();
                        }

                        try
                        {
                            // PileSection.GetMThetaRelationship: axialN は kN、戻り値は θ[rad], M[kNm]
                            var result = pileSection.GetMThetaRelationship(axialN);
                            if (result.HasValue)
                            {
                                var (thetas, ms) = result.Value;
                                var pts = new List<(double theta, double moment)>(thetas.Count);
                                for (int i = 0; i < thetas.Count; i++)
                                    pts.Add((thetas[i], ms[i]));

                                // v28: Mcr 同期 Mode 切替 (ヒステリシス付き) 用に Mcr [kNm] も取得。
                                // null の場合は Mode 切替無効 (従来挙動)。
                                double? mcrKNm = pileSection.GetPileHeadMcrInKNm(axialN);

                                System.Diagnostics.Debug.WriteLine(
                                    $"[GetMThetaRelationship] 場所打ちRC杭: points={pts.Count}, " +
                                    $"θ=[{string.Join(",", thetas.Select(t => t.ToString("E3")))}], " +
                                    $"M=[{string.Join(",", ms.Select(m => m.ToString("F1")))}], " +
                                    $"Mcr={mcrKNm?.ToString("F1") ?? "null"} kNm");
                                return PileHeadRotationDef.Combined(new MomentRotationCurve(pts), mcrKNm);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[GetMThetaRelationship] 場所打ちRC杭 例外: {ex.Message}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[GetMThetaRelationship] 場所打ちRC杭 フォールバック → Rigid()");
                    return PileHeadRotationDef.Rigid();
                }

                // 場所打ち鉄筋コンクリート杭以外の鉄筋定着工法も剛結とする
                return PileHeadRotationDef.Rigid();
            }

            // ===== ここから汎用ロジック（従来実装） =====
            var pileTop = this.PileTop as object;
            if (pileTop == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetMThetaRelationship] PileTop=null → Rigid() (フォールバック)");
                return PileHeadRotationDef.Rigid();
            }

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

            // データ無し → 安全側として剛結（M-θデータなしの場合はモーメントを完全伝達）
            System.Diagnostics.Debug.WriteLine(
                $"[GetMThetaRelationship] M-θデータ取得不可 (PileTopType='{pileTopType}', PileBodyType='{pileBodyType}') → Rigid() (フォールバック)");
            return PileHeadRotationDef.Rigid();
        }

        // 反射で obj の GetMThetaRelationship を呼び、(theta[], M[]) から曲線を生成する（寛容検索版）
        // 注意: 反射で取得したデータは断面計算クラスの生データ（θ:[rad], M:[N·mm]）のため、
        //       M を kNm に変換（×10⁻⁶）する必要がある
        private static MomentRotationCurve? TryCallMThetaRelationship(object obj, double axialN)
        {
            if (obj == null) return null;

            var type = obj.GetType();

            // まず典型的なシグニチャを厳密に検索（double) / (double,double)
            var mi = type.GetMethod("GetMThetaRelationship", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(double) }, null)
                     ?? type.GetMethod("GetMThetaRelationship", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(double), typeof(double) }, null);

            // 見つからなければ名前だけで柔軟に検索（パラメタ数 >= 1 のものを優先）
            if (mi == null)
            {
                mi = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .FirstOrDefault(m => string.Equals(m.Name, "GetMThetaRelationship", StringComparison.OrdinalIgnoreCase)
                                              && m.GetParameters().Length >= 1);
            }

            // 明示的インターフェイス実装など "IFoo.GetMThetaRelationship" の可能性を探す
            if (mi == null)
            {
                mi = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                         .FirstOrDefault(m => m.Name.EndsWith(".GetMThetaRelationship", StringComparison.Ordinal)
                                              && m.GetParameters().Length >= 1);
            }

            if (mi == null) return null;

            object result;
            try
            {
                var ps = mi.GetParameters();
                object[] args;

                if (ps.Length == 1)
                {
                    args = new object[] { Convert.ChangeType(axialN, ps[0].ParameterType) };
                }
                else if (ps.Length == 2)
                {
                    args = new object[] { Convert.ChangeType(axialN, ps[0].ParameterType), Convert.ChangeType(1.0, ps[1].ParameterType) };
                }
                else
                {
                    args = new object[ps.Length];
                    args[0] = Convert.ChangeType(axialN, ps[0].ParameterType);
                    for (int i = 1; i < ps.Length; i++)
                    {
                        var pType = ps[i].ParameterType;
                        if (pType.IsValueType)
                        {
                            if (pType == typeof(double) || pType == typeof(float) || pType == typeof(decimal) || typeof(IConvertible).IsAssignableFrom(pType))
                                args[i] = Convert.ChangeType(1.0, pType);
                            else
                                args[i] = Activator.CreateInstance(pType);
                        }
                        else
                        {
                            args[i] = null;
                        }
                    }
                }

                result = mi.Invoke(obj, args);
            }
            catch
            {
                return null;
            }

            if (result == null) return null;

            // 直接 MomentRotationCurve を返す実装に対応（既に変換済みと仮定）
            if (result is MomentRotationCurve direct) return direct;

            var t = result.GetType();

            // タプル (Item1, Item2) やプロパティ名 Thetas/Moments を探す
            var item1 = t.GetProperty("Item1")?.GetValue(result) as System.Collections.IEnumerable;
            var item2 = t.GetProperty("Item2")?.GetValue(result) as System.Collections.IEnumerable;

            if (item1 == null || item2 == null)
            {
                item1 = t.GetProperty("Thetas")?.GetValue(result) as System.Collections.IEnumerable
                        ?? t.GetProperty("Theta")?.GetValue(result) as System.Collections.IEnumerable;
                item2 = t.GetProperty("Moments")?.GetValue(result) as System.Collections.IEnumerable
                        ?? t.GetProperty("Moment")?.GetValue(result) as System.Collections.IEnumerable;
            }

            if (item1 == null || item2 == null) return null;

            var th = item1.Cast<object>().Select(Convert.ToDouble).ToList();
            var mmRaw = item2.Cast<object>().Select(Convert.ToDouble).ToList();
            if (th.Count < 2 || th.Count != mmRaw.Count) return null;

            // 単位変換: 断面計算から取得したデータは M:[N·mm] → [kNm] (×10⁻⁶)
            var pts = new List<(double theta, double moment)>(th.Count);
            for (int i = 0; i < th.Count; i++)
                pts.Add((th[i], mmRaw[i] * 1e-6));
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
        //{
        //    //var copy = (PileBodyInput)this.MemberwiseClone();
        //    //copy.PileBodySegments = new ObservableCollection<PileBodySegment>(this.PileBodySegments.Select(segment => segment.DeepCopy()));
        //    //copy.PileTop = this.PileTop.DeepCopy();
        //    //return copy;

        //    // shallow copy
        //    var copy = (PileBodyInput)this.MemberwiseClone();

        //    // まず PileBodyType を明示的に反映（プロパティ経由で）
        //    // これにより後続の PileBodySegments セッターが正しい親タイプを参照できます
        //    copy.PileBodyType = this.PileBodyType;

        //    // 深いコピー：セグメント／杭頭を新しいコレクション／インスタンスで作成
        //    copy.PileBodySegments = new ObservableCollection<PileBodySegment>(
        //        this.PileBodySegments.Select(segment => segment.DeepCopy())
        //    );

        //    copy.PileTop = this.PileTop?.DeepCopy();

        //    // 必要ならプリセット名コレクションもコピー（安全措置）
        //    if (this.PileTipSettlementPresetParameterNames != null)
        //        copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>(this.PileTipSettlementPresetParameterNames);
        //    else
        //        copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>();

        //    // 他の参照型フィールドも必要に応じてコピーしてください

        //    return copy;
        //}
        //{
        //    // shallow copy
        //    var copy = (PileBodyInput)this.MemberwiseClone();

        //    // まず PileBodyType を明示的に反映（プロパティ経由で）
        //    copy.PileBodyType = this.PileBodyType;

        //    // 深いコピー：セグメント／杭頭を新しいコレクション／インスタンスで作成
        //    // セグションの ResetSectionProperties 呼び出しを抑止して割り当てる
        //    copy._suppressSectionReset = true;
        //    copy.PileBodySegments = new ObservableCollection<PileBodySegment>(
        //        this.PileBodySegments.Select(segment => segment.DeepCopy())
        //    );
        //    copy._suppressSectionReset = false;

        //    copy.PileTop = this.PileTop?.DeepCopy();

        //    if (this.PileTipSettlementPresetParameterNames != null)
        //        copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>(this.PileTipSettlementPresetParameterNames);
        //    else
        //        copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>();

        //    return copy;
        //}
        {
            try
            {
                var copy = (PileBodyInput)this.MemberwiseClone();
                copy.PileBodyType = this.PileBodyType;
                copy._suppressSectionReset = true;
                copy.PileBodySegments = new ObservableCollection<PileBodySegment>(
                    this.PileBodySegments.Select(segment => segment.DeepCopy())
                );
                copy._suppressSectionReset = false;
                copy.PileTop = this.PileTop?.DeepCopy();
                if (this.PileTipSettlementPresetParameterNames != null)
                    copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>(this.PileTipSettlementPresetParameterNames);
                else
                    copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>();
                return copy;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PileBodyInput.DeepCopy]");
                return null;
            }
        }
    }
}
