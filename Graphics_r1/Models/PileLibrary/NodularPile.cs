using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// 節杭 (Nodular pile) 製品ライブラリの 1 レコード。
    ///
    /// メーカー横断の汎用スキーマ。現在の収録はジャパンパイル JP-NPH85/105/123
    /// (プレストレスト高強度コンクリート節杭) のみだが、他社の節杭も
    /// <see cref="Maker"/> / <see cref="Series"/> を変えて同じ CSV に追加できる。
    ///
    /// 断面性能 (<see cref="Ao"/>, <see cref="Io"/> 等) はすべて<b>軸部</b>基準の
    /// 中空円形断面である。節部は断面性能に寄与せず、支持力 (周面摩擦) と
    /// 形状表示にのみ効く。したがって既製 PHC 杭の断面計算ロジックがそのまま使える。
    /// </summary>
    public class NodularPile
    {
        // ── 製品同定 ────────────────────────────────────────────────
        /// <summary>製造者名 (例: ジャパンパイル)</summary>
        public string Maker { get; set; } = "";
        /// <summary>製品シリーズ名 (例: JP-NPH85)</summary>
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
        /// <summary>種類 (A / AH / A2 / A2H / B / BH / C / CH)</summary>
        public string PrestressType { get; set; } = "";

        // ── PC 鋼棒 ────────────────────────────────────────────────
        /// <summary>PC 鋼棒の呼び名 [mm] (7.1 / 9.0 / 10.0 / 11.2)</summary>
        public string PcDesignation { get; set; } = "";
        /// <summary>PC 鋼棒の本数</summary>
        public int PcCount { get; set; }
        /// <summary>PC 鋼棒の断面積合計 Ap [mm²]</summary>
        public double Ap { get; set; }
        /// <summary>配筋径 PCD [mm]</summary>
        public double Pcd { get; set; }

        // ── 断面諸数値 (軸部基準、カタログ記載値) ───────────────────
        /// <summary>断面積 Ao [mm²]</summary>
        public double Ao { get; set; }
        /// <summary>換算断面積 Ae [mm²]</summary>
        public double Ae { get; set; }
        /// <summary>断面 2 次モーメント Io [mm⁴]</summary>
        public double Io { get; set; }
        /// <summary>換算断面 2 次モーメント Ie [mm⁴]</summary>
        public double Ie { get; set; }
        /// <summary>換算断面係数 Ze [mm³] — カタログ印字値</summary>
        public double Ze { get; set; }
        /// <summary>Ie/(D/2) から算出した換算断面係数 [mm³]。
        /// カタログ印字の誤植がある行ではこちらが正しい (<see cref="Note"/> 参照)。</summary>
        public double ZeFromIe { get; set; }

        // ── 断面性能 (カタログ記載値) ───────────────────────────────
        /// <summary>有効プレストレス量 σce 規定値 [N/mm²] (JIS A 5373 附属書E)</summary>
        public double SigmaCeSpec { get; set; }
        /// <summary>有効プレストレス量 σce 計算値 [N/mm²]</summary>
        public double SigmaCeCalc { get; set; }
        /// <summary>長期許容軸力 N [kN]</summary>
        public double Nal { get; set; }
        /// <summary>長期許容曲げモーメント Mal [kN·m] (軸力 0 時)</summary>
        public double Mal { get; set; }
        /// <summary>短期許容曲げモーメント Mas [kN·m] (軸力 0 時)</summary>
        public double Mas { get; set; }
        /// <summary>ひび割れモーメント Mc [kN·m] (軸力 0 時)</summary>
        public double Mc { get; set; }
        /// <summary>終局モーメント Mu [kN·m] (軸力 0 時)</summary>
        public double Mu { get; set; }
        /// <summary>長期許容せん断力 Qal [kN]</summary>
        public double Qal { get; set; }
        /// <summary>短期許容せん断力 Qas [kN]</summary>
        public double Qas { get; set; }
        /// <summary>終局せん断耐力 Qu [kN]</summary>
        public double Qu { get; set; }

        // ── 設計に用いる諸定数 ──────────────────────────────────────
        /// <summary>コンクリートのヤング係数 Ec [N/mm²]</summary>
        public double Ec { get; set; }
        /// <summary>PC 鋼棒の耐力 [N/mm²]</summary>
        public double Ftp { get; set; }
        /// <summary>PC 鋼棒の引張強さ [N/mm²]</summary>
        public double SigmaPu { get; set; }
        /// <summary>PC 鋼棒のヤング係数 Ep [N/mm²]</summary>
        public double Ep { get; set; }
        /// <summary>コンクリート長期許容圧縮応力度 [N/mm²]</summary>
        public double FcAllowCompLong { get; set; }
        /// <summary>コンクリート短期許容圧縮応力度 [N/mm²]</summary>
        public double FcAllowCompShort { get; set; }
        /// <summary>コンクリート長期許容斜引張応力度 [N/mm²]</summary>
        public double FcAllowDiagLong { get; set; }
        /// <summary>コンクリート短期許容斜引張応力度 [N/mm²]</summary>
        public double FcAllowDiagShort { get; set; }

        // ── その他 ─────────────────────────────────────────────────
        /// <summary>単位長さ質量 [t/m] (標準質量表)</summary>
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

        /// <summary>
        /// 節部テーパーの軸方向長さ [mm]。
        /// <b>カタログに寸法記入が無く、姿図の実測 (テーパーが 45°) による推定値。</b>
        /// 公称寸法ではないので、形状表示以外に使ってはならない。
        /// </summary>
        public double EstimatedTaperLength => (Do - D) / 2.0;

        /// <summary>
        /// 節部 (最大径が一定の区間) の軸方向長さ [mm]。
        /// <b>カタログに寸法記入が無く、姿図の実測による推定値。</b>
        /// 姿図では テーパー長 と等しく描かれている。
        /// </summary>
        public double EstimatedNodeFlatLength => (Do - D) / 2.0;

        /// <summary>節 1 個分の軸方向全長 [mm] (テーパー + 節部 + テーパー)。推定値。</summary>
        public double EstimatedNodeTotalLength => 2.0 * EstimatedTaperLength + EstimatedNodeFlatLength;

        /// <summary>
        /// 製品選択 ComboBox / 保存ファイルで使う表示名。
        /// 既製 PHC 杭の <c>PHC-300-標準-80-A</c> と語順を揃え、節杭は節部径と軸部径を併記する。
        /// 例: <c>NPH-440-300-標準-85-A</c>
        /// </summary>
        public string DisplayName =>
            $"NPH-{Do:0}-{D:0}-{ThicknessType}-{Fc:0}-{PrestressType}";

        /// <summary>
        /// 既存の既製杭ライブラリ DTO へ変換する。
        ///
        /// <see cref="PileDesign.Models.InputData.PileSection"/> の製品転記処理は
        /// <see cref="PrecastPile"/> を前提にしているため、ここで詰め替えることで
        /// 転記ロジックを新規に書かずに済む。断面性能は軸部基準なので
        /// <see cref="D"/> / <see cref="T"/> をそのまま杭径・肉厚として渡す
        /// (鋼管を持たないので Ts = 0 → ConcreteOutDia = D, ConcreteThickness = t となる)。
        ///
        /// 節杭固有の <see cref="Do"/> / <see cref="MassPerM"/> はこの DTO に入らないため、
        /// 呼び出し側が別途 PileSection へ転記すること。
        /// </summary>
        public PrecastPile ToPrecastPile() => new()
        {
            No = 0,
            ThicknessType = ThicknessType,
            PrestressType = PrestressType,
            Name = DisplayName,
            PileType = "NPH",
            PileDiameter = D,
            PileThickness = T,
            Fc = Fc,
            // 既製杭 CSV の fc_ は短期許容圧縮、fbc は短期許容曲げ引張 (= σce/2) に対応する
            SFc = FcAllowCompShort,
            Fbc = SigmaCeCalc / 2.0,
            // 設計値はカタログ注記どおり有効プレストレスの「計算値」を用いる
            SigmaE = SigmaCeCalc,
            Ec = Ec,
            Ap = Ap,
            Dp = Pcd,
            Ftp = Ftp,
            SigmaPu = SigmaPu,
            Ep = Ep,
            // 節杭に主筋・鋼管はない
            HasReinf = false,
            Nr = 0,
            RDesignation = "0",
            Ag = 0,
            Dr = 0,
            Ftr = 0,
            Er = 0,
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
    /// </summary>
    public class NodularPileHead
    {
        public string Maker { get; set; } = "";
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
        /// <summary>標準質量の増分 [t] (質量 = m + MassAdd, m は軸部の標準質量)</summary>
        public double MassAdd { get; set; }

        /// <summary>
        /// 拡頭中間径タイプ (軸部径 &lt; Dt &lt; 節部径) か。Dt が節部径以上なら拡頭タイプ。
        /// 注: φ440-300(450) / φ450-300(450) は Dt = 450 &gt; Do = 440 となる
        /// (450mm が規格の拡頭径として用意されているため)。これらは拡頭タイプ扱い。
        /// </summary>
        public bool IsIntermediateHead => Dt < Do - 1e-6;
    }

    public static class NodularPileLoader
    {
        private static CsvConfiguration Config => new(CultureInfo.InvariantCulture)
        {
            // ヘッダー名でマッピングする (列順の変更に強くする)
            PrepareHeaderForMatch = args => args.Header.Trim(),
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        public static List<NodularPile> LoadFromCsv(string filePath)
        {
            var list = new List<NodularPile>();
            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, Config);
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    list.Add(new NodularPile
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
                        Ao = csv.GetField<double>("Ao"),
                        Ae = csv.GetField<double>("Ae"),
                        Io = csv.GetField<double>("Io"),
                        Ie = csv.GetField<double>("Ie"),
                        Ze = csv.GetField<double>("Ze"),
                        ZeFromIe = csv.GetField<double>("ZeFromIe"),
                        SigmaCeSpec = csv.GetField<double>("SigmaCeSpec"),
                        SigmaCeCalc = csv.GetField<double>("SigmaCeCalc"),
                        Nal = csv.GetField<double>("Nal"),
                        Mal = csv.GetField<double>("Mal"),
                        Mas = csv.GetField<double>("Mas"),
                        Mc = csv.GetField<double>("Mc"),
                        Mu = csv.GetField<double>("Mu"),
                        Qal = csv.GetField<double>("Qal"),
                        Qas = csv.GetField<double>("Qas"),
                        Qu = csv.GetField<double>("Qu"),
                        Ec = csv.GetField<double>("Ec"),
                        Ftp = csv.GetField<double>("Ftp"),
                        SigmaPu = csv.GetField<double>("SigmaPu"),
                        Ep = csv.GetField<double>("Ep"),
                        FcAllowCompLong = csv.GetField<double>("FcAllowCompLong"),
                        FcAllowCompShort = csv.GetField<double>("FcAllowCompShort"),
                        FcAllowDiagLong = csv.GetField<double>("FcAllowDiagLong"),
                        FcAllowDiagShort = csv.GetField<double>("FcAllowDiagShort"),
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
                Serilog.Log.Debug($"[NodularPileLoader] 読込失敗 ({filePath}): {ex.Message}");
            }
            return list;
        }

        public static List<NodularPileHead> LoadHeadsFromCsv(string filePath)
        {
            var list = new List<NodularPileHead>();
            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, Config);
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    list.Add(new NodularPileHead
                    {
                        Maker = csv.GetField<string>("Maker") ?? "",
                        Shape = csv.GetField<string>("Shape") ?? "",
                        Name = csv.GetField<string>("Name") ?? "",
                        Do = csv.GetField<double>("Do"),
                        D = csv.GetField<double>("D"),
                        Dt = csv.GetField<double>("Dt"),
                        Lt = csv.GetField<double>("Lt"),
                        MassAdd = csv.GetField<double>("MassAdd"),
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[NodularPileLoader] 拡頭形状読込失敗 ({filePath}): {ex.Message}");
            }
            return list;
        }

        /// <summary>アプリ配置先の既定 CSV から節杭ライブラリを読み込む。</summary>
        public static List<NodularPile> LoadDefault() =>
            LoadFromCsv(DefaultPath("pile_library_NodularPile.csv"));

        /// <summary>アプリ配置先の既定 CSV から拡頭形状一覧を読み込む。</summary>
        public static List<NodularPileHead> LoadDefaultHeads() =>
            LoadHeadsFromCsv(DefaultPath("pile_library_NodularPile_head.csv"));

        private static string DefaultPath(string fileName) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "PileLibrary", fileName);
    }
}
