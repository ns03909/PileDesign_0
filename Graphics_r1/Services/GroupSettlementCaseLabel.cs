using PileDesign.Models.Results;

namespace PileDesign.Services
{
    /// <summary>計算書の図表に表示する群杭沈下のケース識別子。</summary>
    public static class GroupSettlementCaseLabel
    {
        public static string Describe(GroupSettlementCaseRecord? record)
        {
            if (record == null) return "（群杭沈下: 未計算）";
            string caseName = string.IsNullOrWhiteSpace(record.LoadCaseName) ? "未設定" : record.LoadCaseName.Trim();
            string loadingType = string.IsNullOrWhiteSpace(record.LoadingType) ? "未設定" : record.LoadingType.Trim();
            return $"（ケース: {caseName}／解析方式: {loadingType}）";
        }
    }
}
