using System;
using System.Globalization;

namespace PileDesign.Models.Results
{
    /// <summary>検定の種類。応答値・限界値の意味と、表示の書式がこれで決まる。</summary>
    public enum EvaluationKind
    {
        /// <summary>杭体の曲げ。応答 = |M|、限界 = N-M 相関曲線から軸力で補間した許容 M。</summary>
        PileSectionMoment,

        /// <summary>杭頭の回転角 (場所打ちRC杭)。応答 = θ、限界 = 1/100 rad。</summary>
        PileHeadRotation,

        /// <summary>基礎梁の傾斜角。応答 = |ΔUz|/L、限界 = 1/300。</summary>
        FoundationBeamInclination,
    }

    /// <summary>
    /// 検定 1 件。
    ///
    /// 従来は「[OK]/[NG] の行を積んだテキスト」しか残らず、
    /// <b>応答値と限界値を両方計算していながら比を捨てていた</b>ため、
    /// 余裕度も支配ケースも分からなかった。1 件を数値で持つことで、
    /// 検定比の並べ替え・支配ケースの特定・案の比較ができるようになる。
    ///
    /// 表示テキストはこの型から組み立てる (<c>EvaluationTextFormatter</c>)。
    /// 従来の出力と 1 文字も変えないため、書式の再現に要る素材
    /// (<see cref="LimitName"/> や <see cref="EndLabel"/> など) も持たせている。
    /// </summary>
    public sealed record EvaluationItem : IHasLoadCondition
    {
        public EvaluationKind Kind { get; init; }

        /// <summary>地震動レベル (1 / 2)。基礎梁の傾斜角は水平解析の外なので 0。</summary>
        public int Level { get; init; }

        /// <summary>画面の一覧に出す分類名。例:「杭体曲げ (安全限界)」</summary>
        [ResultColumn("検定項目", 2, tooltip: "何を照査しているか")]
        public string Category { get; init; } = "";

        /// <summary>限界状態の名称 (「損傷限界」「安全限界」)。テキスト再現に使う。</summary>
        public string LimitName { get; init; } = "";

        /// <summary>対象の名称。梁要素名 / 回転ばね名 / 「梁 #3」。</summary>
        public string TargetName { get; init; } = "";

        /// <summary>「i端」「j端」。端の区別が無い検定では空。</summary>
        public string EndLabel { get; init; } = "";

        public int PileBodyNo { get; init; }

        /// <summary>杭体区間の番号。区間の区別が無い検定では null。</summary>
        public int? SegmentIndex { get; init; }

        [ResultColumn("荷重ケース", 4, tooltip: "この検定を行った荷重ケース")]
        public string LoadCaseName { get; init; } = "";

        [ResultColumn("荷重組合せ", 5, tooltip: "この検定を行った荷重組合せ (αL / βU / βL)")]
        public string LoadCombinationName { get; init; } = "";

        /// <summary>
        /// 液状化を考慮したケースか。液状化の概念が無い検定 (基礎梁の傾斜角) では null。
        /// 条件フィルタで行を絞るのに使う。
        /// </summary>
        public bool? IsLiquefaction { get; init; }

        /// <summary>「液状化有」「液状化無」。概念が無い検定では空。</summary>
        [ResultColumn("液状化", 6, tooltip: "液状化を考慮したケースか")]
        public string LiquefactionLabel =>
            IsLiquefaction is bool b ? (b ? "液状化有" : "液状化無") : "";

        /// <summary>応答値 (解析から得た値)。</summary>
        public double Response { get; init; }

        /// <summary>限界値 (これを超えると NG)。</summary>
        public double Limit { get; init; }

        /// <summary>
        /// 応答値・限界値の表示桁数。量によって適切な桁が違うので、
        /// 列ごとに固定するのではなく行ごとに決める。
        ///   モーメント・せん断力 (kN·m, kN) … 小数 1 桁
        ///   杭頭回転角 (rad)                  … 小数 3 桁 (限界 0.010 と比べられる桁)
        ///   基礎梁の傾斜角 (rad)              … 小数 5 桁
        ///     限界 1/300 = 0.00333 なので、3 桁だと限界と応答が同じ 0.003 に潰れる
        /// </summary>
        public string ValueFormat => Kind switch
        {
            EvaluationKind.PileSectionMoment => "N1",
            EvaluationKind.PileHeadRotation => "N3",
            EvaluationKind.FoundationBeamInclination => "N5",
            _ => "N3",
        };

        [ResultColumn("応答値", 10, tooltip: "解析から得た値。単位は「単位」列を参照", rightAlign: true)]
        public string ResponseText => Response.ToString(ValueFormat, CultureInfo.InvariantCulture);

        [ResultColumn("限界値", 11, tooltip: "この値を超えると NG。単位は「単位」列を参照", rightAlign: true)]
        public string LimitText => Limit.ToString(ValueFormat, CultureInfo.InvariantCulture);

        /// <summary>応答値・限界値の単位。「kN·m」「rad」など。</summary>
        [ResultColumn("単位", 12, tooltip: "応答値・限界値の単位")]
        public string Unit { get; init; } = "";

        /// <summary>限界値の前提となった軸力 (kN)。N-M 系のみ。</summary>
        [ResultColumn("軸力(kN)", 13, "N1", "限界値の前提となった軸力。杭体曲げのみ (圧縮が正)")]
        public double? AxialForce { get; init; }

        /// <summary>基礎梁の長さ (m)。傾斜角のみ。</summary>
        public double? BeamLength { get; init; }

        /// <summary>基礎梁の番号。傾斜角のみ。</summary>
        public int? FoundationBeamNo { get; init; }

        /// <summary>
        /// 判定。
        ///
        /// <b>比から導かず、算出元と同じ比較で決めた値を持つ。</b>
        /// 検定によって境界の扱いが違う (曲げと回転角は「超えたら NG」だが、
        /// 傾斜角は「限界未満なら OK」= ちょうど等しいと NG) ため、
        /// <see cref="Ratio"/> &lt;= 1 で導くと境界で判定が変わってしまう。
        /// </summary>
        public bool IsOk { get; init; }

        /// <summary>
        /// 検定比 = 応答値 / 限界値。1 を超えるほど厳しい。
        /// 限界値が 0 以下の項目は検定対象から外しているので、分母は 0 にならない。
        /// </summary>
        [ResultColumn("検定比", 0, "N2", "応答値 ÷ 限界値。1 を超えると NG")]
        public double Ratio => Limit > 0 ? Response / Limit : double.NaN;

        /// <summary>一覧に出す判定の文字列。</summary>
        [ResultColumn("判定", 1, tooltip: "限界値を超えていれば NG")]
        public string StatusLabel => IsOk ? "OK" : "NG";

        /// <summary>画面で対象を特定するための文字列。例:「杭配置No.7 / 要素3 / i端」</summary>
        [ResultColumn("対象", 3, tooltip: "どの杭のどこか (杭配置番号 / 杭体区間 / 端部)")]
        public string TargetDescription
        {
            get
            {
                if (Kind == EvaluationKind.FoundationBeamInclination)
                    return FoundationBeamNo is int no ? $"基礎梁 #{no}" : TargetName;

                string s = $"杭配置No.{PileBodyNo}";
                if (SegmentIndex is int seg) s += $" / 要素{seg}";
                if (!string.IsNullOrEmpty(EndLabel)) s += $" / {EndLabel}";
                return s;
            }
        }

        /// <summary>画面で荷重条件を特定するための文字列。</summary>
        public string ConditionDescription =>
            string.IsNullOrEmpty(LoadCombinationName) && string.IsNullOrEmpty(LiquefactionLabel)
                ? LoadCaseName
                : $"{LoadCaseName} / {LoadCombinationName} / {LiquefactionLabel}".TrimStart(' ', '/');
    }
}
