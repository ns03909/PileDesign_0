using PileDesign.FEM;
using System;
using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class DoatsuGoryokuBaneItem : BaseModel
    {
        public double Gamma { get; private set; }
        public double Phi { get; private set; }
        public double C { get; private set; }

        public ZDataItem ZDataItemTop { get; private set; }
        public ZDataItem ZDataItemBtm { get; private set; }

        public double ZTop { get; private set; }
        public double ZBtm { get; private set; }

        public double X1 { get; private set; }
        public double X2 { get; private set; }
        public double Y1 { get; private set; }
        public double Y2 { get; private set; }

        public double X0 => (X1 + X2) * 0.5;
        public double Y0 => (Y1 + Y2) * 0.5;

        public double DX => Math.Abs(X2 - X1);
        public double DY => Math.Abs(Y2 - Y1);
        public double DZ => Math.Abs(ZTop - ZBtm);

        public string Name { get; private set; }

        public double StressTop { get; private set; }
        public double StressBtm { get; private set; }

        public double DeltaP { get; private set; }
        public double Ysp { get; private set; }

        public double K0 { get; private set; } = 0.5;
        public double Kp { get; private set; }
        public double Q { get; private set; } // (kN/m2) 深さZaでの上載圧
        public double P0 { get; private set; } // (kN/m2)
        public double Pp { get; private set; } // (kN/m2)

        public double P0Top { get; private set; }
        public double P0Btm { get; private set; }
        public double PpTop { get; private set; }
        public double PpBtm { get; private set; }

        public FEM.Node TopEmbedmentNode { get; private set; }
        public FEM.Node BtmEmbedmentNode { get; private set; }

        public FEM.Node TopSoilNode { get; private set; }
        public FEM.Node BtmSoilNode { get; private set; }

        public HorizontalSoilSpring TopHorizontalSoilSpring { get; private set; }
        public HorizontalSoilSpring BtmHorizontalSoilSpring { get; private set; }

        // コンストラクタ
        public DoatsuGoryokuBaneItem(
        ZDataItem zDataItemTop, ZDataItem zDataItemBtm,
        double zTop, double zBtm, double x1, double x2, double y1, double y2,
            string name, double gamma, double stressTop, double stressBtm, double phi, double c)
        {
            Gamma = gamma;
            Phi = phi;
            C = c;
            ZDataItemTop = zDataItemTop;
            ZDataItemBtm = zDataItemBtm;
            ZTop = zTop;
            ZBtm = zBtm;
            X1 = x1;
            X2 = x2;
            Y1 = y1;
            Y2 = y2;

            Name = name;

            StressTop = stressTop;
            StressBtm = stressBtm;

            Kp = Math.Pow(Math.Tan((45 + Phi * 0.5) * Math.PI / 180), 2);
            P0 = (Q + Gamma * (ZTop - ZBtm)) * K0; // kN/m2 p484
            Pp = (Q + Gamma * (ZTop - ZBtm)) * Kp + 2 * C * Math.Sqrt(Kp); // kN/m2 p484

            P0Top = K0 * stressTop;
            P0Btm = K0 * stressBtm;
            PpTop = Kp * stressTop;
            PpBtm = Kp * stressBtm;
        }

        public void SetTopNodesAndSpring(FEM.Node embedmentNode, FEM.Node soilNode, HorizontalSoilSpring horizontalSoilSpring)
        {
            TopEmbedmentNode = embedmentNode;
            TopSoilNode = soilNode;
            TopHorizontalSoilSpring = horizontalSoilSpring;
        }

        public void SetBtmNodesAndSpring(FEM.Node embedmentNode, FEM.Node soilNode, HorizontalSoilSpring horizontalSoilSpring)
        {
            BtmEmbedmentNode = embedmentNode;
            BtmSoilNode = soilNode;
            BtmHorizontalSoilSpring = horizontalSoilSpring;
        }

        // kN/m2
        public double GetPressure(double disp)
        {
            if (disp == 0)
            {
                return 0;
            }
            else if (disp < DeltaP)
            {
                return (Pp - P0) * (DeltaP - Ysp) * disp / (2 * DeltaP * Ysp + (DeltaP - 3 * Ysp) * Math.Abs(disp));
            }
            else
            {
                return Pp - P0;
            }
        }

        // kN/m3
        public double GetTangentStiffness(double disp)
        {
            double fraction = Math.Pow(10, -6);
            if (disp == 0)
            {
                return GetPressure(fraction) / fraction;
            }
            else if (disp < DeltaP)
            {
                return (GetPressure(disp + fraction) - GetPressure(disp - fraction)) / (2 * fraction);
            }
            else { return 0; }
        }

        public double GetSecantStiffness(double disp)
        {
            double fraction = Math.Pow(10, -6);
            if (disp == 0)
            {
                return GetPressure(fraction) / fraction;
            }
            else if (disp < DeltaP)
            {
                return GetPressure(disp) / disp;
            }
            else { return 0; }
        }


        // 剛性ベクトルを返すメソッド
        public NodeSpring GetTangentStiffnessVector(FEM.NodeDisp relDispVector)
        {
            double x = relDispVector.Ux;
            double y = relDispVector.Uy;
            double kx = GetTangentStiffness(x) * DY * DZ * 0.5; // 確認 
            double ky = GetTangentStiffness(y) * DX * DZ * 0.5; // 確認
            var vec = new NodeSpring(kx, ky, 0, 0, 0, 0);
            return vec;
        }

        public NodeSpring GetSecantStiffnessVector(FEM.NodeDisp relDispVector)
        {
            double x = relDispVector.Ux;
            double y = relDispVector.Uy;
            double kx = GetSecantStiffness(x) * DY * DZ * 0.5; // 確認
            double ky = GetSecantStiffness(y) * DX * DZ * 0.5; // 確認
            var vec = new NodeSpring(kx, ky, 0, 0, 0, 0);
            return vec;
        }

        public void SetDeltaPAndYsp(double deltaP, double ysp)
        {
            DeltaP = deltaP;
            Ysp = ysp;
        }
    }

    public class DoatsuGoryokuBane : BaseModel
    {
        private ObservableCollection<DoatsuGoryokuBaneItem> _items = [];
        public ObservableCollection<DoatsuGoryokuBaneItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        public double DeltaP;
        public double Ysp;

        // DeltaPYspの更新メソッド
        public void UpdateDeltaPYsp(double deltaPonEmbedmentDepth)
        {
            if (Items.Count == 0)
            { return; }
            double embedmentDepth = Items[0].ZTop - Items[^1].ZBtm;
            DeltaP = deltaPonEmbedmentDepth * embedmentDepth;
            Ysp = 0.1 * DeltaP;
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].SetDeltaPAndYsp(DeltaP, Ysp);
            }
        }

        // 深いコピーを作成するメソッド
        public DoatsuGoryokuBane DeepCopy()
        {
            return (DoatsuGoryokuBane)this.MemberwiseClone();
        }
    }
}

