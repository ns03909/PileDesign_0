namespace PileDesign.Models.Results
{
    /// <summary>
    /// 行が属する荷重条件。
    ///
    /// 荷重条件をまたぐ表 (<see cref="ResultTable.SpansAllConditions"/>) では、
    /// 表そのものが条件を持たないため、条件のフィルタは<b>行に対して</b>掛ける。
    /// これを実装していない行は絞り込めないので、またぐ表の行はこれを実装すること。
    /// </summary>
    public interface IHasLoadCondition
    {
        string LoadCaseName { get; }
        string LoadCombinationName { get; }

        /// <summary>液状化を考慮したケースか。液状化の概念が無い検定では null。</summary>
        bool? IsLiquefaction { get; }
    }
}
