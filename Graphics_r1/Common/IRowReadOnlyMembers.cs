namespace PileDesign.Common
{
    /// <summary>
    /// 行ごとに一部の列だけを読み取り専用にする行データ。
    ///
    /// <para>列の <c>IsReadOnly</c> は全行に効くので、「この行のこの値だけは計算で決まる」を表せない。
    /// <see cref="EnhancedDataGrid"/> は、セルの編集・打鍵での上書き・貼り付け・塗り潰し・クリアのすべてで
    /// これを確かめる (どれか 1 つでも見落とすと、そこから値が書き換わる)。</para>
    /// </summary>
    public interface IRowReadOnlyMembers
    {
        /// <summary>このプロパティ (列のバインドのパス) を、この行では書き換えられないときの理由。書き換えられるなら null。</summary>
        string? ReadOnlyReason(string propertyPath);
    }
}
