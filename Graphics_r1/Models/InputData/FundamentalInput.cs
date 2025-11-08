using PileDesign.ViewModels;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class FundamentalInput : BaseViewModel
    {
        // プロジェクト番号
        private string _projectNo;
        public string ProjectNo
        {
            get => _projectNo;
            set => SetProperty(ref _projectNo, value);
        }

        // プロジェクト名
        private string _projectName;
        public string ProjectName
        {
            get => _projectName;
            set => SetProperty(ref _projectName, value);
        }

        // 参照レベル
        private string _refLevel;
        public string RefLevel
        {
            get => _refLevel;
            set => SetProperty(ref _refLevel, value);
        }

        // 耐震グレード
        private string _seismicGrade;
        public string SeismicGrade
        {
            get => _seismicGrade;
            set => SetProperty(ref _seismicGrade, value);
        }

        // 
        private double _x0;
        public double X0
        {
            get => _x0;
            set => SetProperty(ref _x0, value);
        }

        private double _y0;
        public double Y0
        {
            get => _y0;
            set => SetProperty(ref _y0, value);
        }

        private double _z0;
        public double Z0
        {
            get => _z0;
            set => SetProperty(ref _z0, value);
        }

        // 参考軸中心
        public Point3D Point3D0 => new() { X = X0, Y = Y0, Z = Z0 };


        // コンストラクタ
        public FundamentalInput()
        {
            RefLevel = "TP";
            ProjectNo = "J240000-#";
            ProjectName = "プロジェクト名";
            SeismicGrade = "A";
        }

        // 浅いコピーを作成するメソッド
        public FundamentalInput ShallowCopy()
        {
            return (FundamentalInput)this.MemberwiseClone();
        }
    }
}
