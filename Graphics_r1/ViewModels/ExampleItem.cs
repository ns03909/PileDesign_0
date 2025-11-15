using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public class ExampleItem
    {
        public string Display { get; init; }
        public ICommand? Command { get; init; }

        public ExampleItem(string display, ICommand? command)
        {
            Display = display;
            Command = command;
        }

        public override string ToString() => Display;
    }
}