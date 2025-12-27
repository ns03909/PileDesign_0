using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace PileDesign.Models.InputData
{
    public class GridDataItem : BaseDataItem
    {

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _coord;
        public double Coord
        {
            get => _coord;
            set => SetProperty(ref _coord, value);
        }

        private double _spacing;
        public double Spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }

        private Brush _spacingForeground = Brushes.Black; // 初期値を設定
        [JsonIgnore]
        public Brush SpacingForeground
        {
            get => _spacingForeground;
            set => SetProperty(ref _spacingForeground, value);
        }

        private Brush _coordForeground = Brushes.Black; // 初期値を設定
        [JsonIgnore]
        public Brush CoordForeground
        {
            get => _coordForeground;
            set => SetProperty(ref _coordForeground, value);
        }

        public List<PileLayoutDataItem> Piles { get; set; } = [];

        public void SetPiles(ObservableCollection<PileLayoutDataItem> piles, double axisAngle, double distance = 0.1)
        {
            // xGridの場合はy座標で、yGridの場合はx座標で位置を設定
            Piles = [];
            if (axisAngle == 0) // X
            {
                foreach (var pile in piles)
                {
                    if (Math.Abs(pile.Y - Coord) <= distance)
                    {
                        Piles.Add(pile);
                    }
                }
            }
            else if (axisAngle == 90) // Y
            {
                foreach (var pile in piles)
                {
                    if (Math.Abs(pile.X - Coord) <= distance)
                    {
                        Piles.Add(pile);
                    }
                }
            }
        }
    }
}




