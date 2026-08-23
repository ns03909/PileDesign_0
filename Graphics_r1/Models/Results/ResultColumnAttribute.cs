using System;

namespace PileDesign.Models.Results
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ResultColumnAttribute : Attribute
    {
        public string Header { get; }
        public int Order { get; }
        public string? Format { get; }
        public string? Tooltip { get; }

        /// <summary>
        /// 右寄せにするか。
        /// 数値型の列は自動で右寄せになるが、桁数を行ごとに変えるために
        /// 文字列へ整形した列 (応答値・限界値など) はこれで指定する。
        /// </summary>
        public bool RightAlign { get; }

        public ResultColumnAttribute(string header, int order, string? format = null, string? tooltip = null,
            bool rightAlign = false)
        {
            Header = header;
            Order = order;
            Format = format;
            Tooltip = tooltip;
            RightAlign = rightAlign;
        }
    }
}