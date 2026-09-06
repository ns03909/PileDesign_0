using System;
using System.Globalization;

namespace TestProject1.LiteratureVerification
{
    /// <summary>許容の決め方。文献ごとに「どこまでを一致と呼ぶか」が違う。</summary>
    public enum ToleranceKind
    {
        /// <summary>|program − literature| / |literature| ≦ Tolerance。</summary>
        Relative,

        /// <summary>|program − literature| ≦ Tolerance (単位は項目の単位)。</summary>
        Absolute,

        /// <summary>
        /// literature / Tolerance ≦ program ≦ literature × Tolerance。
        /// 「倍半分」(Tolerance = 2) のようにチャート読み取りの幅を文献が明記している場合。
        /// </summary>
        Factor,

        /// <summary>
        /// floor(program) == literature。カタログの表が小数を切り捨てて載せている場合。
        /// </summary>
        FloorEqual,
    }

    /// <summary>
    /// 出典の性格。表を分けて出すための区分。
    ///
    /// 学会の指針・計算例は設計法そのものの再現で、認定工法のカタログは
    /// メーカーが評定・認定の範囲で公表している算定式の再現。読む側の受け止め方が違うので、
    /// 検証ウィンドウでは別ページにし、README でも表を分ける。
    /// </summary>
    public enum SourceKind
    {
        /// <summary>学会の指針・計算例 (建築基礎構造設計指針、基礎部材の強度と変形性能 など)。</summary>
        AcademicStandard,

        /// <summary>認定工法のカタログ・設計マニュアル (杭先端の支持力算定など)。</summary>
        CertifiedMethod,
    }

    /// <summary>
    /// 文献の値 1 つと、それを本プログラムで求める手続きの組。
    ///
    /// テストはこれを並べたカタログ (<see cref="LiteratureCatalog"/>) を回して
    /// 許容内かを確かめ、同じカタログから検証ウィンドウと README の表を生成する。
    /// 「テストでは通っているが表には別の値が書いてある」を起こさないため、
    /// 値も許容も 1 か所にしか持たない。
    /// </summary>
    public sealed record LiteratureCheck(
        string Source,
        string Section,
        string Item,
        string Unit,
        double Literature,
        ToleranceKind Kind,
        double Tolerance,
        Func<double> Program,
        string? Note = null,
        SourceKind SourceKind = SourceKind.AcademicStandard)
    {
        /// <summary>表に出す許容の表記。</summary>
        public string ToleranceLabel => Kind switch
        {
            ToleranceKind.Relative => $"±{Tolerance * 100:0.#}%",
            ToleranceKind.Absolute => $"±{FormatValue(Tolerance)} {Unit}".TrimEnd(),
            ToleranceKind.Factor => Tolerance == 2.0 ? "倍半分" : $"×/÷{Tolerance:0.##}",
            ToleranceKind.FloorEqual => "切り捨て一致",
            _ => "",
        };

        public bool Passes(double program) => Kind switch
        {
            ToleranceKind.Relative => Literature != 0
                ? Math.Abs(program - Literature) / Math.Abs(Literature) <= Tolerance + 1e-12
                : Math.Abs(program) <= Tolerance,
            ToleranceKind.Absolute => Math.Abs(program - Literature) <= Tolerance + 1e-12,
            ToleranceKind.Factor => program >= Literature / Tolerance - 1e-9 && program <= Literature * Tolerance + 1e-9,
            ToleranceKind.FloorEqual => Math.Floor(program) == Literature,
            _ => false,
        };

        /// <summary>表に出す値の書式。桁数は値の大きさで決め、実行ごとの末尾桁の揺れを表に持ち込まない。</summary>
        public static string FormatValue(double v)
        {
            if (double.IsNaN(v)) return "—";
            double a = Math.Abs(v);
            // 末尾の 0 は落とす (文献の「4」を「4.000」と書かない)
            string fmt = a >= 1000 ? "#,##0" : a >= 100 ? "0.#" : a >= 10 ? "0.##" : "0.###";
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>カタログ 1 件を実際に計算した結果。</summary>
    public sealed record LiteratureCheckResult(LiteratureCheck Check, double Program)
    {
        public bool Ok => Check.Passes(Program);

        /// <summary>
        /// 表に出すプログラム値。切り捨て一致の項目は「切り捨てた値 (元の値)」で出す。
        /// 654.96 を四捨五入して 655 と書くと、文献の 654 と「切り捨て一致」が読めなくなるため。
        /// </summary>
        public string ProgramText => Check.Kind == ToleranceKind.FloorEqual
            ? $"{Math.Floor(Program).ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture)} ({Program.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})"
            : LiteratureCheck.FormatValue(Program);

        /// <summary>相対差 (%)。文献値が 0 のときは null。</summary>
        public double? RelativeDiffPercent =>
            Check.Literature != 0 ? (Program - Check.Literature) / Math.Abs(Check.Literature) * 100.0 : null;
    }
}
