using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// PRC 節杭 (Nodular PRC pile) 製品ライブラリの 1 レコード。
    ///
    /// 現在の収録はジャパンパイル JP-NPRC105 (プレストレスト鉄筋高強度コンクリート節杭)。
    /// <see cref="NodularPile"/> (NPH) との違いは次の 3 点。
    /// <list type="number">
    ///   <item>主筋に PC 鋼棒 <b>と異形棒鋼 (SD345)</b> を併用する = PRC 断面である</item>
    ///   <item>1 本の杭が「PRC部」と「PHC部」の <b>2 断面</b>を持つ
    ///         (杭頭側が PRC部、それより下が PHC部)。断面性能・耐力とも別々に規定される</item>
    ///   <item>せん断補強筋に 標準型 / 高せん断型 の 2 仕様があり、
    ///         せん断耐力はせん断スパン比 1.0 / 1.5 / 2.0 ごとに与えられる</item>
    /// </list>
    ///
    /// 断面性能 (<see cref="Ao"/>, <see cref="Io"/> 等) はすべて<b>軸部</b>基準の
    /// 中空円形断面である。節部は断面性能に寄与せず、支持力 (周面摩擦) と
    /// 形状表示にのみ効く。
    /// </summary>
    public class NodularPrcPile
    {
        // ── 製品同定 ────────────────────────────────────────────────
        /// <summary>製造者名 (例: ジャパンパイル)</summary>
        public string Maker { get; set; } = "";
        /// <summary>製品シリーズ名 (例: JP-NPRC105)</summary>
        public string Series { get; set; } = "";
        /// <summary>杭形状の種別 (現状は「節杭」固定。将来の拡張用)</summary>
        public string Shape { get; set; } = "";
        /// <summary>呼び名 (例: 440-300)。「節部径-軸部径」を表す。</summary>
        public string Name { get; set; } = "";

        // ── 形状 [mm] ───────────────────────────────────────────────
        /// <summary>節部径 Do</summary>
        public double Do { get; set; }
        /// <summary>軸部径 D</summary>
        public double D { get; set; }
        /// <summary>肉厚 t</summary>
        public double T { get; set; }
        /// <summary>厚さ仕様 (標準 / 特厚 / 超特厚)</summary>
        public string ThicknessType { get; set; } = "";

        // ── 材料・種別 ──────────────────────────────────────────────
        /// <summary>コンクリート設計基準強度 [N/mm²]</summary>
        public double Fc { get; set; }
        /// <summary>種類 (ローマ数字 Ⅰ〜Ⅷ。径によっては ⅠA / ⅠB のように枝番が付く)</summary>
        public string PrestressType { get; set; } = "";

        // ── PC 鋼棒 ────────────────────────────────────────────────
        /// <summary>PC 鋼棒の呼び名 [mm] (7.1 / 9.0 / 10.0 / 11.2 / 12.6)</summary>
        public string PcDesignation { get; set; } = "";
        /// <summary>PC 鋼棒の本数</summary>
        public int PcCount { get; set; }
        /// <summary>PC 鋼棒の断面積合計 Ap [mm²]</summary>
        public double Ap { get; set; }
        /// <summary>PC 鋼棒の配筋径 PCD [mm]</summary>
        public double Pcd { get; set; }

        // ── 異形棒鋼 (主筋 SD345) ──────────────────────────────────
        /// <summary>異形棒鋼の呼び名 (例: D13 / D16 / …)</summary>
        public string BarDesignation { get; set; } = "";
        /// <summary>異形棒鋼の本数</summary>
        public int BarCount { get; set; }
        /// <summary>異形棒鋼の断面積合計 Ag [mm²]</summary>
        public double Ag { get; set; }
        /// <summary>異形棒鋼の配筋径 [mm]</summary>
        public double BarPcd { get; set; }

        // ── PRC部 断面諸数値 (軸部基準、カタログ記載値) ─────────────
        /// <summary>断面積 Ao [mm²]</summary>
        public double Ao { get; set; }
        /// <summary>換算断面積 Ae [mm²] (= Ao + (np−1)Ap + (nr−1)Ag)</summary>
        public double Ae { get; set; }
        /// <summary>断面 1 次モーメント So [mm³] — カタログ印字値</summary>
        public double So { get; set; }
        /// <summary>中空円形断面から算出した断面 1 次モーメント (D³−di³)/12 [mm³]。
        /// カタログ印字の誤植がある行ではこちらが正しい (<see cref="Note"/> 参照)。</summary>
        public double SoFromSection { get; set; }
        /// <summary>断面 2 次モーメント Io [mm⁴]</summary>
        public double Io { get; set; }
        /// <summary>換算断面 2 次モーメント Ie [mm⁴]</summary>
        public double Ie { get; set; }
        /// <summary>換算断面係数 Ze [mm³]</summary>
        public double Ze { get; set; }
        /// <summary>有効プレストレス量 σce [N/mm²]</summary>
        public double SigmaCe { get; set; }

        // ── せん断補強筋 (標準線径 [mm] / ピッチ [mm]) ───────────────
        /// <summary>標準型・降伏強度 490N/mm² の標準線径</summary>
        public double ShearBarStdDia490 { get; set; }
        /// <summary>標準型・降伏強度 490N/mm² のピッチ</summary>
        public double ShearBarStdPitch490 { get; set; }
        /// <summary>標準型・降伏強度 785N/mm² の標準線径</summary>
        public double ShearBarStdDia785 { get; set; }
        /// <summary>標準型・降伏強度 785N/mm² のピッチ</summary>
        public double ShearBarStdPitch785 { get; set; }
        /// <summary>高せん断型・降伏強度 785N/mm² の標準線径</summary>
        public double ShearBarHighDia785 { get; set; }
        /// <summary>高せん断型・降伏強度 785N/mm² のピッチ</summary>
        public double ShearBarHighPitch785 { get; set; }

        // ── PRC部 断面性能 (カタログ記載値) ─────────────────────────
        /// <summary>ひび割れモーメント Msc [kN·m] (軸力 0 時)</summary>
        public double Msc { get; set; }
        /// <summary>長期許容曲げモーメント Mal [kN·m] (軸力 0 時)</summary>
        public double Mal { get; set; }
        /// <summary>短期許容曲げモーメント Mas [kN·m] (軸力 0 時)</summary>
        public double Mas { get; set; }
        /// <summary>終局モーメント Mu [kN·m] (軸力 0 時)</summary>
        public double Mu { get; set; }
        /// <summary>長期許容せん断力 Qal [kN]</summary>
        public double Qal { get; set; }

        /// <summary>標準型 短期許容せん断力 Qas [kN]・せん断スパン比 1.0</summary>
        public double QasStd10 { get; set; }
        /// <summary>標準型 短期許容せん断力 Qas [kN]・せん断スパン比 1.5</summary>
        public double QasStd15 { get; set; }
        /// <summary>標準型 短期許容せん断力 Qas [kN]・せん断スパン比 2.0</summary>
        public double QasStd20 { get; set; }
        /// <summary>標準型 終局せん断耐力 Qu [kN]・せん断スパン比 1.0</summary>
        public double QuStd10 { get; set; }
        /// <summary>標準型 終局せん断耐力 Qu [kN]・せん断スパン比 1.5</summary>
        public double QuStd15 { get; set; }
        /// <summary>標準型 終局せん断耐力 Qu [kN]・せん断スパン比 2.0</summary>
        public double QuStd20 { get; set; }
        /// <summary>高せん断型 短期許容せん断力 Qas [kN]・せん断スパン比 1.0</summary>
        public double QasHigh10 { get; set; }
        /// <summary>高せん断型 短期許容せん断力 Qas [kN]・せん断スパン比 1.5</summary>
        public double QasHigh15 { get; set; }
        /// <summary>高せん断型 短期許容せん断力 Qas [kN]・せん断スパン比 2.0</summary>
        public double QasHigh20 { get; set; }
        /// <summary>高せん断型 終局せん断耐力 Qu [kN]・せん断スパン比 1.0</summary>
        public double QuHigh10 { get; set; }
        /// <summary>高せん断型 終局せん断耐力 Qu [kN]・せん断スパン比 1.5</summary>
        public double QuHigh15 { get; set; }
        /// <summary>高せん断型 終局せん断耐力 Qu [kN]・せん断スパン比 2.0</summary>
        public double QuHigh20 { get; set; }

        /// <summary>長期許容軸力 N [kN]</summary>
        public double Nal { get; set; }

        // ── PHC部 (同じ製品の下部。異形棒鋼を持たない) ──────────────
        /// <summary>PHC部 換算断面積 Ae [mm²] (= Ao + (np−1)Ap)</summary>
        public double PhcAe { get; set; }
        /// <summary>PHC部 換算断面 2 次モーメント Ie [mm⁴]</summary>
        public double PhcIe { get; set; }
        /// <summary>PHC部 有効プレストレス量 σce [N/mm²]</summary>
        public double PhcSigmaCe { get; set; }
        /// <summary>PHC部 ひび割れモーメント Mc [kN·m]</summary>
        public double PhcMc { get; set; }
        /// <summary>PHC部 終局モーメント Mu [kN·m]</summary>
        public double PhcMu { get; set; }
        /// <summary>PHC部 短期許容せん断力 Qas [kN]</summary>
        public double PhcQas { get; set; }
        /// <summary>PHC部 終局せん断耐力 Qu [kN]</summary>
        public double PhcQu { get; set; }
        /// <summary>PHC部 長期許容軸力 N [kN]</summary>
        public double PhcNal { get; set; }

        // ── 設計に用いる諸定数 ──────────────────────────────────────
        /// <summary>コンクリートのヤング係数 Ec [N/mm²]</summary>
        public double Ec { get; set; }
        /// <summary>PC 鋼棒の耐力 [N/mm²]</summary>
        public double Ftp { get; set; }
        /// <summary>PC 鋼棒の引張強さ [N/mm²]</summary>
        public double SigmaPu { get; set; }
        /// <summary>PC 鋼棒のヤング係数 Ep [N/mm²]</summary>
        public double Ep { get; set; }
        /// <summary>異形棒鋼の引張強さ [N/mm²]</summary>
        public double BarFtu { get; set; }
        /// <summary>異形棒鋼の降伏点応力度 [N/mm²]</summary>
        public double BarFy { get; set; }
        /// <summary>異形棒鋼のヤング係数 Er [N/mm²]</summary>
        public double Er { get; set; }
        /// <summary>異形棒鋼の長期許容応力度 [N/mm²] (D25 以下)</summary>
        public double BarAllowLong { get; set; }
        /// <summary>異形棒鋼の長期許容応力度 [N/mm²] (D29 以上)</summary>
        public double BarAllowLongD29Up { get; set; }
        /// <summary>異形棒鋼の短期許容応力度 [N/mm²]</summary>
        public double BarAllowShort { get; set; }

        /// <summary>コンクリート長期許容圧縮応力度 [N/mm²] (PRC部・PHC部 共通)</summary>
        public double FcAllowCompLong { get; set; }
        /// <summary>コンクリート短期許容圧縮応力度 [N/mm²] (PRC部・PHC部 共通)</summary>
        public double FcAllowCompShort { get; set; }
        /// <summary>PRC部 長期許容斜引張応力度 [N/mm²]。
        /// <b>PRC部には短期の斜引張の規定が無い</b> (短期は Qas 表による)。</summary>
        public double PrcAllowDiagLong { get; set; }
        /// <summary>PHC部 長期許容斜引張応力度 [N/mm²]</summary>
        public double PhcAllowDiagLong { get; set; }
        /// <summary>PHC部 短期許容斜引張応力度 [N/mm²]</summary>
        public double PhcAllowDiagShort { get; set; }
        /// <summary>PHC部 長期許容曲げ引張応力度の σce 係数 (= 1/4)</summary>
        public double PhcAllowBendTensLongFactor { get; set; }
        /// <summary>PHC部 短期許容曲げ引張応力度の σce 係数 (= 1/2)</summary>
        public double PhcAllowBendTensShortFactor { get; set; }

        // ── その他 ─────────────────────────────────────────────────
        /// <summary>単位長さ質量 [t/m] (標準質量表の「0.154×L」の係数)</summary>
        public double MassPerM { get; set; }

        // ── 姿図寸法 [mm] ──────────────────────────────────────────
        /// <summary>節ピッチ (節中心間距離)。カタログ姿図に寸法記入あり。</summary>
        public double NodePitch { get; set; }
        /// <summary>杭頭から第 1 節中心までの距離。カタログ姿図に寸法記入あり。</summary>
        public double HeadOffset { get; set; }
        /// <summary>杭先端から最終節中心までの距離。カタログ姿図に寸法記入あり。</summary>
        public double ToeOffset { get; set; }

        /// <summary>カタログ記載に矛盾がある場合の注記 (空文字なら問題なし)。</summary>
        public string Note { get; set; } = "";

        // ── 導出プロパティ ─────────────────────────────────────────
        /// <summary>軸部の内径 [mm]</summary>
        public double InnerDiameter => D - 2.0 * T;

        /// <summary>PHC部 換算断面係数 [mm³] (カタログに印字が無いので Ie から算出)</summary>
        public double PhcZe => D > 0 ? PhcIe / (D / 2.0) : 0.0;

        /// <summary>
        /// 節部テーパーの軸方向長さ [mm]。
        /// <b>カタログに寸法記入が無く、姿図の実測 (テーパーが 45°) による推定値。</b>
        /// 公称寸法ではないので、形状表示以外に使ってはならない。
        /// </summary>
        public double EstimatedTaperLength => (Do - D) / 2.0;

        /// <summary>
        /// 節部 (最大径が一定の区間) の軸方向長さ [mm]。
        /// <b>カタログに寸法記入が無く、姿図の実測による推定値。</b>
        /// </summary>
        public double EstimatedNodeFlatLength => (Do - D) / 2.0;

        /// <summary>節 1 個分の軸方向全長 [mm] (テーパー + 節部 + テーパー)。推定値。</summary>
        public double EstimatedNodeTotalLength => 2.0 * EstimatedTaperLength + EstimatedNodeFlatLength;

        /// <summary>
        /// 製品選択 ComboBox / 保存ファイルで使う表示名。
        /// 節杭 (NPH) と語順を揃える。例: <c>NPRC-440-300-標準-105-Ⅰ</c>
        /// </summary>
        public string DisplayName =>
            $"NPRC-{Do:0}-{D:0}-{ThicknessType}-{Fc:0}-{PrestressType}";

        /// <summary>
        /// 同じ製品の PHC部 の表示名。PRC部 と同じ呼び名の製品なので、
        /// 保存ファイル・製品選択で取り違えないよう接尾辞で区別する。
        /// </summary>
        public string PhcPartDisplayName => DisplayName + "-PHC部";

        /// <summary>
        /// 既存の既製杭ライブラリ DTO へ変換する。
        ///
        /// <paramref name="phcPart"/> が true なら同じ製品の <b>PHC部</b> (異形棒鋼なし)
        /// の断面として詰め替える。断面性能は軸部基準なので <see cref="D"/> / <see cref="T"/>
        /// をそのまま杭径・肉厚として渡す (鋼管を持たないので Ts = 0)。
        ///
        /// 節杭固有の <see cref="Do"/> はこの DTO に入らないため、
        /// 呼び出し側が別途 PileSection へ転記すること。
        /// </summary>
        public PrecastPile ToPrecastPile(bool phcPart = false) => new()
        {
            No = 0,
            ThicknessType = ThicknessType,
            PrestressType = PrestressType,
            Name = phcPart ? PhcPartDisplayName : DisplayName,
            PileType = phcPart ? "NPRC_PHC" : "NPRC",
            PileDiameter = D,
            PileThickness = T,
            Fc = Fc,
            // 既製杭 CSV の fc_ は短期許容圧縮、fbc は短期許容曲げ引張 (= σce/2) に対応する
            SFc = FcAllowCompShort,
            Fbc = (phcPart ? PhcSigmaCe : SigmaCe) * PhcAllowBendTensShortFactor,
            SigmaE = phcPart ? PhcSigmaCe : SigmaCe,
            Ec = Ec,
            Ap = Ap,
            Dp = Pcd,
            Ftp = Ftp,
            SigmaPu = SigmaPu,
            Ep = Ep,
            // PHC部には異形棒鋼が無い
            HasReinf = !phcPart && BarCount > 0,
            Nr = phcPart ? 0 : BarCount,
            RDesignation = phcPart ? "0" : BarDesignation,
            Ag = phcPart ? 0 : Ag,
            Dr = phcPart ? 0 : BarPcd,
            Ftr = phcPart ? 0 : BarFy,
            Er = phcPart ? 0 : Er,
            // 鋼管は無い
            Ts = 0,
            Fts = 0,
            Es = 0,
            PsSigmaY = 0,
        };

        /// <summary>杭長 L [m] に対する節の中心位置 [mm] を杭頭から順に返す。</summary>
        public IEnumerable<double> NodeCenterPositions(double pileLengthM)
        {
            double lengthMm = pileLengthM * 1000.0;
            double last = lengthMm - ToeOffset;
            for (double z = HeadOffset; z <= last + 1e-6; z += NodePitch)
                yield return z;
        }

        /// <summary>単位長さ質量から杭長 L [m] の標準質量 [t] を返す。</summary>
        public double MassForLength(double pileLengthM) => MassPerM * pileLengthM;
    }

    /// <summary>
    /// 拡頭中間径タイプ / 拡頭タイプの形状。1 つの呼び名に対し複数の拡頭径 Dt がある。
    /// NPH の <see cref="NodularPileHead"/> と違い、NPRC のカタログには質量増分の記載が無い。
    /// </summary>
    public class NodularPrcPileHead
    {
        public string Maker { get; set; } = "";
        public string Series { get; set; } = "";
        public string Shape { get; set; } = "";
        /// <summary>呼び名 (例: 440-300)</summary>
        public string Name { get; set; } = "";
        /// <summary>節部径 Do [mm]</summary>
        public double Do { get; set; }
        /// <summary>軸部径 D [mm]</summary>
        public double D { get; set; }
        /// <summary>拡頭部径 Dt [mm]</summary>
        public double Dt { get; set; }
        /// <summary>拡頭部長さ Lt [mm]</summary>
        public double Lt { get; set; }

        /// <summary>
        /// 拡頭中間径タイプ (軸部径 &lt; Dt &lt; 節部径) か。Dt が節部径以上なら拡頭タイプ。
        /// 注: φ440-300(450) / φ450-300(450) は Dt = 450 &gt; Do = 440 となる
        /// (450mm が規格の拡頭径として用意されているため)。これらは拡頭タイプ扱い。
        /// </summary>
        public bool IsIntermediateHead => Dt < Do - 1e-6;
    }

    public static class NodularPrcPileLoader
    {
        private static CsvConfiguration Config => new(CultureInfo.InvariantCulture)
        {
            // ヘッダー名でマッピングする (列順の変更に強くする)
            PrepareHeaderForMatch = args => args.Header.Trim(),
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        public static List<NodularPrcPile> LoadFromCsv(string filePath)
        {
            var list = new List<NodularPrcPile>();
            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, Config);
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    list.Add(new NodularPrcPile
                    {
                        Maker = csv.GetField<string>("Maker") ?? "",
                        Series = csv.GetField<string>("Series") ?? "",
                        Shape = csv.GetField<string>("Shape") ?? "",
                        Name = csv.GetField<string>("Name") ?? "",
                        Do = csv.GetField<double>("Do"),
                        D = csv.GetField<double>("D"),
                        T = csv.GetField<double>("t"),
                        ThicknessType = csv.GetField<string>("ThicknessType") ?? "",
                        Fc = csv.GetField<double>("Fc"),
                        PrestressType = csv.GetField<string>("PrestressType") ?? "",
                        PcDesignation = csv.GetField<string>("PcDesignation") ?? "",
                        PcCount = csv.GetField<int>("PcCount"),
                        Ap = csv.GetField<double>("Ap"),
                        Pcd = csv.GetField<double>("PCD"),
                        BarDesignation = csv.GetField<string>("BarDesignation") ?? "",
                        BarCount = csv.GetField<int>("BarCount"),
                        Ag = csv.GetField<double>("Ag"),
                        BarPcd = csv.GetField<double>("BarPCD"),
                        Ao = csv.GetField<double>("Ao"),
                        Ae = csv.GetField<double>("Ae"),
                        So = csv.GetField<double>("So"),
                        SoFromSection = csv.GetField<double>("SoFromSection"),
                        Io = csv.GetField<double>("Io"),
                        Ie = csv.GetField<double>("Ie"),
                        Ze = csv.GetField<double>("Ze"),
                        SigmaCe = csv.GetField<double>("SigmaCe"),
                        ShearBarStdDia490 = csv.GetField<double>("ShearBarStdDia490"),
                        ShearBarStdPitch490 = csv.GetField<double>("ShearBarStdPitch490"),
                        ShearBarStdDia785 = csv.GetField<double>("ShearBarStdDia785"),
                        ShearBarStdPitch785 = csv.GetField<double>("ShearBarStdPitch785"),
                        ShearBarHighDia785 = csv.GetField<double>("ShearBarHighDia785"),
                        ShearBarHighPitch785 = csv.GetField<double>("ShearBarHighPitch785"),
                        Msc = csv.GetField<double>("Msc"),
                        Mal = csv.GetField<double>("Mal"),
                        Mas = csv.GetField<double>("Mas"),
                        Mu = csv.GetField<double>("Mu"),
                        Qal = csv.GetField<double>("Qal"),
                        QasStd10 = csv.GetField<double>("QasStd10"),
                        QasStd15 = csv.GetField<double>("QasStd15"),
                        QasStd20 = csv.GetField<double>("QasStd20"),
                        QuStd10 = csv.GetField<double>("QuStd10"),
                        QuStd15 = csv.GetField<double>("QuStd15"),
                        QuStd20 = csv.GetField<double>("QuStd20"),
                        QasHigh10 = csv.GetField<double>("QasHigh10"),
                        QasHigh15 = csv.GetField<double>("QasHigh15"),
                        QasHigh20 = csv.GetField<double>("QasHigh20"),
                        QuHigh10 = csv.GetField<double>("QuHigh10"),
                        QuHigh15 = csv.GetField<double>("QuHigh15"),
                        QuHigh20 = csv.GetField<double>("QuHigh20"),
                        Nal = csv.GetField<double>("Nal"),
                        PhcAe = csv.GetField<double>("PhcAe"),
                        PhcIe = csv.GetField<double>("PhcIe"),
                        PhcSigmaCe = csv.GetField<double>("PhcSigmaCe"),
                        PhcMc = csv.GetField<double>("PhcMc"),
                        PhcMu = csv.GetField<double>("PhcMu"),
                        PhcQas = csv.GetField<double>("PhcQas"),
                        PhcQu = csv.GetField<double>("PhcQu"),
                        PhcNal = csv.GetField<double>("PhcNal"),
                        Ec = csv.GetField<double>("Ec"),
                        Ftp = csv.GetField<double>("Ftp"),
                        SigmaPu = csv.GetField<double>("SigmaPu"),
                        Ep = csv.GetField<double>("Ep"),
                        BarFtu = csv.GetField<double>("BarFtu"),
                        BarFy = csv.GetField<double>("BarFy"),
                        Er = csv.GetField<double>("Er"),
                        BarAllowLong = csv.GetField<double>("BarAllowLong"),
                        BarAllowLongD29Up = csv.GetField<double>("BarAllowLongD29Up"),
                        BarAllowShort = csv.GetField<double>("BarAllowShort"),
                        FcAllowCompLong = csv.GetField<double>("FcAllowCompLong"),
                        FcAllowCompShort = csv.GetField<double>("FcAllowCompShort"),
                        PrcAllowDiagLong = csv.GetField<double>("PrcAllowDiagLong"),
                        PhcAllowDiagLong = csv.GetField<double>("PhcAllowDiagLong"),
                        PhcAllowDiagShort = csv.GetField<double>("PhcAllowDiagShort"),
                        PhcAllowBendTensLongFactor = csv.GetField<double>("PhcAllowBendTensLongFactor"),
                        PhcAllowBendTensShortFactor = csv.GetField<double>("PhcAllowBendTensShortFactor"),
                        MassPerM = csv.GetField<double>("MassPerM"),
                        NodePitch = csv.GetField<double>("NodePitch"),
                        HeadOffset = csv.GetField<double>("HeadOffset"),
                        ToeOffset = csv.GetField<double>("ToeOffset"),
                        Note = csv.GetField<string>("Note") ?? "",
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[NodularPrcPileLoader] 読込失敗 ({filePath}): {ex.Message}");
            }
            return list;
        }

        public static List<NodularPrcPileHead> LoadHeadsFromCsv(string filePath)
        {
            var list = new List<NodularPrcPileHead>();
            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, Config);
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    list.Add(new NodularPrcPileHead
                    {
                        Maker = csv.GetField<string>("Maker") ?? "",
                        Series = csv.GetField<string>("Series") ?? "",
                        Shape = csv.GetField<string>("Shape") ?? "",
                        Name = csv.GetField<string>("Name") ?? "",
                        Do = csv.GetField<double>("Do"),
                        D = csv.GetField<double>("D"),
                        Dt = csv.GetField<double>("Dt"),
                        Lt = csv.GetField<double>("Lt"),
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[NodularPrcPileLoader] 拡頭形状読込失敗 ({filePath}): {ex.Message}");
            }
            return list;
        }

        /// <summary>アプリ配置先の既定 CSV から PRC 節杭ライブラリを読み込む。</summary>
        public static List<NodularPrcPile> LoadDefault() =>
            LoadFromCsv(DefaultPath("pile_library_NodularPrcPile.csv"));

        /// <summary>アプリ配置先の既定 CSV から拡頭形状一覧を読み込む。</summary>
        public static List<NodularPrcPileHead> LoadDefaultHeads() =>
            LoadHeadsFromCsv(DefaultPath("pile_library_NodularPrcPile_head.csv"));

        private static string DefaultPath(string fileName) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "PileLibrary", fileName);
    }
}
