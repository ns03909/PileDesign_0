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
                case EvaluationKind.PileHeadRotation:
                    AppendRotation(sb, item);
                    break;
                case EvaluationKind.FoundationBeamInclination:
                    AppendInclination(sb, item);
                    break;
            }
        }

        private static void AppendMoment(StringBuilder sb, EvaluationItem item)
        {
            string tail = $": {item.TargetName}  杭配置No.{item.PileBodyNo} / 要素{item.SegmentIndex}";

            if (item.IsOk)
                sb.AppendLine($"  [OK] {item.LimitName}（{item.EndLabel}）{tail}");
            else
                sb.AppendLine($"  [NG] {item.LimitName}超過（{item.EndLabel}）{tail}");

            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            sb.AppendLine($"       M={item.Response:F1} kNm {op} {item.LimitName}M={item.Limit:F1} kNm (N={item.AxialForce:F1} kN)");
            sb.AppendLine();
        }

        private static void AppendRotation(StringBuilder sb, EvaluationItem item)
        {
            string tail = $": {item.TargetName}  杭配置No.{item.PileBodyNo}";

            if (item.IsOk)
                sb.AppendLine($"  [OK] θ（場所打ちRC杭）{tail}");
            else
                sb.AppendLine($"  [NG] θ超過（場所打ちRC杭）{tail}");

            AppendCondition(sb, item);

            string op = item.IsOk ? "≤" : ">";
            sb.AppendLine($"       θ={item.Response:F5} rad {op} {item.Limit:F2} rad");
            sb.AppendLine();
        }

        private static void AppendInclination(StringBuilder sb, EvaluationItem item)
        {
            string status = item.IsOk ? "OK" : "NG";
            double inv = item.Response > 0 ? 1.0 / item.Response : 0;
            sb.AppendLine($"  {status} 梁 #{item.FoundationBeamNo}: " +
                          $"傾斜角 = {item.Response:E3} rad (1/{inv:F0}), L={item.BeamLength:F2}m");
        }

        private static void AppendCondition(StringBuilder sb, EvaluationItem item)
        {
            sb.AppendLine($"       荷重ケース: {item.LoadCaseName} / 組合せ: {item.LoadCombinationName} / {item.LiquefactionLabel}");
        }
    }
}
