using System;

namespace PileDesign.Common
{
    /// <summary>
    /// ウィンドウを閉じるためのインターフェース
    /// </summary>
    public interface ICloseable
    {
        event EventHandler RequestClose;
    }
}
