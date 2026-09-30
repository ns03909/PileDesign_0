using PileDesign.Models.InputData;

namespace PileDesign.Models.Results
{
    /// <summary>検定の根拠の 1 項目 (項目名と、単位つきの値・出所)。</summary>
    public sealed record EvaluationBasisEntry(string Item, string Value);

    /// <summary>
    /// せん断の限界値を式と係数に分けるための材料 (<see cref="EvaluationItem.DescribeShearFormula"/>)。
    /// 検定したときと同じ断面・限界状態・レベル・低減の有無・M/(Q·d)・軸力を持つ。
    /// </summary>
    internal sealed record ShearFormulaSource(
        PileSection Section, SectionLimitState Limit, int DamageLevel, bool IsFactored, double MonQd, double AxialKN);
}
