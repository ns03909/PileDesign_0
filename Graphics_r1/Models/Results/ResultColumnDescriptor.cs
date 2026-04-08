using System.Reflection;

namespace PileDesign.Models.Results
{
    public sealed class ResultColumnDescriptor
    {
        public string Header { get; init; } = "";
        public int Order { get; init; }
        public PropertyInfo Property { get; init; } = null!;
        public string? Format { get; init; }
        public string? Tooltip { get; init; }
    }
}