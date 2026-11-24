using PileDesign.Constants;
using Newtonsoft.Json;
using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows;

using Serilog;
using PileDesign.Services;
namespace PileDesign.Models.InputData
{
    public class PileBodyInput : BaseModel, IJsonOnDeserializing, IJsonOnDeserialized
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
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 4)
                    {
                        string name = parts[0].Trim();
                        string soilType = parts[1].Trim();
                        if (!PileDesign.Common.NumericText.TryParse(parts[2].Trim(), out double alpha) ||
                            !PileDesign.Common.NumericText.TryParse(parts[3].Trim(), out double n))
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

        // 子要素 (PileSection) への自動同期を抑止するフラグ。
        // - JSON デシリアライズ中: PileBodySegments が PileBodyType より先に loaded されるため、
        //   ここで子要素を触ると this.PileBodyType=null のまま既定値で上書き破壊してしまう
        // - DeepCopy 中: 子要素は既に正しい状態にあるため再同期不要
        private bool _suppressChildSync = false;

        // System.Text.Json 用コールバック (本プロジェクトの主デシリアライザ)
        void IJsonOnDeserializing.OnDeserializing() => _suppressChildSync = true;
        void IJsonOnDeserialized.OnDeserialized() => _suppressChildSync = false;

        // Newtonsoft.Json 用コールバック (副デシリアライザ経由でロードされる場合に備えて)
        [OnDeserializing]
        internal void OnDeserializingHandler(StreamingContext _) => _suppressChildSync = true;

        [OnDeserialized]
        internal void OnDeserializedHandler(StreamingContext _) => _suppressChildSync = false;

        private ObservableCollection<PileBodySegment> _pileBodySegments = null!;   // コンストラクタで入る
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
                SetProperty(ref _pileBodySegments!, value);

                // 新しいコレクションに購読を追加し、既存アイテムに親タイプを反映
                if (_pileBodySegments != null)
                {
                    _pileBodySegments.CollectionChanged += PileBodySegments_CollectionChanged;

                    // ロード中/DeepCopy 中は子要素に触らない (購読は維持)
                    if (_suppressChildSync) return;

                    foreach (var seg in _pileBodySegments)
                    {
                        if (seg?.PileSection != null)
                        {
                            // PileBodyTypeがnullや空文字の場合は既定値をセット
                            var safeType = string.IsNullOrWhiteSpace(this.PileBodyType)
                                ? PileBodyTypeOption.FirstOrDefault() ?? PileTypeNames.InsituRc
                                : this.PileBodyType;
                            seg.PileSection.PileBodyType = safeType;
                            seg.PileSection.ResetSectionProperties();
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

                UpdateNodularPileHeads();

                // 最下段区間の断面が変わると Hybrid ニーディングの節部径 D1 が変わり、
                // 根固め部径 D3 = e·D1 も変わる
                UpdateHybridDerivedDiameter();
            }
            catch (Exception ex)
            {
                // 画面へ直接ダイアログを出さない (理由は PileSection の同種の箇所を参照)
                PileDesign.Common.CalcFallbackTracker.Report(
                    "杭区間情報の更新（既定値で継続）", ex, $"杭体={PileBodyRef}");
            }
        }

        // ─── 鋼管杭の杭頭部 (l1・l2) ───────────────────────────────────────────
        //
        // 杭頭部の検討式 (合成剛性 EIeq、「基礎部材の強度と変形性能」図 8.3) の l1 (コンクリート充填部) と
        // l2 (充填部下端〜鋼管の断面変化部) は、杭体の上の 2 区間そのもの。2026-11-21 までは杭断面に
        // 別の入力 (CftFillLength・CftThickPartLength) として持ち、区間長と二重になっていた (利用者の指摘)。
        // いまは杭体ウィンドウで l1・l2 を入れると上の 2 区間を作り直し (SetHeadZone)、杭断面の値は
        // ここから配る (SyncHeadZoneSections)。l1・l2 は区間の長さから読む (別に覚えない)。

        /// <summary>充填部 (l1) の区間。無ければ null。</summary>
        internal PileBodySegment? HeadFillSegment => PileBodySegments?.FirstOrDefault(s => s?.HeadZoneRole == HeadZoneRole.Fill);

        /// <summary>充填部下端〜断面変化部 (l2) の区間。無ければ null。</summary>
        internal PileBodySegment? HeadThickPartSegment => PileBodySegments?.FirstOrDefault(s => s?.HeadZoneRole == HeadZoneRole.ThickPart);

        /// <summary>l1 (mm)。充填部の区間長。充填部が無ければ 0。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HeadFillLength => (HeadFillSegment?.SegmentLength ?? 0.0) * 1000.0;

        /// <summary>l2 (mm)。充填部下端〜断面変化部の区間長。無ければ 0。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HeadThickPartLength => (HeadThickPartSegment?.SegmentLength ?? 0.0) * 1000.0;

        /// <summary>杭頭部の杭径 D (mm)。充填部があればその断面、無ければ 1 区間目の断面の径。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HeadPileDiameter
            => (HeadFillSegment ?? PileBodySegments?.FirstOrDefault())?.PileSection?.PileDiameter ?? 0.0;

        /// <summary>杭頭部の鋼管の外径 (mm、腐食前)。図の注記に使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HeadPipeDiameter
            => (HeadFillSegment ?? PileBodySegments?.FirstOrDefault())?.PileSection?.PipeDia ?? 0.0;

        /// <summary>杭頭部 (l1・l2) の鋼管の肉厚 (mm、腐食前)。図の壁の厚さに使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HeadPipeThickness
            => (HeadFillSegment ?? PileBodySegments?.FirstOrDefault())?.PileSection?.PipeTs ?? 0.0;

        /// <summary>
        /// 断面変化部より下 (杭頭の区間のすぐ下の区間) の鋼管の肉厚 (mm、腐食前)。EI3 の鋼管。
        /// 下の区間が無ければ杭頭部と同じ。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double LowerPipeThickness
            => PileBodySegments?.FirstOrDefault(s => s?.HeadZoneRole == HeadZoneRole.None)?.PileSection?.PipeTs ?? HeadPipeThickness;

        /// <summary>
        /// l1・l2 (mm) を入れてよいか。だめなら理由 (画面の赤枠に出す)。
        /// 厚い鋼管の区間 l1 + l2 は 4D 以下 (検討式の範囲)。l2 は充填部の下端から測るので、充填部が無ければ 0。
        /// 杭の全長は変えない (下の区間で差を吸収する) ので、下に 1 m 以上の区間が残ること。
        /// </summary>
        internal string? ValidateHeadZone(double l1, double l2)
        {
            if (PileBodyType != PileTypeNames.SteelPipe) return "鋼管杭のときだけ入力できます";
            if (!double.IsFinite(l1) || !double.IsFinite(l2) || l1 < 0 || l2 < 0) return "0 以上の数を入れてください";
            if (l1 <= 0 && l2 > 0) return "充填部が無い (l1 = 0) ときは、l2 は 0 にしてください (l2 は充填部の下端から測ります)";
            double d = HeadPileDiameter;
            if (d > 0 && l1 + l2 > 4.0 * d + 1e-6)
                return $"l1 + l2 = {l1 + l2:N0} mm が 4D = {4.0 * d:N0} mm を超えています (厚い鋼管の区間は 4D 以下)";
            double total = (PileBodySegments?.Sum(s => s.SegmentLength) ?? 0.0) * 1000.0;
            double lower = total - l1 - l2;
            if (lower < 1000.0 - 1e-6)
                return $"杭の全長 {total:N0} mm のうち、l1 + l2 の下に 1,000 mm 以上の区間が残りません";
            return null;
        }

        /// <summary>
        /// l1・l2 (mm) に合わせて上の 2 区間を作り直す。杭の全長は変えず、その下の 1 つ目の区間の長さで差を吸収する。
        /// 入れられない値なら何も変えずに理由を返す (<see cref="ValidateHeadZone"/>)。
        /// 充填部の断面は、いまの充填部 (無ければ 1 区間目) の断面を写して「コンクリート充填鋼管部」にし、
        /// l2 の区間はその鋼管を写した「鋼管部」にする (<see cref="SyncHeadZoneSections"/>)。
        /// </summary>
        public string? SetHeadZone(double l1, double l2)
        {
            string? problem = ValidateHeadZone(l1, l2);
            if (problem != null) return problem;
            var segments = PileBodySegments!;

            var oldFill = HeadFillSegment;
            var template = (oldFill ?? HeadThickPartSegment ?? segments.FirstOrDefault(s => s.HeadZoneRole == HeadZoneRole.None) ?? segments[0]).PileSection;
            double oldHead = segments.Where(s => s.HeadZoneRole != HeadZoneRole.None).Sum(s => s.SegmentLength);
            foreach (var s in segments.Where(s => s.HeadZoneRole != HeadZoneRole.None).ToList()) segments.Remove(s);

            // 吸収する区間 (作った区間のすぐ下)。全長を保つ
            double newHead = (l1 + l2) / 1000.0;
            // 区間は一覧に入れてから断面を入れる (一覧に足すと断面が既定値に初期化される: PileBodySegments_CollectionChanged)
            if (segments.Count == 0)
            {
                var lowerSection = template.DeepCopy();
                lowerSection.PileSectionType = PileTypeNames.SteelPipeSection;
                var lower = new PileBodySegment { SegmentLength = 0.0 };
                segments.Add(lower);
                lower.PileSection = lowerSection;
            }
            segments[0].SegmentLength = Math.Round(segments[0].SegmentLength + oldHead - newHead, 9);

            int at = 0;
            if (l1 > 0)
            {
                var fill = new PileBodySegment { HeadZoneRole = HeadZoneRole.Fill, SegmentLength = l1 / 1000.0 };
                segments.Insert(at++, fill);
                fill.PileSection = template.DeepCopy();
            }
            if (l2 > 0)
            {
                var thick = new PileBodySegment { HeadZoneRole = HeadZoneRole.ThickPart, SegmentLength = l2 / 1000.0 };
                segments.Insert(at, thick);
                thick.PileSection = template.DeepCopy();
            }

            // 杭頭の区間より下に「コンクリート充填鋼管部」が残っていたら「鋼管部」にする (鋼管はそのまま)。
            // 1 区間だけの充填鋼管杭に l1・l2 を入れると、元の区間が下の区間として充填のまま残っていた (利用者の指摘 2026-11-22)。
            // 充填部は杭頭の区間だけ (図 8.3 の形)。残すと、l1 = 0 のときに読み直しで 1 区間目が充填部とみなされる (EnsureHeadZoneRoles)
            foreach (var s in segments.Where(s => s.HeadZoneRole == HeadZoneRole.None && s.PileSection?.PileSectionType == PileTypeNames.CftSection))
            {
                var pipe = s.PileSection.DeepCopy();
                pipe.PileSectionType = PileTypeNames.SteelPipeSection;
                pipe.CftFillLength = 0.0;
                pipe.CftThickPartLength = 0.0;
                s.PileSection = pipe;
            }

            SyncHeadZoneSections();
            PileBodySegmentsUpdate();
            return null;
        }

        /// <summary>
        /// 杭頭部の区間の断面をそろえ、杭断面の l1・l2 (合成剛性 EIeq と節点に使う値) を配る。
        /// 充填部は「コンクリート充填鋼管部」、l2 の区間は充填部の断面を写した「鋼管部」(鋼管は同じ)。
        /// 充填部の断面 (鋼管・コンクリート) を杭断面ウィンドウで変えたら呼ぶ (l2 の区間が追随する)。
        /// </summary>
        public void SyncHeadZoneSections()
        {
            if (PileBodySegments == null) return;
            if (PileBodyType != PileTypeNames.SteelPipe)
            {
                foreach (var s in PileBodySegments) if (s != null) s.HeadZoneRole = HeadZoneRole.None;
                return;
            }
            var fill = HeadFillSegment;
            var thick = HeadThickPartSegment;
            if (fill?.PileSection != null)
            {
                if (fill.PileSection.PileSectionType != PileTypeNames.CftSection) fill.PileSection.PileSectionType = PileTypeNames.CftSection;
                fill.PileSection.CftFillLength = HeadFillLength;
                fill.PileSection.CftThickPartLength = HeadThickPartLength;
            }
            // 写すのは鋼管が違うときだけ (毎回作り直すと、配るたびに断面が別の実体になり曲線の控えも捨てる)
            if (thick != null && fill?.PileSection != null
                && !(thick.PileSection?.PileSectionType == PileTypeNames.SteelPipeSection && SamePipe(fill.PileSection, thick.PileSection)))
            {
                var copy = fill.PileSection.DeepCopy();
                copy.PileSectionType = PileTypeNames.SteelPipeSection;
                copy.CftFillLength = 0.0;
                copy.CftThickPartLength = 0.0;
                thick.PileSection = copy;
            }
        }

        /// <summary>
        /// 杭頭部の区間の印が無い鋼管杭 (この仕組みより前のファイル・計算例) に印を付ける。
        /// 1 区間目が「コンクリート充填鋼管部」なら充填部 (l1 = その区間長)、そのすぐ下が同じ鋼管
        /// (径・肉厚・規格・腐食) の「鋼管部」なら l2 の区間とする。区間の長さ・断面は変えない。
        /// </summary>
        public void EnsureHeadZoneRoles()
        {
            var segments = PileBodySegments;
            if (segments == null || segments.Count == 0) return;
            if (PileBodyType != PileTypeNames.SteelPipe || segments.Any(s => s?.HeadZoneRole != HeadZoneRole.None))
            {
                SyncHeadZoneSections();
                return;
            }
            if (segments[0]?.PileSection?.PileSectionType != PileTypeNames.CftSection) return;
            segments[0].HeadZoneRole = HeadZoneRole.Fill;
            if (segments.Count >= 3 && SamePipe(segments[0].PileSection, segments[1].PileSection)
                && segments[1].PileSection.PileSectionType == PileTypeNames.SteelPipeSection)
                segments[1].HeadZoneRole = HeadZoneRole.ThickPart;
            SyncHeadZoneSections();
        }

        private static bool SamePipe(PileSection? a, PileSection? b)
            // 製品名ではなく、計算に使う数値 (径・肉厚・規格・腐食) で比べる (名前の無い断面どうしを同じとみなさない)
            => a != null && b != null
               && a.PipeDia == b.PipeDia && a.PipeTs == b.PipeTs
               && a.PipeGrade == b.PipeGrade
               && a.CorrosionDepth == b.CorrosionDepth;

        /// <summary>
        /// 節杭 の杭頭タイプ（標準 / 拡頭中間径 / 拡頭）を、直上の杭区間の径から自動決定する。
        ///
        /// 継手で接合する以上、節杭の杭頭径は直上区間の径と一致していなければならない。
        /// 区間構成や断面が変わるたびに呼ぶ必要があるため、区間更新の一部として実行する。
        /// </summary>
        public void UpdateNodularPileHeads()
        {
            if (PileBodySegments == null) return;

            for (int i = 0; i < PileBodySegments.Count; ++i)
            {
                var section = PileBodySegments[i].PileSection;
                if (section == null) continue;

                // 最上段では節杭を選べないようにする (断面タイプの選択肢から除外される)
                section.IsTopSegment = i == 0;

                double? diameterAbove = i > 0
                    ? PileBodySegments[i - 1].PileSection?.PileDiameter
                    : null;

                section.ResolveNodularHead(diameterAbove);
            }
        }

        private string _pileBodyRef = null!;   // コンストラクタで既定値が入る
        public string PileBodyRef
        {
            get => _pileBodyRef;
            set => SetProperty(ref _pileBodyRef, value);
        }

        public static ObservableCollection<string> PileBodyTypeOption { get; } =
        [
            PileTypeNames.InsituRc,
            PileTypeNames.InsituSteelPipeConcrete,
            PileTypeNames.PrecastConcrete,
            PileTypeNames.SteelPipe
        ];

        private string _pileConstructionType = null!;   // コンストラクタで既定値が入る
        public string PileConstructionType
        {
            get => _pileConstructionType;
            set
            {
                if (SetProperty(ref _pileConstructionType, value)) UpdateHybridDerivedDiameter();
            }
        }

        // 施工タイプオプション
        private ObservableCollection<string> _pileConstructionTypeOption = null!;   // コンストラクタで入る
        public ObservableCollection<string> PileConstructionTypeOption
        {
            get => _pileConstructionTypeOption;
            set => SetProperty(ref _pileConstructionTypeOption, value);
        }

        public static ObservableCollection<string> InsituPileConstructionTypeOption { get; } =
        [PileConstructionTypeNames.Insitu];

        // メーカー別の高支持力杭工法は、そのメーカーの既製コンクリート杭に限って適用する。
        //   Smart-MAGNUM     … ジャパンパイル
        //   Hybridニーディング … 三谷セキサン
        // Smart-MAGNUM のカタログは適用杭種に鋼管杭も挙げているが、鋼管杭では選べないようにしている。
        public static ObservableCollection<string> PrecastPileConstructionTypeOption { get; } =
        [
            PileConstructionTypeNames.Preboring,
            PileConstructionTypeNames.Chubori,
            PileConstructionTypeNames.Driven,
            PileConstructionTypeNames.SmartMagnum,
            PileConstructionTypeNames.HybridKneading
        ];

        public static ObservableCollection<string> SteelPileConstructionTypeOption { get; } =
        [
            PileConstructionTypeNames.Preboring,
            PileConstructionTypeNames.Chubori,
            PileConstructionTypeNames.Rotary
        ];

        // 杭頭タイプ
        private string _pileTopType = null!;   // コンストラクタで既定値が入る
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
            "FT-Pile構法",
            "キャプリングパイル工法"
        ];

        // 鋼管杭　杭頭タイプオプション
        public static ObservableCollection<string> SteelPileTopTypeOption { get; } =
        [
            "鉄筋定着工法",
            "キャプリングパイル工法"
        ];

        // 杭頭タイプオプション
        private ObservableCollection<string> _pileTopTypeOption = null!;   // コンストラクタで入る
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

        // ─── Smart-MAGNUM 工法（ジャパンパイル）専用入力 ───
        // 拡大根固め部径 Den は既存の PileToeDia を流用する（埋込み杭では UI ラベルが既に
        // 「根固め部径d」で、姿図の横スケールと 3D の地盤柱径もこれを見ているため）。
        // 節部径 Don / Dos は入力せず杭区間の断面（節杭なら節部径、それ以外は公称外径）から導出する。

        // 周面強化型か（false = 標準型）。周面摩擦力係数 β・γ が切り替わる。
        private bool _smartMagnumIsReinforcedCircum;
        public bool SmartMagnumIsReinforcedCircum
        {
            get => _smartMagnumIsReinforcedCircum;
            set => SetProperty(ref _smartMagnumIsReinforcedCircum, value);
        }

        // 杭下拡大根固め部長さ LL (m)。杭先端から下方への長さ。適用範囲 0〜2m。
        private double _smartMagnumLL = 1.0;
        public double SmartMagnumLL
        {
            get => _smartMagnumLL;
            set
            {
                if (!double.IsFinite(value)) return;
                SetProperty(ref _smartMagnumLL, value);
            }
        }

        // 杭周面部の掘削径 Des (mm)。0 は「拡大掘削なし」を意味し、拡大比 ωs = 1.0 となる。
        private double _smartMagnumDes;
        public double SmartMagnumDes
        {
            get => _smartMagnumDes;
            set
            {
                if (!double.IsFinite(value)) return;
                SetProperty(ref _smartMagnumDes, value);
            }
        }

        // 拡翼掘削部長さ (m)。カタログの計算例と同じく杭下拡大根固め部長さ LL を含む長さで、
        // 杭下拡大根固め部の下端（杭先端から下方 LL）を起点に上方へ測る。
        // この範囲に入る杭区間だけ周面摩擦に ωs が掛かり、範囲外は ωs = 1.0 とする。
        private double _smartMagnumWingLength;
        public double SmartMagnumWingLength
        {
            get => _smartMagnumWingLength;
            set
            {
                if (!double.IsFinite(value)) return;
                SetProperty(ref _smartMagnumWingLength, value);
            }
        }

        // ─── Hybrid ニーディング工法（三谷セキサン）専用入力 ───
        // 根固め部径 D3 は設計拡径比 e と節部径 D1 から D3 = e·D1 と決まるため入力しない
        // （UpdatePileProperties が PileToeDia に導出値を書き戻し、姿図・3D・地盤柱がそれに従う）。

        // 設計拡径比 e（根固め部径の節部径に対する比）。1.0〜2.0 の 0.1 刻み。
        private double _hybridExpansionRatio = 1.5;
        public double HybridExpansionRatio
        {
            get => _hybridExpansionRatio;
            set
            {
                if (!double.IsFinite(value)) return;
                if (SetProperty(ref _hybridExpansionRatio, value)) UpdateHybridDerivedDiameter();
            }
        }

        /// <summary>
        /// 節杭の節部径 D1 [mm]。杭体最下段区間の断面から導出する
        /// （節杭は節部径、ストレート杭は公称外径）。Hybrid ニーディングの
        /// 先端有効断面積 Ap = π·D1²/4、根固め部径 D3 = e·D1、杭周固定部の掘削径 es·D1 の基準になる。
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public double HybridD1
        {
            get
            {
                var section = PileBodySegments is { Count: > 0 } ? PileBodySegments[^1].PileSection : null;
                if (section == null) return 0;
                return section.IsNodularPile ? section.NodeDiameter : section.NominalPileDiameter;
            }
        }

        /// <summary>根固め部径 D3 = e·D1 [mm]（導出値）。</summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public double HybridD3 => HybridExpansionRatio * HybridD1;

        /// <summary>
        /// Hybrid ニーディングの導出値（根固め部径 D3）を <see cref="PileToeDia"/> に反映する。
        ///
        /// 姿図・3D・地盤柱・CAD 出力はいずれも PileToeDia を見るため、ここで書き戻して
        /// 下流の消費側に工法分岐を持ち込まない。設計拡径比や杭区間の断面が変わった時点で
        /// 即座に反映されないと、杭体ウィンドウの表示が古い径のまま残ってしまう
        /// （解析側では SoilPile.UpdatePileProperties でも同じ書き戻しを行う）。
        /// </summary>
        public void UpdateHybridDerivedDiameter()
        {
            OnPropertyChanged(nameof(HybridD1));
            OnPropertyChanged(nameof(HybridD3));

            if (!PileConstructionTypeNames.IsHybridKneading(PileConstructionType)) return;

            double d3 = HybridD3;
            if (d3 > 0) PileToeDia = d3;
        }

        // 設計掘削径比 es（杭周固定部の掘削径の節部径に対する比）。
        // e ≤ 1.6 のとき 1.0〜2.0、e ≥ 1.7 のとき 1.0〜1.6。かつ es ≤ e。
        private double _hybridExcavationRatio = 1.0;
        public double HybridExcavationRatio
        {
            get => _hybridExcavationRatio;
            set
            {
                if (!double.IsFinite(value)) return;
                SetProperty(ref _hybridExcavationRatio, value);
            }
        }

        // 摩擦強化型か（false = 標準型）。節杭区間の周面摩擦力係数 β・γ が切り替わる。
        // カタログでは標準型と摩擦強化型の併用はしない。
        private bool _hybridIsFrictionEnhanced;
        public bool HybridIsFrictionEnhanced
        {
            get => _hybridIsFrictionEnhanced;
            set => SetProperty(ref _hybridIsFrictionEnhanced, value);
        }

        // 杭下長 Lu (m)。先端支持力算定位置から杭先端までの長さで、
        // この区間は杭周面摩擦力の対象外になる。
        // カタログは「節部径・拡径比によって異なります。詳細についてはお問い合わせください」
        // として値を公表していないため入力とする。
        private double _hybridPileBelowLength;
        public double HybridPileBelowLength
        {
            get => _hybridPileBelowLength;
            set
            {
                if (!double.IsFinite(value)) return;
                SetProperty(ref _hybridPileBelowLength, value);
            }
        }

        // 根固め部内の最上部節部の高さ (m、杭先端から上を正)。0 は未入力。
        // 引抜き方向の杭先端平均 N 値は「基礎杭の根固め部内の最上部節部より上方に 4D1 区間」
        // (カタログ p.7・p.8 の平均 N 値算出区間の図) で数える。節の位置は製品の節の間隔と根固め部の長さで決まり、
        // 杭断面からは求まらないので入力とする (2026-11-23 利用者の判断)。
        // 以前は先端支持力算定位置 (杭先端の Lu 上) から上方 4D1 で数えていた。未入力 (0) のときは従来どおり。
        private double _hybridTopNodeHeight;
        public double HybridTopNodeHeight
        {
            get => _hybridTopNodeHeight;
            set
            {
                if (!double.IsFinite(value) || value < 0) return;
                SetProperty(ref _hybridTopNodeHeight, value);
            }
        }

        // 杭先端閉塞性
        private double _tipNonPermability;
        public double TipNonPermability
        {
            get => _tipNonPermability;
            set => SetProperty(ref _tipNonPermability, value);
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<string> TipStyleOption { get; } =
        ["開端杭", "閉端杭",];

        // 杭先端スタイル
        private string _pileTipStyle = null!;   // コンストラクタで既定値が入る
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


        // 沈下の α・N の文字列版は持たない。画面は数値の SettleAlpha / SettleN を
        // 直接 Binding しており、文字列版は入れるだけで誰も読まなかった (2026-09-19 に撤去)。

        private double _settleN;
        public double SettleN
        {
            get => _settleN;
            set => SetProperty(ref _settleN, value);
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

        /// <summary>
        /// 杭頭部の入力 (パイルキャップ・定着筋・半剛接合 3 工法の選択)。
        /// 以前は公開フィールドで、保存の設定 (フィールドを含めない) では書き出されず、開き直すと既定値に戻っていた
        /// (キャプテンパイルの PC リングが無くなって杭頭が剛接合で解かれ、杭頭の N-M は既定の D41×16 で検定された)。
        /// Undo の写しはフィールドも含めるので、保存してから開き直すまで気づけなかった。
        /// </summary>
        public PileTop PileTop { get; set; } = new();

        /// <summary>杭頭部の、最上段の杭断面から導く値を入れ直す (<see cref="PileTop.SyncFromTopSection"/>)。</summary>
        internal void SyncPileTopFromTopSection()
            => PileTop?.SyncFromTopSection(PileBodyType, PileBodySegments?.FirstOrDefault()?.PileSection);

        //public string PileBodyType { get; set; }
        private string _pileBodyType = null!;   // コンストラクタで既定値が入る
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
                    PileDesign.Common.CalcFallbackTracker.Report(
                        "杭体タイプの設定（既定値で継続）", ex, $"杭体={PileBodyRef}");
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
                if (_suppressChildSync) return;
                if (e.NewItems == null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is PileBodySegment seg && seg.PileSection != null)
                    {
                        seg.PileSection.PileBodyType = this.PileBodyType;
                        seg.PileSection.ResetSectionProperties();
                    }
                }
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report(
                    "杭区間コレクションの変更（既定値で継続）", ex, $"杭体={PileBodyRef}");
            }
        }

        /// <summary>
        /// 半剛接合の杭頭モデルへ、パイルキャップのコンクリートと杭体の最上段の断面の値を配る。
        /// 杭頭モデルは「まだ無ければ作る」形でしか生成されず、入力を変えても届かない値があったので、
        /// 解析の入口・例題の読込・杭頭部ウィンドウから呼ぶ。変わっていなければ何もしない。
        /// <list type="bullet">
        /// <item>パイルキャップの Fc・γ … コンストラクタ引数でしか入らなかった</item>
        /// <item>FT-Pile の杭径 … 画面の操作でしか入らず、読込直後は既定の φ600</item>
        /// <item>キャプテンパイルの杭体の Fc・Ec … σmax の Fc (杭体・パイルキャップの小さい方) と Kp の Ep (2026-11-24)</item>
        /// </list>
        /// </summary>
        internal void ApplyTopSectionToPileTop()
        {
            PileTop?.ApplyPileCapConcrete();
            var topSection = PileBodySegments?.FirstOrDefault()?.PileSection;
            if (topSection == null || PileTop == null) return;
            PileTop.FTPile?.SetDimensionsFromSection(topSection.PileDiameter, topSection.ConcreteThickness);
            PileTop.CaptainPile?.SetPileBodyConcrete(topSection.ConcreteFc, topSection.ConcreteE);
            if (PileTop.CapringPile is { } capring) ApplyTopSectionToCapring(capring, topSection, PileBodyType);
        }

        /// <summary>
        /// キャプリングパイル工法の杭頭モデルへ、最上段の杭断面の値を配る。変わっていなければ何もしない。
        /// <list type="bullet">
        /// <item>鋼管杭かどうかと管厚 … 合成 EI (Es·I_pipe + Ec·I_fill) に効く</item>
        /// <item>杭体の Ep と EI … Kp = Ep·Ip/Hp。以前は渡っておらず、パイルキャップの Ec と杭径の中実円を使っていた (2026-11-25)</item>
        /// </list>
        /// 杭頭部ウィンドウも同じものを呼ぶ。
        /// </summary>
        internal static void ApplyTopSectionToCapring(CapringPile capring, PileSection? topSection, string? pileBodyType)
        {
            bool isSteelPipe = (pileBodyType ?? "").Contains(PileTypeNames.SteelPipe);
            capring.SetSteelPipeFromSection(isSteelPipe, topSection?.PipeTs ?? 0.0);
            if (topSection == null) return;
            // EI は kN·m² (解析が使う値。鋼管は腐食後)。N·mm² に直す
            double ep = isSteelPipe ? topSection.PipeEs : topSection.ConcreteE;
            capring.SetPileBodyFromSection(ep, topSection.EI * 1e9);
        }

        /// <summary>
        /// 杭頭回転角 (杭頭の M-θ) を基本設定「場所打ち杭・鋼管杭の杭頭回転角」で考慮するか選べる杭頭か。
        /// 鉄筋定着工法の場所打ちRC杭 (トリリニア) と鋼管杭 (線形の Kθ)。<see cref="GetMThetaRelationship"/> の分岐と同じ。
        /// </summary>
        internal bool HasSelectablePileHeadRotation()
            => (PileTopType ?? "").Contains("鉄筋定着工法")
               && ((PileBodyType ?? "").Contains(PileTypeNames.InsituRc) || (PileBodyType ?? "").Contains(PileTypeNames.SteelPipe))
               && !(PileBodyType ?? "").Contains(PileTypeNames.InsituSteelPipeConcrete);

        /// <summary>選べる杭頭のうち、M-θ が非線形 (トリリニア) のもの (鉄筋定着工法の場所打ちRC杭)。</summary>
        internal bool HasNonlinearSelectablePileHeadRotation()
            => HasSelectablePileHeadRotation() && (PileBodyType ?? "").Contains(PileTypeNames.InsituRc);

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

            // 半剛接合の 3 工法は、既にできている杭頭モデルを使い回す。入力を変えても
            // 届かない値があったので、ここで揃える。変わっていなければ何もしない。
            //  - パイルキャップの Fc・γ … コンストラクタ引数でしか入らなかった
            //  - FT-Pile の杭径 … 画面の操作でしか入らず、読込直後は既定の φ600
            ApplyTopSectionToPileTop();

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
                    catch (Exception ex)
                    {
                        // M-θ が作れなければ、最後の Rigid() (安全側) まで落ちる。
                        Serilog.Log.Warning(ex,
                            "[GetMThetaRelationship] キャプテンパイル工法の M-θ を作れませんでした (N={AxialN} kN)", axialN);
                    }
                }
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
                    catch (Exception ex)
                    {
                        // M-θ が作れなければ、最後の Rigid() (安全側) まで落ちる。
                        Serilog.Log.Warning(ex,
                            "[GetMThetaRelationship] FT-Pile構法の M-θ を作れませんでした (N={AxialN} kN)", axialN);
                    }
                }
            }

            // 2.5) キャプリングパイル工法 → PileTop.CapringPile の M-θ を採用
            if (pileTopType.Contains("キャプリングパイル工法"))
            {
                // PileTopWindow を開かずに F9 で解析開始した場合 CapringPile が未生成のため自動初期化
                if (PileTop != null && PileTop.CapringPile == null)
                {
                    var newCp = new CapringPile(PileTop.PileCapEc)
                    {
                        PileBodyType = pileBodyType,
                        PileCapFc = PileTop.PileCapFc,
                        PileCapEc = PileTop.PileCapEc,
                    };
                    newCp.LoadPCRingOptions();
                    newCp.LoadTensionBarOptions();
                    // 最上部杭区間から杭径を取得し PCリングを自動選定
                    var topSec = PileBodySegments?.FirstOrDefault()?.PileSection;
                    if (topSec != null)
                    {
                        bool isSp = pileBodyType.Contains(PileTypeNames.SteelPipe);
                        double dia = (isSp && topSec.PipeDia > 0) ? topSec.PipeDia : topSec.PileDiameter;
                        if (dia > 0)
                        {
                            int targetSize = (int)Math.Ceiling(dia / 50.0) * 50;
                            if (targetSize < 300) targetSize = 300;
                            if (targetSize > 1200) targetSize = 1200;
                            var ring = newCp.PCRings.FirstOrDefault(r => r.Name == $"{targetSize}N")
                                    ?? newCp.PCRings.FirstOrDefault(r => (r.Name ?? "").EndsWith("N") && r.D >= dia)
                                    ?? newCp.PCRings.FirstOrDefault();
                            if (ring != null)
                            {
                                newCp.PCRing = ring;
                                newCp.D = ring.D;
                                newCp.SelectedPCRingName = ring.Name ?? "";
                            }
                            if (isSp)
                            {
                                newCp.IsConcreteFilledSteelPipe = true;
                                newCp.SteelPipeWallThickness = topSec.PipeTs;
                            }
                        }
                    }
                    newCp.Update();
                    PileTop.CapringPile = newCp;
                    if (newCp.PCRing != null)
                        PileTop.SelectedPileTopSpecification = newCp.GetCombinedSpecs();
                    Serilog.Log.Debug(
                        $"[GetMThetaRelationship] CapringPile 自動初期化: D={newCp.D:F0}, Ke={newCp.Ke:E3}, IsCFSP={newCp.IsConcreteFilledSteelPipe}");
                }

                var caprObj = PileTop?.CapringPile;
                if (caprObj is CapringPile cpr)
                {
                    // 鋼管厚・杭体の Ep と EI は杭断面が持つ。PC リングの自動選定でしか入らなかったり
                    // (鋼管厚)、まったく渡っていなかったり (Ep・EI) した。ここで揃える (上で自動初期化した場合も)。
                    ApplyTopSectionToCapring(cpr, PileBodySegments?.FirstOrDefault()?.PileSection, pileBodyType);

                    try
                    {
                        // axialN は kN、CapringPile は N を期待するため×1000
                        var (thetasObs, msObs) = cpr.GetMThetaRelationship(axialN * 1000);
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
                    catch (Exception ex)
                    {
                        // M-θ が作れなければ、最後の Rigid() (安全側) まで落ちる。
                        Serilog.Log.Warning(ex,
                            "[GetMThetaRelationship] キャプリングパイル工法の M-θ を作れませんでした (N={AxialN} kN)", axialN);
                    }
                }
            }

            // 3) 定着筋方式/埋込み方式 → 完全剛
            if (pileTopType.Contains("定着筋方式") || pileTopType.Contains("埋込み方式"))
            {
                return PileHeadRotationDef.Rigid();
            }

            // 4) 鉄筋定着工法
            if (pileTopType.Contains("鉄筋定着工法"))
            {
                // 場所打ち鋼管コンクリート杭 / 既製コンクリート杭 → 完全剛
                // (杭頭部 RC 円形 NM は PileTop 側 (PileTop.GetNMRaw) で 2 段断面評価。
                //  回転ばね M-θ は剛で良い)
                if (pileBodyType.Contains(PileTypeNames.InsituSteelPipeConcrete)
                    || pileBodyType.Contains(PileTypeNames.PrecastConcrete))
                {
                    return PileHeadRotationDef.Rigid();
                }

                // 鋼管杭 + 鉄筋定着工法 → 接合部の弾性回転剛性 Kθ を線形ばねとして反映
                // (PileTop.GetSteelPilePileHeadJointKtheta で算定。降伏後挙動は考慮しない
                //  純粋な線形ばね = 折線でない。必須入力欠落時は Rigid フォールバック)
                if (pileBodyType.Contains(PileTypeNames.SteelPipe))
                {
                    if (this.PileTop != null)
                    {
                        try
                        {
                            double? ktheta = this.PileTop.GetSteelPilePileHeadJointKtheta(axialN);
                            if (ktheta.HasValue && ktheta.Value > 0.0 && double.IsFinite(ktheta.Value))
                            {
                                Serilog.Log.Debug(
                                    $"[GetMThetaRelationship] 鋼管杭+鉄筋定着工法: " +
                                    $"axialN={axialN:F1} kN, Kθ={ktheta.Value:E3} kN·m/rad");
                                return PileHeadRotationDef.CombinedLinear(ktheta.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Debug(
                                $"[GetMThetaRelationship] 鋼管杭+鉄筋定着工法 例外: {ex.Message}");
                        }
                    }
                    Serilog.Log.Debug(
                        $"[GetMThetaRelationship] 鋼管杭+鉄筋定着工法 フォールバック → Rigid()");
                    return PileHeadRotationDef.Rigid();
                }

                // 場所打ち鉄筋コンクリート杭 → InsituReinforcedConcreteSection.GetMThetaRelationship() を直接呼ぶ
                if (pileBodyType.Contains(PileTypeNames.InsituRc))
                {
                    var pileSection = PileBodySegments?.FirstOrDefault()?.PileSection;
                    if (pileSection != null)
                    {
                        // 基礎指針: 軸応力度 σ0 = N/Ae が -Ft ≤ σ0 ≤ (1/4)·ξ·Fc の範囲でのみ
                        // M-θ を考慮する。範囲外 (強引張 or 高圧縮) は杭頭を剛結として検討。
                        if (!pileSection.IsWithinMThetaValidAxialRange(axialN))
                        {
                            Serilog.Log.Debug(
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

                                Serilog.Log.Debug(
                                    $"[GetMThetaRelationship] 場所打ちRC杭: points={pts.Count}, " +
                                    $"θ=[{string.Join(",", thetas.Select(t => t.ToString("E3")))}], " +
                                    $"M=[{string.Join(",", ms.Select(m => m.ToString("F1")))}], " +
                                    $"Mcr={mcrKNm?.ToString("F1") ?? "null"} kNm");
                                return PileHeadRotationDef.Combined(new MomentRotationCurve(pts), mcrKNm);
                            }
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Debug(
                                $"[GetMThetaRelationship] 場所打ちRC杭 例外: {ex.Message}");
                        }
                    }

                    Serilog.Log.Debug(
                        $"[GetMThetaRelationship] 場所打ちRC杭 フォールバック → Rigid()");
                    return PileHeadRotationDef.Rigid();
                }

                // 場所打ち鉄筋コンクリート杭以外の鉄筋定着工法も剛結とする
                return PileHeadRotationDef.Rigid();
            }

            // ===== どの杭頭タイプにも当てはまらなかった =====
            //
            // 杭頭タイプごとの分岐は上でぜんぶ尽くしている。ここへ来るのは
            // 「杭頭タイプを増やしたが、その M-θ をここに書いていない」ときで、
            // そのときは<b>安全側として剛結</b>にする (モーメントを完全に伝達する)。
            //
            // 以前はここに「PileTop に IsRigidHead / MthetaXY_ByN / KthetaXY といった
            // プロパティがあれば使う」という反射のフォールバックが並んでいたが、
            // PileTop にその名前のプロパティは<b>ひとつも無い</b>。つまり必ず素通りして
            // この Rigid() に来ており、読む人には「汎用の受け皿がある」ように見えていた。
            // 杭頭タイプを足すときの受け皿は反射ではなく、上の分岐に 1 つ足すこと。
            Serilog.Log.Debug(
                $"[GetMThetaRelationship] 杭頭タイプに対応する M-θ が無い "
                + $"(PileTopType='{pileTopType}', PileBodyType='{pileBodyType}') → Rigid() (安全側)");
            return PileHeadRotationDef.Rigid();
        }

        /// <summary>
        /// 杭体の画面で編集を始めたときの杭体番号 (1 から)。画面を開いてから足した杭体は 0。保存しない。
        ///
        /// <para>画面の中で杭体を消す・元に戻すと、並び順 (= 杭体番号) が変わる。杭配置の杭体番号は、
        /// 画面を OK で閉じたときにこれを手掛かりに付け直す (<c>PileBodyViewModel.RenumberPileLayout</c>)。
        /// 複製 (<see cref="DeepCopy"/>) はメンバーの写しから始めるので、元に戻したあとも残る。</para>
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        internal int NoAtEditStart { get; set; }

        // 深いコピーを作成するメソッド
        public PileBodyInput DeepCopy()
        {
            // 複製できなければ例外のまま知らせる (欠けた写しを返さない)。以前は例外を握り潰して null を返しており、
            // 呼び出し側 (元に戻す操作の控え・履歴の写し・単杭沈下の画面の控え) はそれを杭体の一覧へそのまま入れるので、
            // 戻したときに杭体が null になって別の場所で落ちた。InputModel.CopyForHistory と同じ方針
            // (履歴の写しは TryCopyForHistory が受けて、その操作を記録しないで知らせる)
            var copy = CloneWithoutSubscribers<PileBodyInput>();
            copy.PileBodyType = this.PileBodyType;
            copy._suppressChildSync = true;
            copy.PileBodySegments = new ObservableCollection<PileBodySegment>(
                this.PileBodySegments.Select(segment => segment.DeepCopy())
            );
            copy._suppressChildSync = false;
            copy.PileTop = this.PileTop?.DeepCopy()!;   // 元が null なら null のまま写す
            if (this.PileTipSettlementPresetParameterNames != null)
                copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>(this.PileTipSettlementPresetParameterNames);
            else
                copy.PileTipSettlementPresetParameterNames = new ObservableCollection<string>();
            return copy;
        }

        /// <summary>
        /// この杭体入力のスカラー項目を <paramref name="other"/> の値へ戻す。
        /// 沈下ウィンドウのキャンセルが使う。
        ///
        /// <para><b>戻す項目を手で並べない。</b>並べると、後から沈下側の入力が増えたときに
        /// ここへ足すのを忘れ、キャンセルしたのに値が残る。杭断面ウィンドウで実際に起きた形
        /// (<see cref="PileSection.RestoreFrom"/> の経緯も同じ)。</para>
        ///
        /// <para>フィールドを直に写す。セッター経由だと子要素の同期 (<c>_suppressChildSync</c> が
        /// 抑えている類) が走るが、戻すのは<b>もともと過去の姿</b>なので同期は要らない。</para>
        ///
        /// <para>参照型 (セグメント・杭頭・選択肢のコレクション) は写さない。沈下ウィンドウは
        /// そこを編集しないうえ、写すとコレクションの実体がスナップショット側と入れ替わり、
        /// 他所が持っている参照 (SoilPile 経由や保存グラフの $ref) が外れてしまう。</para>
        /// </summary>
        internal void RestoreScalarsFrom(PileBodyInput other)
        {
            if (other == null) return;

            PileDesign.Common.ModelRestore.CopyScalarFields(this, other);

            // 一時的な抑止フラグだけは過去の値ではなく「いま抑止していない」が正しい。
            _suppressChildSync = false;

            // 画面へ全プロパティの変更を知らせる (空文字は「全部」の意味)
            OnPropertyChanged(string.Empty);
        }
    }
}
