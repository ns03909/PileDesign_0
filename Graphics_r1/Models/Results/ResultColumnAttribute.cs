using System;

namespace PileDesign.Models.Results
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ResultColumnAttribute : Attribute
    {
        public string Header { get; }
        public int Order { get; }
        public string? Format { get; }
        public ResultColumnAttribute(string header, int order, string? format = null)
        {
            Header = header;
            Order = order;
            Format = format;
        }
    }
}