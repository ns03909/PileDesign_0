using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PileDesignCore.Shared
{
    public static class NikkenDrawingColors
    {
        public static System.Drawing.Color SkyBlue = System.Drawing.Color.FromArgb(98, 176, 226); // sky blue
        public static System.Drawing.Color PaleRed = System.Drawing.Color.FromArgb(233, 85, 65); // pale red
        public static System.Drawing.Color Red = System.Drawing.Color.FromArgb(216, 37, 49); // red
        public static System.Drawing.Color DeepBlue = System.Drawing.Color.FromArgb(50, 113, 173); // deep blue
        public static System.Drawing.Color Yellow = System.Drawing.Color.FromArgb(247, 181, 21); // yellow
        public static System.Drawing.Color Green = System.Drawing.Color.FromArgb(35, 137, 102); // green
    }

    public static class NikkenWindowsMediaColors
    {
        public static System.Windows.Media.Color SkyBlue = System.Windows.Media.Color.FromRgb(98, 176, 226); // sky blue
        public static System.Windows.Media.Color PaleRed = System.Windows.Media.Color.FromRgb(233, 85, 65); // pale red
        public static System.Windows.Media.Color Red = System.Windows.Media.Color.FromRgb(216, 37, 49); // red
        public static System.Windows.Media.Color DeepBlue = System.Windows.Media.Color.FromRgb(50, 113, 173); // deep blue
        public static System.Windows.Media.Color Yellow = System.Windows.Media.Color.FromRgb(247, 181, 21); // yellow
        public static System.Windows.Media.Color Green = System.Windows.Media.Color.FromRgb(35, 137, 102); // green
    }

    public static class NikkenBrush
    {
        public static SolidColorBrush SkyBlue = new SolidColorBrush(NikkenWindowsMediaColors.SkyBlue);
        public static SolidColorBrush PaleRed = new SolidColorBrush(NikkenWindowsMediaColors.PaleRed);
        public static SolidColorBrush Red = new SolidColorBrush(NikkenWindowsMediaColors.Red);
        public static SolidColorBrush DeepBlue = new SolidColorBrush(NikkenWindowsMediaColors.DeepBlue);
        public static SolidColorBrush Yellow = new SolidColorBrush(NikkenWindowsMediaColors.Yellow);
        public static SolidColorBrush Green = new SolidColorBrush(NikkenWindowsMediaColors.Green);
    }

}
