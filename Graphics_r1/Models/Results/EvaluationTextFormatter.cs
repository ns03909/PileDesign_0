using System.Collections.Generic;
using System.Text;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 検定項目 1 件をテキストに直す。
    ///
    /// 出力は<b>従来の実装と 1 文字も変えない</b>。
    /// 検定テキストは画面 (EvaluationWindow) と計算書 (WordDocument) の
    /// 両方が使っており、変えると計算書の見た目が変わってしまう。
    /// 書式を変えたくなったら、golden テスト
    /// (TestProject1/EvaluationTextGoldenTests.cs) を意識して行うこと。
    /// </summary>
    public static class EvaluationTextFormatter
    {
        /// <summary>
        /// 杭体曲げ・杭頭回転角の 1 件分 (4 行: 見出し / 荷重条件 / 数値 / 空行)。
        /// </summary>
        public static void AppendItem(StringBuilder sb, EvaluationItem item)
        {
            switch (item.Kind)
            {
                case EvaluationKind.PileSectionMoment:
                    AppendMoment(sb, item);
                    break;
                case EvaluationKind.PileSectionShear:
                    AppendShear(sb, item);
                    break;
                case EvaluationKind.PileHeadRotation:
                    AppendRotation(sb, item);
                    break;
                case EvaluationKind.PileHeadDeformationAngle:
                    AppendDeformationAngle(sb, item);
                    break;
                case EvaluationKind.FoundationBeamInclination:
                    AppendInclination(sb, item);
                    break;
            }
        }

        private static void AppendMoment(StringBuilder sb, EvaluationItem item)
        {
            string tail = $": {item.TargetName}  {PileLabel(item)} / 要素{item.SegmentIndex}";

            sb.AppendLine($"  [{Verdict(item)}] {item.LimitName}{OverSuffix(item)}（{item.EndLabel}）{tail}");

            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            sb.AppendLine($"       M={item.Response:F1} kNm {op} {item.LimitName}M={item.Limit:F1} kNm (N={item.AxialForce:F1} kN)");
            sb.AppendLine();
        }

        /// <summary>
        /// 杭体せん断の 1 件分。曲げと同じ並び (見出し / 荷重条件 / 数値 / 空行) にする。
        /// 曲げと同じ画面・同じ章に並ぶので、書式が違うと読み比べられない。
        /// </summary>
        private static void AppendShear(StringBuilder sb, EvaluationItem item)
        {
            string tail = $": {item.TargetName}  {PileLabel(item)} / 要素{item.SegmentIndex}";

            sb.AppendLine($"  [{Verdict(item)}] {item.LimitName}せん断{OverSuffix(item)}（{item.EndLabel}）{tail}");

            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            // せん断耐力は M/(Q·d) に依存するので、どの値で算定したかも残す
            string monQd = item.MonQd is double v ? $", M/(Q·d)={v:F2}" : string.Empty;
            sb.AppendLine($"       Q={item.Response:F1} kN {op} {item.LimitName}Q={item.Limit:F1} kN (N={item.AxialForce:F1} kN{monQd})");
            sb.AppendLine();
        }

        /// <summary>
        /// 杭頭回転角。限界値と照査するレベルは杭頭工法で決まるので、
        /// どの規定で照査したかが読めるように工法名 (Category) と限界状態を出す。
        /// </summary>
        private static void AppendRotation(StringBuilder sb, EvaluationItem item)
        {
            string tail = $": {item.TargetName}  {PileLabel(item)}";
            string kind = string.IsNullOrEmpty(item.Category) ? "杭頭回転角" : item.Category;

            string over = OverSuffix(item).Length > 0 ? " 超過" : "";
            sb.AppendLine($"  [{Verdict(item)}] {kind}{over}{tail}");

            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            string limitName = string.IsNullOrEmpty(item.LimitName) ? "" : item.LimitName;
            sb.AppendLine($"       θ={item.Response:F5} rad {op} {limitName}θ={item.Limit:F5} rad");
            sb.AppendLine();
        }

        /// <summary>
        /// 杭頭 2 点間の変形角。1 つの荷重条件につき最大値 1 件なので、
        /// どの組で最大になったかを対象名で示す。
        /// </summary>
        private static void AppendDeformationAngle(StringBuilder sb, EvaluationItem item)
        {
            sb.AppendLine($"  [{Verdict(item)}] {item.LimitName}変形角{OverSuffix(item)}: {item.TargetName}");
            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            double inv = item.Response > 0 ? 1.0 / item.Response : 0;
            sb.AppendLine($"       θ={item.Response:E3} rad (1/{inv:F0}) {op} "
                          + $"{item.LimitName}θ={item.Limit:E3} rad");
            sb.AppendLine();
        }

        private static void AppendInclination(StringBuilder sb, EvaluationItem item)
        {
            string status = item.IsOk ? "OK" : "NG";
            double inv = item.Response > 0 ? 1.0 / item.Response : 0;
            sb.AppendLine($"  {status} 梁 #{item.FoundationBeamNo}: " +
                          $"傾斜角 = {item.Response:E3} rad (1/{inv:F0}), L={item.BeamLength:F2}m");
        }

        /// <summary>
        /// 行頭の判定。収束していないケースは <b>OK とも NG とも名乗らない</b> —
        /// 応答値が釣り合いを満たしていないので、限界値と比べた結果に意味が無い。
        /// </summary>
        private static string Verdict(EvaluationItem item) =>
            item.IsFromUnconvergedCase ? "未収束" : (item.IsOk ? "OK" : "NG");

        /// <summary>「超過」。NG のときだけ付く (未収束は超過とも言えない)。</summary>
        private static string OverSuffix(EvaluationItem item) =>
            !item.IsFromUnconvergedCase && !item.IsOk ? "超過" : "";

        /// <summary>
        /// 対象の杭の名乗り。杭体は複数の杭で共有されるので、
        /// 杭体番号だけでは別々の杭の行が同じ表記になって区別できない。
        /// 従来は杭体番号を「杭配置No.」と名乗っていた（名前と中身が食い違っていた）。
        /// </summary>
        private static string PileLabel(EvaluationItem item) =>
            item.PileNo is int no
                ? $"杭No.{no} / 杭体No.{item.PileBodyNo}"
                : $"杭体No.{item.PileBodyNo}";

        /// <summary>
        /// 荷重条件の行。組合せや液状化の概念が無い検定 (群杭沈下の変形角など) では
        /// 空の項目を出さない (「組合せ: 」だけが並ぶのを避ける)。
        /// 地震時の検定はどちらも必ず入るので、従来の出力は変わらない。
        /// </summary>
        private static void AppendCondition(StringBuilder sb, EvaluationItem item)
        {
            var parts = new List<string> { $"荷重ケース: {item.LoadCaseName}" };
            if (!string.IsNullOrEmpty(item.LoadCombinationName))
                parts.Add($"組合せ: {item.LoadCombinationName}");
            if (!string.IsNullOrEmpty(item.LiquefactionLabel))
                parts.Add(item.LiquefactionLabel);

            sb.AppendLine("       " + string.Join(" / ", parts));
        }
    }
}
