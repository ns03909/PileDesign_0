namespace PileDesign.FEM
{
    /// <summary>
    /// 杭節点の名前 <c>杭節点-{杭番号}-{節点順}</c> (<see cref="AnalysisModelling"/> が付ける) の読み方。
    /// 杭番号は解析モデルを組んだときの杭配置の番号 (No。解析の前に 1 から連番に揃えている)。
    ///
    /// <para>解析モデルの節点は杭番号を別に持たず、名前が識別子を兼ねている。読み方を 1 か所に置き、
    /// 結果の表・結果の検査・剛性の検査が同じものを使う (写しを作らない)。</para>
    /// </summary>
    internal static class PileNodeNaming
    {
        private const string Prefix = "杭節点-";

        /// <summary>杭節点の名前 (または「名前:自由度」) の杭番号。杭節点でなければ null。</summary>
        internal static int? PileNoOf(string? nodeName)
        {
            if (nodeName == null || !nodeName.StartsWith(Prefix, System.StringComparison.Ordinal)) return null;
            var parts = nodeName.Split('-');
            return parts.Length >= 3 && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int no) ? no : null;
        }
    }
}
