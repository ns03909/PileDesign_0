using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// 頭部厚型節付き杭 (三谷セキサン BF.S105 / BF.S123 パイル) 製品ライブラリの 1 レコード。
    ///
    /// <see cref="NodularPile"/> (JP-NPH) との最大の違いは、1 本の杭が
    /// <b>頭部軸部</b> (<see cref="HeadDia"/> / <see cref="HeadThickness"/>) と
    /// <b>先端軸部</b> (<see cref="TipDia"/> / <see cref="TipThickness"/>) という
    /// <b>外径の違う 2 つの軸部</b>から成ること。内径は両者で完全に一致しており
    /// (Dt − 2Tt = D0 − 2T)、頭部だけが外側に厚い。
    /// PC 鋼棒は本数・断面積・配筋径とも両軸部で共通。
    ///
    /// カタログの標準性能表は<b>頭部軸部の値のみ</b>を載せている (表の注記)。
    /// 先端軸部の断面性能 (<see cref="TipAo"/> 等) は取り込み時に算定した値で、
    /// 耐力 (Mcr / Mu / Qas 等) はカタログに存在しないため
    /// アプリ側の断面計算 (PHCSection) の結果を用いる。
    /// </summary>
    public class BfsPile
    {
        // ── 製品同定 ────────────────────────────────────────────────
        /// <summary>製造者名 (三谷セキサン)</summary>
        public string Maker { get; set; } = "";
        /// <summary>製品シリーズ名 (BF.S105 / BF.S123)</summary>
        public string Series { get; set; } = "";
        /// <summary>杭形状の種別 (頭部厚型節付き杭)</summary>
        public string Shape { get; set; } = "";
        /// <summary>呼び名 (例: 400-3045)。「頭部軸部径-先端軸部径+先端肉厚」を表す。</summary>
        public string Name { get; set; } = "";
        /// <summary>コンクリート設計基準強度 [N/mm²] (105 / 123)</summary>
        public double Fc { get; set; }
        /// <summary>種類 (A2 / B2 / C2)</summary>
        public string PrestressType { get; set; } = "";

        // ── 形状 [mm] ───────────────────────────────────────────────
        /// <summary>頭部軸部径 Dt</summary>
        public double HeadDia { get; set; }
        /// <summary>頭部軸部の肉厚 Tt</summary>
        public double HeadThickness { get; set; }
        /// <summary>先端軸部径 D0</summary>
        public double TipDia { get; set; }
        /// <summary>先端軸部の肉厚 T</summary>
        public double TipThickness { get; set; }
        /// <summary>節部径 D1</summary>
        public double NodeDia { get; set; }

        // ── PC 鋼棒 (両軸部共通) ───────────────────────────────────
        /// <summary>PC 鋼棒の呼び名 [mm]</summary>
        public string PcDesignation { get; set; } = "";
        /// <summary>PC 鋼棒の本数</summary>
        public int PcCount { get; set; }
        /// <summary>PC 鋼棒の断面積合計 Ap [mm²]</summary>
        public double Ap { get; set; }
        /// <summary>
        /// 配筋径 PCD [mm]。
        /// <b>カタログに印字が無いため、頭部軸部の Ie から逆算した値。</b>
        /// 同一呼び名では Fc・種類によらず一致し 5mm 丸めに乗ることを取り込み時に確認している
        /// (tools/pile-catalog/build_bfs.py)。
        /// </summary>
        public double Pcd { get; set; }

        // ── 頭部軸部 (カタログ記載値) ───────────────────────────────
        /// <summary>断面積 Ao [mm²]</summary>
        public double HeadAo { get; set; }
        /// <summary>換算断面積 Ae [mm²]</summary>
        public double HeadAe { get; set; }
        /// <summary>換算断面 2 次モーメント Ie [mm⁴]</summary>
        public double HeadIe { get; set; }
        /// <summary>有効プレストレス量 σce [N/mm²]</summary>
        public double HeadSigmaCe { get; set; }
        /// <summary>長期許容曲げモーメント Mal [kN·m] (軸力 0 時)</summary>
        public double HeadMal { get; set; }
        /// <summary>短期許容曲げモーメント Mas [kN·m] (軸力 0 時)</summary>
        public double HeadMas { get; set; }
        /// <summary>ひび割れモーメント Mcr [kN·m] (軸力 0 時)</summary>
        public double HeadMcr { get; set; }
        /// <summary>破壊モーメント Mu [kN·m] (軸力 0 時)</summary>
        public double HeadMu { get; set; }
        /// <summary>長期許容せん断力 Qal [kN]</summary>
        public double HeadQal { get; set; }
        /// <summary>短期許容せん断力 Qas [kN]</summary>
        public double HeadQas { get; set; }
        /// <summary>ひび割れせん断力 Qcr [kN]</summary>
        public double HeadQcr { get; set; }
        /// <summary>長期許容軸力 N [kN]</summary>
        public double HeadNal { get; set; }

        // ── 先端軸部 (算定値。カタログに記載が無い) ─────────────────
        /// <summary>断面積 Ao [mm²]。<b>算定値</b></summary>
        public double TipAo { get; set; }
        /// <summary>換算断面積 Ae [mm²]。<b>算定値</b></summary>
        public double TipAe { get; set; }
        /// <summary>換算断面 2 次モーメント Ie [mm⁴]。<b>算定値</b></summary>
        public double TipIe { get; set; }
        /// <summary>
        /// 有効プレストレス量 σce [N/mm²]。<b>算定値</b>。
        /// 有効プレストレス力 P = σce·Ae は同じ杭なので両軸部で共通、という前提で
        /// σce(先端) = σce(頭部)·Ae(頭部)/Ae(先端) とした。
        /// 結果が JIS A 5373 の A/B/C 種の規定値 (4 / 8 / 10 N/mm²) に ±5% で乗ることを
        /// 取り込み時に確認している。
        /// </summary>
        public double TipSigmaCe { get; set; }

        // ── 姿図寸法 [mm] ──────────────────────────────────────────
        /// <summary>節ピッチ (節中心間距離)。カタログ標準構造図の寸法記入値。</summary>
        public double NodePitch { get; set; }
        /// <summary>
        /// 杭頭から第 1 節中心までの距離。
        /// <b>カタログに寸法記入が無い導出値</b>。杭長が 1m 単位で節ピッチ 1000・
        /// 先端 500 であることから 500 が唯一整合する。
        /// </summary>
        public double HeadOffset { get; set; }
        /// <summary>杭先端から最終節中心までの距離。カタログ標準構造図の寸法記入値。</summary>
        public double ToeOffset { get; set; }
        /// <summary>
        /// 節部 (最大径が一定の区間) の軸方向長さ。カタログ標準構造図の寸法記入値
        /// (75mm、φ800-7090〜φ1200-110130 は 100mm) で、(D1 − D0)/2 に一致する。
        /// テーパーは 45° (軸方向長さ = 半径差) で、これも寸法記入値と整合する。
        /// </summary>
        public double NodeFlatLength { get; set; }

        // ── 設計に用いる諸数値 ──────────────────────────────────────
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
        /// <summary>長期許容曲げ引張応力度の σce 係数 (= 1/4)</summary>
        public double AllowBendTensLongFactor { get; set; }
        /// <summary>短期許容曲げ引張応力度の σce 係数 (= 1/2)</summary>
        public double AllowBendTensShortFactor { get; set; }

        // ── 導出プロパティ ─────────────────────────────────────────
        /// <summary>軸部の内径 [mm]。頭部軸部・先端軸部で共通。</summary>
        public double InnerDiameter => HeadDia - 2.0 * HeadThickness;

        /// <summary>製品選択 ComboBox / 保存ファイルで使う表示名 (頭部軸部)。</summary>
        public string DisplayName => $"BF.S-{Name}-{Fc:0}-{PrestressType}";

        /// <summary>同じ製品の先端軸部の表示名。</summary>
        public string TipDisplayName => DisplayName + "-先端軸部";

        /// <summary>
        /// 既存の既製杭ライブラリ DTO へ変換する。
        /// <paramref name="tipPart"/> が true なら先端軸部の断面として詰め替える。
        /// 節部径・節寸法は DTO に入らないため、呼び出し側が別途 PileSection へ転記すること。
        /// </summary>
        public PrecastPile ToPrecastPile(bool tipPart = false) => new()
        {
            No = 0,
            ThicknessType = tipPart ? "先端軸部" : "頭部軸部",
            PrestressType = PrestressType,
            Name = tipPart ? TipDisplayName : DisplayName,
            PileType = tipPart ? "BFS_TIP" : "BFS_HEAD",
            PileDiameter = tipPart ? TipDia : HeadDia,
            PileThickness = tipPart ? TipThickness : HeadThickness,
            Fc = Fc,
            // 既製杭 CSV の fc_ は短期許容圧縮、fbc は短期許容曲げ引張 (= σce/2) に対応する
            SFc = FcAllowCompShort,
            Fbc = (tipPart ? TipSigmaCe : HeadSigmaCe) * AllowBendTensShortFactor,
            SigmaE = tipPart ? TipSigmaCe : HeadSigmaCe,
            Ec = Ec,
            Ap = Ap,
            Dp = Pcd,
            Ftp = Ftp,
            SigmaPu = SigmaPu,
            Ep = Ep,
            // 主筋・鋼管は持たない
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
    }

    public static class BfsPileLoader
    {
        private static CsvConfiguration Config => new(CultureInfo.InvariantCulture)
        {
            // ヘッダー名でマッピングする (列順の変更に強くする)
            PrepareHeaderForMatch = args => args.Header.Trim(),
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        public static List<BfsPile> LoadFromCsv(string filePath)
        {
            var list = new List<BfsPile>();
            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, Config);
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    list.Add(new BfsPile
                    {
                        Maker = csv.GetField<string>("Maker") ?? "",
                        Series = csv.GetField<string>("Series") ?? "",
                        Shape = csv.GetField<string>("Shape") ?? "",
                        Name = csv.GetField<string>("Name") ?? "",
                        Fc = csv.GetField<double>("Fc"),
                        PrestressType = csv.GetField<string>("PrestressType") ?? "",
                        HeadDia = csv.GetField<double>("HeadDia"),
                        HeadThickness = csv.GetField<double>("HeadThickness"),
                        TipDia = csv.GetField<double>("TipDia"),
                        TipThickness = csv.GetField<double>("TipThickness"),
                        NodeDia = csv.GetField<double>("NodeDia"),
                        PcDesignation = csv.GetField<string>("PcDesignation") ?? "",
                        PcCount = csv.GetField<int>("PcCount"),
                        Ap = csv.GetField<double>("Ap"),
                        Pcd = csv.GetField<double>("Pcd"),
                        HeadAo = csv.GetField<double>("HeadAo"),
                        HeadAe = csv.GetField<double>("HeadAe"),
                        HeadIe = csv.GetField<double>("HeadIe"),
                        HeadSigmaCe = csv.GetField<double>("HeadSigmaCe"),
                        HeadMal = csv.GetField<double>("HeadMal"),
                        HeadMas = csv.GetField<double>("HeadMas"),
                        HeadMcr = csv.GetField<double>("HeadMcr"),
                        HeadMu = csv.GetField<double>("HeadMu"),
                        HeadQal = csv.GetField<double>("HeadQal"),
                        HeadQas = csv.GetField<double>("HeadQas"),
                        HeadQcr = csv.GetField<double>("HeadQcr"),
                        HeadNal = csv.GetField<double>("HeadNal"),
                        TipAo = csv.GetField<double>("TipAo"),
                        TipAe = csv.GetField<double>("TipAe"),
                        TipIe = csv.GetField<double>("TipIe"),
                        TipSigmaCe = csv.GetField<double>("TipSigmaCe"),
                        NodePitch = csv.GetField<double>("NodePitch"),
                        HeadOffset = csv.GetField<double>("HeadOffset"),
                        ToeOffset = csv.GetField<double>("ToeOffset"),
                        NodeFlatLength = csv.GetField<double>("NodeFlatLength"),
                        Ec = csv.GetField<double>("Ec"),
                        Ftp = csv.GetField<double>("Ftp"),
                        SigmaPu = csv.GetField<double>("SigmaPu"),
                        Ep = csv.GetField<double>("Ep"),
                        FcAllowCompLong = csv.GetField<double>("FcAllowCompLong"),
                        FcAllowCompShort = csv.GetField<double>("FcAllowCompShort"),
                        FcAllowDiagLong = csv.GetField<double>("FcAllowDiagLong"),
                        FcAllowDiagShort = csv.GetField<double>("FcAllowDiagShort"),
                        AllowBendTensLongFactor = csv.GetField<double>("AllowBendTensLongFactor"),
                        AllowBendTensShortFactor = csv.GetField<double>("AllowBendTensShortFactor"),
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[BfsPileLoader] 読込失敗 ({filePath}): {ex.Message}");
            }
            return list;
        }

        /// <summary>アプリ配置先の既定 CSV から BF.S パイルのライブラリを読み込む。</summary>
        public static List<BfsPile> LoadDefault() =>
            LoadFromCsv(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                     "Models", "PileLibrary", "pile_library_BfsPile.csv"));
    }
}
