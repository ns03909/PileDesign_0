#nullable enable
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public class ExampleItem(string display, ICommand? command)
    {
        public string Display { get; init; } = display;
        public ICommand? Command { get; init; } = command;

        public override string ToString() => Display;
    }
}