using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models
{
    /// <summary>
    /// 開先付き異形棒鋼 NewJ-BAR を杭頭補強筋として鋼管に溶接する杭頭接合部。
    ///
    /// 出典は「開先付き異形棒鋼 NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)」(株式会社ブレイブ・株式会社クラウン、2026 年 9 月改訂)。
    /// NewJ-BAR は材料の大臣認定 (MSRB-0118 ほか) で、工法の認定ではない。マニュアルは杭頭部の断面検定の考え方を示す。
    /// <list type="bullet">
    /// <item>杭頭部は NewJ-BAR を主筋とする鉄筋コンクリートの仮想円形断面とみなす (4.3.1)。
    ///   外径は杭径 250 以上 500 未満で D + 200、500 以上で D + 2·d + 200 (d は補強筋の呼び名の数値)。
    ///   コンクリートはフーチングの設計基準強度。</item>
    /// <item>短期の許容曲げモーメントは RC 規準の円形断面の許容応力度式 (4.4.3)。
    ///   引張側のコンクリートは無視し、圧縮側は主筋をヤング係数比 n 倍した等価断面。主筋 8 本以上は円環、8 本未満は弱軸方向。</item>
    /// <item>終局曲げモーメントは層分割法 (4.4.4)。圧縮縁のひずみ 0.003 (または引張主筋のひずみ 0.2) を終局とし、
    ///   コンクリートは Ec の直線のあと Fc で頭打ち、主筋は Er = 2.05×10⁵ の直線のあと終局強度で頭打ち
    ///   (WSD490 は 490、WSD390 は 1.1×390)。主筋位置のコンクリートは差し引く。</item>
    /// <item>せん断は杭頭の埋込み部側面の支圧で、Qa = D·L·fcc (fcc = 2×(2/3)·Fc)、終局は 1.5 倍 (4.3.1 2)・(4.7) 式)。</item>
    /// </list>
    /// 杭頭の回転ばねはマニュアルに無いので、水平解析では剛接合として扱う。
    ///
    /// 単位は入力の N が [N]、出力の M が [N·mm]、長さが [mm]、応力度が [N/mm²] (キャプテンパイル工法のクラスと同じ)。
    /// </summary>
    public class NewJBar : BaseModel
    {
        /// <summary>杭頭タイプの名前。「鉄筋定着工法」を含まないこと (Contains で分岐している箇所が多い)。</summary>
        public const string PileTopTypeName = "NewJ-BAR";

        /// <summary>主筋 (NewJ-BAR) のヤング係数 (N/mm²)。マニュアル 4.4.4.2。</summary>
        public const double Er = 2.05e5;
        /// <summary>コンクリートの終局ひずみ。マニュアル 4.4.4.2。</summary>
        public const double ConcreteUltimateStrain = 0.003;
        /// <summary>主筋の最大引張ひずみ (引張終局)。マニュアル 4.4.4 の計算方法 3)。</summary>
        public const double RebarUltimateStrain = 0.2;
        /// <summary>長期の許容引張・圧縮応力度 (N/mm²)。マニュアル表 3.1 (WSD390・WSD490 とも)。</summary>
        public const double LongTermAllowableStress = 195.0;
        /// <summary>終局のせん断抵抗は短期の支圧耐力の何倍か。マニュアル 4.3.1 解説 2)。</summary>
        public const double UltimateShearFactor = 1.5;
        /// <summary>定着長さの式の修正係数 S。マニュアル (4.6) 式。</summary>
        public const double AnchorageS = 1.25;
        /// <summary>主筋がこの本数以上なら円環に置き換える。未満は配置を考えて弱軸方向で求める。マニュアル 4.4.1。</summary>
        public const int RingMinimumBars = 8;
        /// <summary>主筋の配置角度を何等分して最小を取るか (π/m 〜 2π/m を 10 等分 = 11 箇所)。マニュアル 4.4.4.3。</summary>
        public const int WeakAxisDivisions = 10;
        /// <summary>仮想断面を何層に分けて積分するか (仮想径に対して 400 分割)。マニュアル 4.4.4.1 (6)。</summary>
        public const int StripCount = 400;
        /// <summary>円環を何点で表すか。</summary>
        private const int RingPoints = 360;

        // ───────── 選択肢 ─────────
        [System.Text.Json.Serialization.JsonIgnore]
        public static IReadOnlyList<string> GradeOptions { get; } = ["WSD390", "WSD490"];

        [System.Text.Json.Serialization.JsonIgnore]
        public static IReadOnlyList<string> SizeOptions { get; } = ["WD32J", "WD35J", "WD38J", "WD41J"];

        /// <summary>規格ごとの呼び名 (表 2.1)。WD41J は WSD490 だけ。</summary>
        public static bool IsAvailable(string? grade, string? size)
            => SizeOptions.Contains(size ?? "") && GradeOptions.Contains(grade ?? "")
               && !(size == "WD41J" && grade == "WSD390");

        // ───────── 入力 (保存する) ─────────
        private string _grade = "WSD390";
        /// <summary>種類の記号 (WSD390 / WSD490)。</summary>
        public string Grade
        {
            get => _grade;
            set { if (SetProperty(ref _grade, value ?? "WSD390")) OnDerivedChanged(); }
        }

        private string _size = "WD32J";
        /// <summary>呼び名 (WD32J / WD35J / WD38J / WD41J)。</summary>
        public string Size
        {
            get => _size;
            set { if (SetProperty(ref _size, value ?? "WD32J")) OnDerivedChanged(); }
        }

        private int _count = 8;
        /// <summary>杭 1 本あたりの本数。</summary>
        public int Count
        {
            get => _count;
            set { if (SetProperty(ref _count, Math.Max(0, value))) OnDerivedChanged(); }
        }

        private double _embedmentLength;
        /// <summary>
        /// 杭のフーチングへの埋込み深さ L0 (mm)。0 なら表 4.3 の標準埋込み深さを使う。
        /// せん断 (支圧) の Qa = D·L·fcc の L に使う。
        /// </summary>
        public double EmbedmentLength
        {
            get => _embedmentLength;
            set { if (SetProperty(ref _embedmentLength, Math.Max(0.0, value))) OnDerivedChanged(); }
        }

        private double _anchorageLengthInD;
        /// <summary>
        /// 定着長さ (呼び名の数値 d の倍数)。0 なら標準品 (WSD390 は 35d、WSD490 は 42d。表 2.5・4.2.3)。
        /// </summary>
        public double AnchorageLengthInD
        {
            get => _anchorageLengthInD;
            set { if (SetProperty(ref _anchorageLengthInD, Math.Max(0.0, value))) OnDerivedChanged(); }
        }

        private bool _isConfined = true;
        /// <summary>横補強筋で拘束されたコア内に定着するか ((4.6) 式の α: 拘束あり 1.0、その他 1.25)。</summary>
        public bool IsConfined
        {
            get => _isConfined;
            set { if (SetProperty(ref _isConfined, value)) OnDerivedChanged(); }
        }

        // ───────── 中詰補強筋 (任意。保存する) ─────────
        // マニュアルは図 4.22 の出力例に中詰補強筋の欄 (本数・半径・かぶり・応力度と許容応力度) を置くだけで、計算法の本文は無い。
        // 本プログラムは NewJ-BAR と中詰補強筋を半径の違う 2 段の円環として、それぞれの材料で平面保持の仮定 (4.4.3.1・4.4.4) を当てる
        // (2026-11-26 利用者の判断。出力例の「1 段鉄筋換算半径」は材料の違う 2 種類を 1 つにまとめるので採らない)。
        // 中詰補強筋がなければ NewJ-BAR だけの計算と同じ。

        private int _infillBarCount;
        /// <summary>中詰補強筋の本数 (0 なら無し)。本数によらず円環に置き換える (出力例の In = atn·rn²/2 と同じ)。</summary>
        public int InfillBarCount
        {
            get => _infillBarCount;
            set { if (SetProperty(ref _infillBarCount, Math.Max(0, value))) OnDerivedChanged(); }
        }

        private string _infillBarSize = "D22";
        /// <summary>中詰補強筋の呼び名 (D13〜D41)。</summary>
        public string InfillBarSize
        {
            get => _infillBarSize;
            set { if (SetProperty(ref _infillBarSize, value ?? "D22")) OnDerivedChanged(); }
        }

        private string _infillBarGrade = "SD345";
        /// <summary>中詰補強筋の種類の記号 (SD295 / SD345 / SD390 / SD490)。</summary>
        public string InfillBarGrade
        {
            get => _infillBarGrade;
            set { if (SetProperty(ref _infillBarGrade, value ?? "SD345")) OnDerivedChanged(); }
        }

        private double _infillBarCover = 100.0;
        /// <summary>
        /// 中詰補強筋の重心かぶり (mm)。中詰めの外周 (鋼管杭・外殻鋼管は鋼管の内面、SC 杭は中空部の内面) から補強筋の中心まで。
        /// </summary>
        public double InfillBarCover
        {
            get => _infillBarCover;
            set { if (SetProperty(ref _infillBarCover, Math.Max(0.0, value))) OnDerivedChanged(); }
        }

        private double _infillDepth;
        /// <summary>
        /// 中詰めコンクリートの深さ (杭頂部から。mm)。0 なら確かめない。中詰補強筋があれば定着長さ + 200 mm 以上が要る (4.2.1 1))。
        /// </summary>
        public double InfillDepth
        {
            get => _infillDepth;
            set { if (SetProperty(ref _infillDepth, Math.Max(0.0, value))) OnDerivedChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public static IReadOnlyList<string> InfillBarSizeOptions { get; } = ["D13", "D16", "D19", "D22", "D25", "D29", "D32", "D35", "D38", "D41"];

        [System.Text.Json.Serialization.JsonIgnore]
        public static IReadOnlyList<string> InfillBarGradeOptions { get; } = ["SD295", "SD345", "SD390", "SD490"];

        // ───────── 杭とパイルキャップから受け取る値 (保存しない) ─────────
        private double _infillOuterDiameter;
        /// <summary>
        /// 中詰めの外径 (mm)。鋼管杭・外殻鋼管は鋼管の内径 (D − 2t)、SC 杭は中空部の径 (D − 2×(鋼管厚 + コンクリート厚))。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillOuterDiameter
        {
            get => _infillOuterDiameter;
            private set => SetProperty(ref _infillOuterDiameter, value);
        }

        private double _pileOuterDiameter;
        /// <summary>鋼管の外径 (腐食代を見込まない公称外径) (mm)。NewJ-BAR はここに溶接する。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double PileOuterDiameter
        {
            get => _pileOuterDiameter;
            private set => SetProperty(ref _pileOuterDiameter, value);
        }

        private double _pipeThickness;
        /// <summary>鋼管の板厚 (公称) (mm)。最小板厚 (1.2 3)) と比べる。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double PipeThickness
        {
            get => _pipeThickness;
            private set => SetProperty(ref _pipeThickness, value);
        }

        private string _pipeGrade = string.Empty;
        /// <summary>鋼管の規格 (SKK400 / SKK490)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string PipeGrade
        {
            get => _pipeGrade;
            private set => SetProperty(ref _pipeGrade, value ?? string.Empty);
        }

        private double _fc;
        /// <summary>フーチング (パイルキャップ) のコンクリートの設計基準強度 (N/mm²)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Fc
        {
            get => _fc;
            private set => SetProperty(ref _fc, value);
        }

        private double _gamma = 23.0;
        /// <summary>フーチングのコンクリートの気乾単位体積重量 (kN/m³)。Ec に使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Gamma
        {
            get => _gamma;
            private set => SetProperty(ref _gamma, value);
        }

        private string _pileBodyType = string.Empty;
        /// <summary>杭体のタイプ (鋼管杭・既製コンクリート杭・場所打ち鋼管コンクリート杭)。適用範囲の確かめにだけ使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string PileBodyType
        {
            get => _pileBodyType;
            private set => SetProperty(ref _pileBodyType, value ?? string.Empty);
        }

        private string _topSectionType = string.Empty;
        /// <summary>最上段の杭区間の断面タイプ (鋼管杭・SC杭・鋼管コンクリート部など)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string TopSectionType
        {
            get => _topSectionType;
            private set => SetProperty(ref _topSectionType, value ?? string.Empty);
        }

        private bool _hasSteelPipe = true;
        /// <summary>
        /// 最上段の杭区間が外面に鋼管を持つか。NewJ-BAR は鋼管杭・SC 杭・外殻鋼管場所打ちコンクリート杭の杭頭の鋼管に溶接する (マニュアル 1.2 1))。
        /// PHC 杭や場所打ち鋼管コンクリート杭の鉄筋コンクリート部には溶接できない。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasSteelPipe
        {
            get => _hasSteelPipe;
            private set => SetProperty(ref _hasSteelPipe, value);
        }

        /// <summary>杭 (最上段の鋼管) の寸法と規格を受け取る。</summary>
        public void SetPile(double outerDiameter, double thickness, string? pipeGrade)
        {
            bool changed = false;
            if (PileOuterDiameter != outerDiameter) { PileOuterDiameter = outerDiameter; changed = true; }
            if (PipeThickness != thickness) { PipeThickness = thickness; changed = true; }
            if (PipeGrade != (pipeGrade ?? string.Empty)) { PipeGrade = pipeGrade ?? string.Empty; changed = true; }
            // 中詰めの外径は鋼管の内径とする (SC 杭は SetTopSection が中空部の径で上書きする)
            double infillD = Math.Max(0.0, outerDiameter - 2.0 * thickness);
            if (InfillOuterDiameter != infillD) { InfillOuterDiameter = infillD; changed = true; }
            if (changed) OnDerivedChanged();
        }

        /// <summary>
        /// 最上段の杭区間を受け取る。外面に鋼管が無ければ (PHC 杭・鉄筋コンクリート部) 鋼管の寸法を 0 にして、断面を組めないようにする。
        /// </summary>
        public void SetTopSection(string? pileBodyType, string? sectionType, double pipeOuterDiameter, double pipeThickness, string? pipeGrade,
            double infillOuterDiameter = 0.0)
        {
            bool hasPipe = HasOuterSteelPipe(sectionType) && pipeOuterDiameter > 0 && pipeThickness > 0;
            bool changed = false;
            if (PileBodyType != (pileBodyType ?? string.Empty)) { PileBodyType = pileBodyType ?? string.Empty; changed = true; }
            if (TopSectionType != (sectionType ?? string.Empty)) { TopSectionType = sectionType ?? string.Empty; changed = true; }
            if (HasSteelPipe != hasPipe) { HasSteelPipe = hasPipe; changed = true; }
            if (changed) OnDerivedChanged();
            SetPile(hasPipe ? pipeOuterDiameter : 0.0, hasPipe ? pipeThickness : 0.0, pipeGrade);
            // 中詰めの外径: 指定があればそれ (SC 杭の中空部)、無ければ SetPile が入れた鋼管の内径のまま
            if (hasPipe && infillOuterDiameter > 0 && InfillOuterDiameter != infillOuterDiameter)
            {
                InfillOuterDiameter = infillOuterDiameter;
                OnDerivedChanged();
            }
        }

        /// <summary>
        /// 外面に鋼管を持つ断面タイプか。鋼管杭 (鋼管・コンクリート充填鋼管部)、SC 杭、場所打ち鋼管コンクリート杭の鋼管コンクリート部。
        /// </summary>
        public static bool HasOuterSteelPipe(string? sectionType) => sectionType is
            PileTypeNames.SteelPipeSection or PileTypeNames.CftSection or PileTypeNames.SteelPipe
            or PileTypeNames.Sc or PileTypeNames.SteelPipeConcreteSection;

        /// <summary>杭径がこれ以上の外殻鋼管場所打ちコンクリート杭は、フーチングを杭と基礎梁の接合部として配慮する (マニュアル 4.2.4)。</summary>
        public const double LargeInsituDiameter = 1500.0;

        /// <summary>
        /// 断面を組めない理由 (組めるなら空文字)。画面・解析前の確認・検定で同じ文を出す。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string NotComputableReason
        {
            get
            {
                if (IsComputable) return string.Empty;
                if (!HasSteelPipe)
                    return $"最上部の杭区間 ({(TopSectionType.Length > 0 ? TopSectionType : "断面タイプ未設定")}) は外面に鋼管を持たないので、NewJ-BAR を溶接できません"
                        + " (NewJ-BAR は鋼管杭・SC杭・外殻鋼管場所打ちコンクリート杭の杭頭の鋼管に溶接します)";
                if (HasInfill && PileOuterDiameter > 0 && InfillBarArea <= 0)
                    return $"中詰補強筋の呼び名 {InfillBarSize} は選択肢にありません";
                if (HasInfill && PileOuterDiameter > 0 && InfillRadius <= 0)
                    return $"中詰補強筋の配置半径が 0 以下です (中詰めの外径 {InfillOuterDiameter:N0} mm、重心かぶり {InfillBarCover:N0} mm)";
                return "NewJ-BAR の仮想円形断面を組めません (鋼管の外径、パイルキャップの Fc、補強筋の本数・呼び名・種類を確かめてください)";
            }
        }

        /// <summary>パイルキャップのコンクリートを受け取る。</summary>
        public void SetPileCapConcrete(double fc, double gamma)
        {
            bool changed = false;
            if (Fc != fc) { Fc = fc; changed = true; }
            if (Gamma != gamma) { Gamma = gamma; changed = true; }
            if (changed) OnDerivedChanged();
        }

        private void OnDerivedChanged()
        {
            // 派生値はすべて getter で求めるので、画面に知らせるだけ
            OnPropertyChanged(string.Empty);
        }

        public NewJBar DeepCopy() => CloneWithoutSubscribers<NewJBar>();

        // ───────── 材料 ─────────
        /// <summary>呼び名に用いた数値 d (mm)。WD32J → 32。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NominalNumber => Size switch
        {
            "WD32J" => 32.0,
            "WD35J" => 35.0,
            "WD38J" => 38.0,
            "WD41J" => 41.0,
            _ => 0.0,
        };

        /// <summary>公称直径 (mm)。表 2.4。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NominalDiameter => Size switch
        {
            "WD32J" => 31.8,
            "WD35J" => 34.9,
            "WD38J" => 38.1,
            "WD41J" => 41.3,
            _ => 0.0,
        };

        /// <summary>公称断面積 (mm²) 1 本あたり。表 2.4 (cm² を mm² に)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double BarArea => Size switch
        {
            "WD32J" => 794.2,
            "WD35J" => 956.6,
            "WD38J" => 1140.0,
            "WD41J" => 1340.0,
            _ => 0.0,
        };

        /// <summary>全本数の断面積 (mm²)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double TotalBarArea => BarArea * Count;

        /// <summary>基準強度 F (N/mm²) = 短期の許容引張・圧縮応力度 (表 3.1)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double F => Grade == "WSD490" ? 490.0 : 390.0;

        /// <summary>主筋の終局強度 (N/mm²)。WSD490 は 490、WSD390 は 1.1×390 (4.4.4.2・3.3 2))。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double UltimateStrength => Grade == "WSD490" ? 490.0 : 1.1 * 390.0;

        /// <summary>コンクリートのヤング係数 Ec = 3.35×10⁴·(γ/24)²·(Fc/60)^(1/3) (N/mm²)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ec => Fc > 0 ? 3.35e4 * Math.Pow(Gamma / 24.0, 2.0) * Math.Pow(Fc / 60.0, 1.0 / 3.0) : 0.0;

        /// <summary>
        /// ヤング係数比 n (RC 規準の許容応力度設計で用いる値)。
        /// Fc ≤ 27 は 15、≤ 36 は 13、≤ 48 は 11、それより大きければ 9。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double YoungRatio => Fc <= 27.0 ? 15.0 : Fc <= 36.0 ? 13.0 : Fc <= 48.0 ? 11.0 : 9.0;

        // ───────── 仮想円形断面 ─────────
        /// <summary>
        /// 仮想円形断面の外径 (mm)。杭径 250 以上 500 未満は D + 200、500 以上は D + 2·d + 200 (4.3.1 1) b))。
        /// d は呼び名の数値 (図 4.22 の出力例 Φ600・WD32J で 600 + 264)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double VirtualDiameter
        {
            get
            {
                double d = PileOuterDiameter;
                if (!(d > 0)) return 0.0;
                return d < 500.0 ? d + 200.0 : d + 2.0 * NominalNumber + 200.0;
            }
        }

        /// <summary>主筋 (NewJ-BAR の中心) の配置半径 (mm)。鋼管の外面に溶接するので D/2 + 公称直径/2。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double BarRadius => PileOuterDiameter > 0 ? PileOuterDiameter / 2.0 + NominalDiameter / 2.0 : 0.0;

        /// <summary>主筋を円環に置き換えるか (8 本以上)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool UsesRing => Count >= RingMinimumBars;

        /// <summary>断面を組めるか (杭径・Fc・本数・呼び名がそろっている)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsComputable => PileOuterDiameter > 0 && Fc > 0 && Count > 0 && BarArea > 0 && IsAvailable(Grade, Size)
            && (!HasInfill || (InfillRadius > 0 && InfillBarArea > 0));

        // ───────── 中詰補強筋の派生値 ─────────
        /// <summary>中詰補強筋があるか。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasInfill => InfillBarCount > 0;

        /// <summary>中詰補強筋の公称断面積 (mm²) 1 本あたり。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillBarArea => PileDesign.Constants.RebarAreas.Of(InfillBarSize);

        /// <summary>中詰補強筋の全断面積 (mm²)。無ければ 0。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillTotalArea => HasInfill ? InfillBarArea * InfillBarCount : 0.0;

        /// <summary>中詰補強筋の呼び名の数値 (mm)。D22 → 22。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillNominalNumber
            => InfillBarSize is { Length: > 1 } s && double.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0.0;

        /// <summary>中詰補強筋の配置半径 rn (mm) = 中詰めの外径/2 − 重心かぶり。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillRadius => InfillOuterDiameter > 0 ? InfillOuterDiameter / 2.0 - InfillBarCover : 0.0;

        /// <summary>中詰補強筋の基準強度 F = 短期の許容引張・圧縮応力度 (N/mm²)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillF => InputData.MainBars.GradeYieldStrength(InfillBarGrade);

        /// <summary>中詰補強筋の長期の許容応力度 (N/mm²)。RC 規準: min(F/1.5, 径 25 以下 215・それより太い 195)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillLongTermAllowableStress => Math.Min(InfillF / 1.5, InfillNominalNumber > 25.0 ? 195.0 : 215.0);

        /// <summary>中詰補強筋の終局強度 (N/mm²)。1.1F (SD490 は F)。NewJ-BAR の WSD390 の 1.1F・WSD490 の 490 と同じ考え方。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillUltimateStrength => InfillBarGrade == "SD490" ? InfillF : 1.1 * InfillF;

        /// <summary>中詰めの深さの下限 (mm): 定着長さ (SD490 は 42d、それ以外は 35d) + 200 (4.2.1 1))。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double InfillRequiredDepth => HasInfill ? (InfillBarGrade == "SD490" ? 42.0 : 35.0) * InfillNominalNumber + 200.0 : 0.0;

        // ───────── せん断 (支圧) ─────────
        /// <summary>標準埋込み深さ L0 (mm)。表 4.3。鋼種 1 (400 N 級) と鋼種 2 以上で違う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double StandardEmbedmentLength
        {
            get
            {
                bool class1 = IsSteelClass1;
                if (Grade == "WSD490")
                {
                    return Size switch
                    {
                        "WD32J" => class1 ? 260.0 : 230.0,
                        "WD35J" or "WD38J" => class1 ? 280.0 : 260.0,
                        "WD41J" => class1 ? 300.0 : 270.0,
                        _ => 0.0,
                    };
                }
                return Size switch
                {
                    "WD32J" => 220.0,
                    "WD35J" or "WD38J" => 240.0,
                    _ => 0.0,
                };
            }
        }

        /// <summary>せん断 (支圧) に使う埋込み深さ L (mm)。入力が 0 なら標準埋込み深さ。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double EffectiveEmbedmentLength => EmbedmentLength > 0 ? EmbedmentLength : StandardEmbedmentLength;

        /// <summary>短期許容支圧応力度 fcc = 2 × (2/3)·Fc (N/mm²)。(4.7) 式。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShortTermBearingStress => 2.0 * (2.0 / 3.0) * Fc;

        /// <summary>短期許容せん断力 (支圧) Qa = D·L·fcc (N)。(4.7) 式。D は杭径。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double ShortTermAllowableShear => PileOuterDiameter * EffectiveEmbedmentLength * ShortTermBearingStress;

        /// <summary>終局せん断抵抗値 Qu = 1.5·Qa (N)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double UltimateShear => UltimateShearFactor * ShortTermAllowableShear;

        // ───────── 定着長さ ─────────
        /// <summary>付着割裂の基準となる強度 fb = Fc/40 + 0.9 (N/mm²)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Fb => Fc / 40.0 + 0.9;

        /// <summary>(4.6) 式の α (横補強筋で拘束されたコア内 1.0、その他 1.25)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double AnchorageAlpha => IsConfined ? 1.0 : 1.25;

        /// <summary>必要定着長さ Lab = α·S·σ1·db/(10·fb) (mm)。(4.6) 式 (RC 規準 2018 の (17.2) 式)。σ1 は短期許容応力度。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double RequiredAnchorageLength => Fb > 0 ? AnchorageAlpha * AnchorageS * F * NominalNumber / (10.0 * Fb) : double.NaN;

        /// <summary>標準品の定着長さ (d の倍数)。WSD390 は 35、WSD490 は 42。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double StandardAnchorageLengthInD => Grade == "WSD490" ? 42.0 : 35.0;

        /// <summary>定着長さ (mm)。入力が 0 なら標準品。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double AnchorageLength => (AnchorageLengthInD > 0 ? AnchorageLengthInD : StandardAnchorageLengthInD) * NominalNumber;

        // ───────── 適用範囲 ─────────
        /// <summary>鋼管の規格が鋼種 1 (400 N 級) か。SKK490 などは鋼種 2。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSteelClass1 => !(PipeGrade ?? "").Contains("490");

        /// <summary>鋼管の最小板厚 (mm)。マニュアル 1.2 3) の表。該当しなければ 0。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double MinimumPipeThickness
        {
            get
            {
                bool class1 = IsSteelClass1;
                if (Grade == "WSD490")
                {
                    return Size switch
                    {
                        "WD32J" => 8.0,
                        "WD35J" => class1 ? 9.0 : 8.0,
                        "WD38J" => class1 ? 10.0 : 8.0,
                        "WD41J" => class1 ? 11.0 : 9.0,
                        _ => 0.0,
                    };
                }
                return Size switch
                {
                    "WD32J" => class1 ? 8.0 : 6.0,
                    "WD35J" => class1 ? 9.0 : 7.0,
                    "WD38J" => class1 ? 10.0 : 8.0,
                    _ => 0.0,
                };
            }
        }

        /// <summary>フーチングの Fc の下限 (N/mm²)。WSD390 は 21、WSD490 は 24 (1.2 5))。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double MinimumFc => Grade == "WSD490" ? 24.0 : 21.0;

        /// <summary>主筋の心心間隔 (mm) = 2π·r'/m。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double BarSpacing => Count > 0 ? 2.0 * Math.PI * BarRadius / Count : 0.0;

        /// <summary>設計上の限界心心間隔 2.7d + 20 (mm)。4.2.2 3)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double MinimumBarSpacing => 2.7 * NominalNumber + 20.0;

        /// <summary>
        /// 適用範囲から外れている点を返す (空なら範囲内)。どれも検定の値は出すが、マニュアルの範囲の外であることを知らせる。
        /// </summary>
        public List<string> GetApplicabilityProblems()
        {
            var list = new List<string>();
            if (!IsAvailable(Grade, Size))
                list.Add($"{Size} は {Grade} にありません (WD41J は WSD490 だけです)");
            if (PileOuterDiameter > 0 && PileOuterDiameter < 250.0)
                list.Add($"杭径 {PileOuterDiameter:N0} mm が適用範囲 (250 mm 以上) の外です");
            if (Fc > 0 && Fc < MinimumFc)
                list.Add($"パイルキャップの Fc {Fc:0.#} N/mm² が {Grade} の下限 (Fc {MinimumFc:0}) を下回っています");
            if (Count > 0 && Count < 4)
                list.Add($"本数 {Count} 本が適用範囲 (4 本以上) の外です");
            double tmin = MinimumPipeThickness;
            if (tmin > 0 && PipeThickness > 0 && PipeThickness < tmin)
                list.Add($"鋼管の板厚 {PipeThickness:0.#} mm が {Size} ({Grade}) を溶接する最小板厚 {tmin:0} mm を下回っています");
            double l0 = StandardEmbedmentLength;
            if (EmbedmentLength > 0 && l0 > 0 && EmbedmentLength < l0)
                list.Add($"埋込み深さ {EmbedmentLength:N0} mm が標準埋込み深さ {l0:N0} mm を下回っています");
            double lab = RequiredAnchorageLength;
            if (double.IsFinite(lab) && NominalNumber > 0 && AnchorageLength < lab)
                list.Add($"定着長さ {AnchorageLength:N0} mm が必要定着長さ Lab = {lab:N0} mm を下回っています");
            if (Count > 0 && BarRadius > 0 && BarSpacing < MinimumBarSpacing)
                list.Add($"心心間隔 {BarSpacing:N0} mm が限界心心間隔 2.7d + 20 = {MinimumBarSpacing:N0} mm を下回っています");
            if (HasInfill && InfillDepth > 0 && InfillDepth < InfillRequiredDepth)
                list.Add($"中詰めコンクリートの深さ {InfillDepth:N0} mm が、中詰補強筋の定着長さ + 200 = {InfillRequiredDepth:N0} mm を下回っています (「NewJ-BAR 設計マニュアル (杭頭補強編)」4.2.1 1))");
            return list;
        }

        /// <summary>
        /// 適用範囲の内でも、マニュアルが設計者に別の検討を求めている点 (空なら無し)。
        /// 杭径 1,500 mm 以上の外殻鋼管場所打ちコンクリート杭は、フーチングが杭と基礎梁の接合部の役割を担う (4.2.4)。
        /// </summary>
        public List<string> GetDesignNotes()
        {
            var list = new List<string>();
            if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileOuterDiameter >= LargeInsituDiameter)
                list.Add($"杭径 {PileOuterDiameter:N0} mm (1,500 mm 以上) の外殻鋼管場所打ちコンクリート杭です。フーチングが杭と基礎梁の接合部となるので、"
                    + "フーチングのせん断と補強筋の長さ (フーチング上端付近まで延ばす) を別に検討してください (「NewJ-BAR 設計マニュアル (杭頭補強編)」4.2.4)");
            if (HasInfill && InfillDepth <= 0)
                list.Add($"中詰めコンクリートの深さは、杭頂部から中詰補強筋の定着長さ + 200 = {InfillRequiredDepth:N0} mm 以上にしてください"
                    + " (「NewJ-BAR 設計マニュアル (杭頭補強編)」4.2.1 1)。深さを入力すると照査します)");
            return list;
        }

        // ───────── 断面の幾何 ─────────
        /// <summary>鉄筋の点。Y は曲げの方向の座標 (圧縮縁が +)、Area は 1 点あたりの面積、Group は 0 = NewJ-BAR・1 = 中詰補強筋。</summary>
        private readonly record struct Bar(double Y, double Area, int Group);

        private const int GroupJBar = 0;
        private const int GroupInfill = 1;

        /// <summary>
        /// 鉄筋の位置と 1 点あたりの面積。NewJ-BAR は 8 本以上なら円環 (等間隔の 360 点)、8 本未満は配置角度 alpha の実際の位置。
        /// 中詰補強筋は本数によらず円環 (出力例の In = atn·rn²/2 と同じ置き換え)。
        /// </summary>
        private List<Bar> BarPoints(double alpha)
        {
            var pts = new List<Bar>();
            double r = BarRadius;
            if (UsesRing)
            {
                double a = TotalBarArea / RingPoints;
                for (int i = 0; i < RingPoints; i++)
                    pts.Add(new(r * Math.Cos(2.0 * Math.PI * (i + 0.5) / RingPoints), a, GroupJBar));
            }
            else
            {
                for (int i = 0; i < Count; i++)
                    pts.Add(new(r * Math.Cos(alpha + 2.0 * Math.PI * i / Count), BarArea, GroupJBar));
            }
            if (HasInfill && InfillRadius > 0)
            {
                double a = InfillTotalArea / RingPoints;
                for (int i = 0; i < RingPoints; i++)
                    pts.Add(new(InfillRadius * Math.Cos(2.0 * Math.PI * (i + 0.5) / RingPoints), a, GroupInfill));
            }
            return pts;
        }

        /// <summary>主筋の配置角度の候補。円環なら 1 つ、8 本未満なら π/m 〜 2π/m を 10 等分した 11 箇所。</summary>
        private IEnumerable<double> AlphaCandidates()
        {
            if (UsesRing) { yield return 0.0; yield break; }
            for (int k = 0; k <= WeakAxisDivisions; k++)
                yield return Math.PI / Count + (Math.PI / Count) * k / WeakAxisDivisions;
        }

        /// <summary>コンクリートの層 (中心の y と面積)。仮想径を 400 層に分ける。</summary>
        private List<(double Y, double Area)> ConcreteStrips()
        {
            double R = VirtualDiameter / 2.0;
            var strips = new List<(double, double)>(StripCount);
            double h = 2.0 * R / StripCount;
            for (int i = 0; i < StripCount; i++)
            {
                double y0 = -R + i * h, y1 = y0 + h;
                // 層の面積は円の弦で挟まれた部分を正確に (台形近似でなく) 求める
                double area = SegmentArea(R, y1) - SegmentArea(R, y0);
                strips.Add(((y0 + y1) / 2.0, area));
            }
            return strips;
        }

        /// <summary>半径 R の円のうち y ≤ t の部分の面積。</summary>
        private static double SegmentArea(double R, double t)
        {
            t = Math.Clamp(t, -R, R);
            return R * R * Math.Acos(-t / R) + t * Math.Sqrt(R * R - t * t);
        }

        // ───────── 許容曲げモーメント (短期・長期) ─────────
        /// <summary>単位の応力状態。Tension・Compression は鉄筋の組 (0 = NewJ-BAR、1 = 中詰補強筋) ごとの最大の応力度。</summary>
        private readonly record struct UnitState(double N, double M, double ConcreteEdge, double[] Tension, double[] Compression);

        /// <summary>
        /// 中立軸を決めたときの単位の応力状態。圧縮縁を y = R、中立軸を y = R − xn に置き、
        /// コンクリートの応力度 = (y − yn)⁺、鉄筋 = n·(y − yn) として軸力と中心まわりのモーメント、各縁の応力度を返す。
        /// </summary>
        private UnitState UnitElasticState(double xn, List<(double Y, double Area)> strips, List<Bar> bars)
        {
            double R = VirtualDiameter / 2.0;
            double yn = R - xn;
            double n = YoungRatio;
            double N = 0, M = 0;
            foreach (var (y, a) in strips)
            {
                double s = y - yn;
                if (s > 0) { N += s * a; M += s * a * y; }
            }
            var tMax = new double[2];
            var cMax = new double[2];
            foreach (var b in bars)
            {
                double s = n * (b.Y - yn);
                N += s * b.Area; M += s * b.Area * b.Y;
                if (s < 0) tMax[b.Group] = Math.Max(tMax[b.Group], -s);
                else cMax[b.Group] = Math.Max(cMax[b.Group], s);
            }
            double edge = Math.Max(0.0, xn);
            return new UnitState(N, M, edge, tMax, cMax);
        }

        /// <summary>コンクリートの許容圧縮応力度と、鉄筋の組ごとの許容応力度 (引張・圧縮とも同じ)。</summary>
        private (double Fc, double[] Bar) AllowableStresses(bool shortTerm)
            => shortTerm
                ? (2.0 / 3.0 * Fc, new[] { F, InfillF })
                : (Fc / 3.0, new[] { LongTermAllowableStress, InfillLongTermAllowableStress });

        /// <summary>中立軸 xn で、どれかの応力度がちょうど許容値に達するときの (N, M) (N, N·mm)。</summary>
        private (double N, double M) AllowableStateAt(double xn, bool shortTerm,
            List<(double Y, double Area)> strips, List<Bar> bars)
        {
            var u = UnitElasticState(xn, strips, bars);
            var (fc, fBar) = AllowableStresses(shortTerm);
            double k = double.PositiveInfinity;
            if (u.ConcreteEdge > 0) k = Math.Min(k, fc / u.ConcreteEdge);
            for (int g = 0; g < 2; g++)
            {
                if (u.Tension[g] > 0) k = Math.Min(k, fBar[g] / u.Tension[g]);
                if (u.Compression[g] > 0) k = Math.Min(k, fBar[g] / u.Compression[g]);
            }
            if (!double.IsFinite(k)) return (double.NaN, double.NaN);
            return (k * u.N, k * u.M);
        }

        /// <summary>
        /// 軸力 N (N、圧縮が正) のときの許容曲げモーメント (N·mm)。短期なら shortTerm = true。
        /// 8 本未満は主筋の配置角度を振って最小を取る (弱軸方向)。軸力が許容軸力の範囲の外なら NaN。
        /// </summary>
        public double GetAllowableMoment(double axialN, bool shortTerm = true)
        {
            if (!IsComputable) return double.NaN;
            var strips = ConcreteStrips();
            double min = double.PositiveInfinity;
            foreach (double alpha in AlphaCandidates())
            {
                double m = AllowableMomentFor(axialN, shortTerm, strips, BarPoints(alpha));
                if (double.IsNaN(m)) return double.NaN;
                min = Math.Min(min, m);
            }
            return double.IsFinite(min) ? min : double.NaN;
        }

        private double AllowableMomentFor(double axialN, bool shortTerm,
            List<(double Y, double Area)> strips, List<Bar> bars)
        {
            // 中立軸の深さ xn を t ∈ (0, 1) で表し、t → 0 が全断面引張 (xn → −∞)、t → 1 が全断面圧縮 (xn → +∞)。
            // N(xn) は xn に対して増える。
            double D = VirtualDiameter;
            double Xn(double t) => D * Math.Tan(Math.PI * (t - 0.5));
            (double N, double M) At(double t) => AllowableStateAt(Xn(t), shortTerm, strips, bars);

            double lo = 1e-9, hi = 1.0 - 1e-9;
            var sLo = At(lo); var sHi = At(hi);
            if (!(axialN >= sLo.N && axialN <= sHi.N)) return double.NaN;
            for (int it = 0; it < 200; it++)
            {
                double mid = 0.5 * (lo + hi);
                var s = At(mid);
                if (s.N < axialN) lo = mid; else hi = mid;
                if (hi - lo < 1e-13) break;
            }
            return Math.Abs(At(0.5 * (lo + hi)).M);
        }

        /// <summary>軸力 N (N) と曲げモーメント M (N·mm) を受けるときの応力状態 (主筋 8 本以上の円環のとき)。</summary>
        private (UnitState State, double Scale)? ElasticStateFor(double axialN, double moment)
        {
            if (!IsComputable) return null;
            var strips = ConcreteStrips();
            var bars = BarPoints(0.0);
            double D = VirtualDiameter;
            double e = moment / axialN;
            // 単位の状態の M/N が偏心距離 e になる中立軸を探す (N > 0 の範囲)。xn を大きくすると M/N は小さくなる。
            double lo = 1e-6 * D, hi = 1e3 * D;
            for (int it = 0; it < 300; it++)
            {
                double mid = Math.Sqrt(lo * hi);
                var u = UnitElasticState(mid, strips, bars);
                double ratio = u.N > 0 ? u.M / u.N : double.PositiveInfinity;
                if (ratio > e) lo = mid; else hi = mid;
                if (hi / lo < 1 + 1e-12) break;
            }
            var s = UnitElasticState(Math.Sqrt(lo * hi), strips, bars);
            return (s, axialN / s.N);
        }

        /// <summary>
        /// 軸力 N (N) と曲げモーメント M (N·mm) を受けるときの、コンクリートの圧縮縁・引張側 NewJ-BAR・圧縮側 NewJ-BAR の応力度 (N/mm²)。
        /// 主筋 8 本以上 (円環) のとき。図 4.22 の出力例 (応力度の検討) と突き合わせる。
        /// </summary>
        public (double Concrete, double BarTension, double BarCompression) GetElasticStresses(double axialN, double moment)
        {
            if (ElasticStateFor(axialN, moment) is not { } r) return (double.NaN, double.NaN, double.NaN);
            return (r.Scale * r.State.ConcreteEdge, r.Scale * r.State.Tension[GroupJBar], r.Scale * r.State.Compression[GroupJBar]);
        }

        /// <summary>同じ条件の、中詰補強筋の引張側・圧縮側の応力度 (N/mm²)。中詰補強筋が無ければ 0。</summary>
        public (double Tension, double Compression) GetInfillElasticStresses(double axialN, double moment)
        {
            if (ElasticStateFor(axialN, moment) is not { } r) return (double.NaN, double.NaN);
            return (r.Scale * r.State.Tension[GroupInfill], r.Scale * r.State.Compression[GroupInfill]);
        }

        // ───────── 終局曲げモーメント ─────────
        /// <summary>
        /// ひずみ分布 (圧縮縁 y = R のひずみ εtop、最も引張側の主筋 y = yb のひずみ εbot) のときの (N, M) (N, N·mm)。
        /// </summary>
        private (double N, double M) UltimateStateAt(double epsTop, double epsBot, double yBot,
            List<(double Y, double Area)> strips, List<Bar> bars)
        {
            double R = VirtualDiameter / 2.0;
            double ec = Ec;
            Span<double> sigmaU = [UltimateStrength, InfillUltimateStrength];
            double Strain(double y) => epsBot + (epsTop - epsBot) * (y - yBot) / (R - yBot);
            double ConcreteStress(double eps) => eps <= 0 ? 0.0 : Math.Min(ec * eps, Fc);
            double N = 0, M = 0;
            foreach (var (y, a) in strips)
            {
                double s = ConcreteStress(Strain(y));
                N += s * a; M += s * a * y;
            }
            foreach (var b in bars)
            {
                double eps = Strain(b.Y);
                double su = sigmaU[b.Group];
                double s = Math.Clamp(Er * eps, -su, su) - ConcreteStress(eps);   // 鉄筋位置のコンクリートを差し引く
                N += s * b.Area; M += s * b.Area * b.Y;
            }
            return (N, M);
        }

        /// <summary>
        /// 軸力 N (N、圧縮が正) のときの終局曲げモーメント Mu (N·mm)。8 本未満は弱軸方向 (最小)。範囲の外なら NaN。
        /// </summary>
        public double GetUltimateMoment(double axialN)
        {
            if (!IsComputable) return double.NaN;
            var strips = ConcreteStrips();
            double min = double.PositiveInfinity;
            foreach (double alpha in AlphaCandidates())
            {
                double m = UltimateMomentFor(axialN, strips, BarPoints(alpha));
                if (double.IsNaN(m)) return double.NaN;
                min = Math.Min(min, m);
            }
            return double.IsFinite(min) ? min : double.NaN;
        }

        private double UltimateMomentFor(double axialN, List<(double Y, double Area)> strips, List<Bar> bars)
        {
            // 引張側のひずみ 0.2 の上限は、最も外の NewJ-BAR で見る (中詰補強筋は内側なのでひずみが小さい)
            double yBot = double.PositiveInfinity;
            foreach (var b in bars) yBot = Math.Min(yBot, b.Y);
            const double ecu = ConcreteUltimateStrain, esu = RebarUltimateStrain;

            // 終局の曲面をたどる 1 変数 s ∈ [0, 2]:
            //   s ∈ [0, 1]: 引張主筋のひずみを −0.2 に固定し、圧縮縁のひずみを −0.2 → 0.003 (引張終局)
            //   s ∈ [1, 2]: 圧縮縁のひずみを 0.003 に固定し、引張主筋のひずみを −0.2 → 0.003 (圧縮終局)
            // この順に軸力は増える。
            (double N, double M) At(double s)
            {
                if (s <= 1.0) return UltimateStateAt(-esu + (ecu + esu) * s, -esu, yBot, strips, bars);
                return UltimateStateAt(ecu, -esu + (ecu + esu) * (s - 1.0), yBot, strips, bars);
            }

            double lo = 0.0, hi = 2.0;
            if (!(axialN >= At(lo).N && axialN <= At(hi).N)) return double.NaN;
            for (int it = 0; it < 200; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (At(mid).N < axialN) lo = mid; else hi = mid;
                if (hi - lo < 1e-13) break;
            }
            return Math.Abs(At(0.5 * (lo + hi)).M);
        }

        // ───────── N-M 曲線 (画面・計算書用) ─────────
        /// <summary>
        /// 許容 (短期) と終局の N-M 曲線 (N は kN、M は kN·m)。軸力を全断面引張から全断面圧縮まで刻む。
        /// </summary>
        public (List<double> N, List<double> M) GetInteraction(bool ultimate, int points = 61)
        {
            var ns = new List<double>();
            var ms = new List<double>();
            if (!IsComputable) return (ns, ms);
            double ac = Math.PI * VirtualDiameter * VirtualDiameter / 4.0;
            double barArea = TotalBarArea + InfillTotalArea;
            double nMin, nMax;
            if (ultimate)
            {
                nMin = -(TotalBarArea * UltimateStrength + InfillTotalArea * InfillUltimateStrength);
                nMax = Fc * (ac - barArea) + UltimateStrength * TotalBarArea + InfillUltimateStrength * InfillTotalArea;
            }
            else
            {
                nMin = -(TotalBarArea * F + InfillTotalArea * InfillF);
                nMax = 2.0 / 3.0 * Fc * (ac + YoungRatio * barArea);
            }
            for (int i = 0; i <= points; i++)
            {
                double n = nMin + (nMax - nMin) * i / points;
                double m = ultimate ? GetUltimateMoment(n) : GetAllowableMoment(n, shortTerm: true);
                if (!double.IsFinite(m)) continue;
                ns.Add(n * 1e-3);
                ms.Add(m * 1e-6);
            }
            return (ns, ms);
        }

        // ───────── 諸元 ─────────
        /// <summary>杭頭部ウィンドウ・計算書に出す諸元。</summary>
        public ObservableCollection<Spec> GetSpecs()
        {
            var specs = new ObservableCollection<Spec>
            {
                new("補強筋 種類の記号", "", Grade, ""),
                new("補強筋 本数-呼び名", "", $"{Count}-{Size}", ""),
                new("補強筋 公称直径", "d", $"{NominalDiameter:0.0}", "mm"),
                new("補強筋 断面積 (1 本)", "ar", $"{BarArea:N1}", "mm2"),
                new("補強筋 短期許容応力度", "ft", $"{F:N0}", "N/mm2"),
                new("補強筋 終局強度", "rσu", $"{UltimateStrength:N0}", "N/mm2"),
                new("杭種 (最上部の杭区間)", "", TopSectionType.Length > 0 ? TopSectionType : "-", ""),
                new("杭径 (鋼管外径)", "D", $"{PileOuterDiameter:N0}", "mm"),
                new("鋼管の板厚・規格", "t", PipeThickness > 0 ? $"{PipeThickness:0.#} ({PipeGrade})" : "-", "mm"),
                new("仮想円形断面の外径", "Dc", $"{VirtualDiameter:N1}", "mm"),
                new("補強筋の配置半径", "r'", $"{BarRadius:N1}", "mm"),
                new("補強筋の扱い", "", UsesRing ? "円環に置き換え (8 本以上)" : "配置を考えた弱軸方向 (8 本未満)", ""),
                new("中詰補強筋 本数-呼び名", "", HasInfill ? $"{InfillBarCount}-{InfillBarSize} ({InfillBarGrade})" : "なし", ""),
                new("中詰補強筋の配置半径", "rn", HasInfill ? Fmt(InfillRadius, "N1") : "-", "mm"),
                new("中詰補強筋 許容応力度 (短期・長期)", "", HasInfill ? $"{InfillF:N0}・{InfillLongTermAllowableStress:N0}" : "-", "N/mm2"),
                new("中詰補強筋 終局強度", "", HasInfill ? $"{InfillUltimateStrength:N1}" : "-", "N/mm2"),
                new("中詰めの深さ (下限)", "", HasInfill ? (InfillDepth > 0 ? $"{InfillDepth:N0}" : "未入力") + $" (≧ {InfillRequiredDepth:N0})" : "-", "mm"),
                new("ヤング係数比", "n", $"{YoungRatio:0}", ""),
                new("短期許容曲げモーメント (N=0)", "Ma", Fmt(GetAllowableMoment(0.0) * 1e-6, "N1"), "kN·m"),
                new("終局曲げモーメント (N=0)", "Mu", Fmt(GetUltimateMoment(0.0) * 1e-6, "N1"), "kN·m"),
                new("埋込み深さ", "L", EmbedmentLength > 0 ? $"{EmbedmentLength:N0}" : $"{StandardEmbedmentLength:N0} (標準)", "mm"),
                new("短期許容支圧応力度", "fcc", $"{ShortTermBearingStress:N1}", "N/mm2"),
                new("短期許容せん断力 (支圧)", "Qa", $"{ShortTermAllowableShear * 1e-3:N0}", "kN"),
                new("終局せん断抵抗値", "Qu", $"{UltimateShear * 1e-3:N0}", "kN"),
                new("定着長さ", "", $"{AnchorageLength:N0}" + (AnchorageLengthInD > 0 ? "" : $" (標準 {StandardAnchorageLengthInD:0}d)"), "mm"),
                new("必要定着長さ", "Lab", Fmt(RequiredAnchorageLength, "N0"), "mm"),
                new("鋼管の最小板厚", "tmin", MinimumPipeThickness > 0 ? $"{MinimumPipeThickness:0}" : "-", "mm"),
            };
            return specs;
        }

        private static string Fmt(double v, string format) => double.IsFinite(v) ? v.ToString(format) : "-";
    }
}
