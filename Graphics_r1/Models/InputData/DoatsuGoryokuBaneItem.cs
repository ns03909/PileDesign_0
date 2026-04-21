using PileDesign.FEM;
using System;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    public class DoatsuGoryokuBaneItem : BaseModel
    {
        // データ保存/復元対象は public set にする
        public double Gamma { get; set; }
        public double Phi { get; set; }
        public double C { get; set; }

        public ZDataItem ZDataItemTop { get; set; }
        public ZDataItem ZDataItemBtm { get; set; }

        public double ZTop { get; set; }
        public double ZBtm { get; set; }

        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y1 { get; set; }
        public double Y2 { get; set; }

        public double X0 => (X1 + X2) * 0.5;
        public double Y0 => (Y1 + Y2) * 0.5;

        public double DX => Math.Abs(X2 - X1);
        public double DY => Math.Abs(Y2 - Y1);
        public double DZ => Math.Abs(ZTop - ZBtm);

        public string Name { get; set; }

        public double StressTop { get; set; }
        public double StressBtm { get; set; }

        // DeltaP/Ysp は外部でセットされるため public set
        public double DeltaP { get; set; }
        public double Ysp { get; set; }

        // 固定係数はそのまま公開プロパティ（必要なら編集可）
        public double K0 { get; set; } = 0.5;

        // 導出値はプロパティで動的計算（デシリアライズ後の再計算不要）
        [JsonIgnore]
        public double Kp => Math.Pow(Math.Tan((45 + Phi * 0.5) * Math.PI / 180), 2);

        [JsonIgnore]
        public double Q { get; set; } // (kN/m2) 深さZaでの上載圧（public set にしておく）

        // P0, Pp 等は状態に基づき動的計算する
        public double P0 => (Q + Gamma * (ZTop - ZBtm)) * K0; // kN/m2 p484
        public double Pp => (Q + Gamma * (ZTop - ZBtm)) * Kp + 2 * C * Math.Sqrt(Kp); // kN/m2 p484

        public double P0Top => K0 * StressTop;
        public double P0Btm => K0 * StressBtm;
        public double PpTop => Kp * StressTop;
        public double PpBtm => Kp * StressBtm;

        // ランタイム参照はシリアライズ対象外にする
        [JsonIgnore]
        public FEM.Node TopEmbedmentNode { get; set; }
        [JsonIgnore]
        public FEM.Node BtmEmbedmentNode { get; set; }

        [JsonIgnore]
        public FEM.Node TopSoilNode { get; set; }
        [JsonIgnore]
        public FEM.Node BtmSoilNode { get; set; }

        [JsonIgnore]
        public HorizontalSoilSpring TopHorizontalSoilSpring { get; set; }
        [JsonIgnore]
        public HorizontalSoilSpring BtmHorizontalSoilSpring { get; set; }

        // パラメータレスコンストラクタ（System.Text.Json が使えるように）
        public DoatsuGoryokuBaneItem()
        {
            Gamma = 0;
            Phi = 0;
            C = 0;
            ZDataItemTop = new ZDataItem();
            ZDataItemBtm = new ZDataItem();
            ZTop = 0;
            ZBtm = 0;
            X1 = X2 = Y1 = Y2 = 0;
            Name = string.Empty;
            StressTop = StressBtm = 0;
            DeltaP = 0;
            Ysp = 0;
            Q = 0;
            K0 = 0.5;
        }

        // 既存コンストラクタは残す（互換）
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

            // Q は呼び出し側でセットされる想定のためここでは触らない
            // P0/Pp 等は動的プロパティで計算される
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
        // 原点対称（奇関数）として評価。入力 disp の符号を保持し、|disp| で有理近似を評価する。
        // 旧実装は disp < DeltaP の判定が負側で常に真となり、|disp| ≈ 2·DeltaP 付近でゼロ除算、
        // それを越えると denom の符号が反転して圧力が誤った方向に出る（counter-loading で発散）。
        public double GetPressure(double disp)
        {
            if (disp == 0) return 0;
            double sign = disp < 0 ? -1.0 : 1.0;
            double absDisp = Math.Abs(disp);
            if (absDisp < DeltaP)
            {
                return sign * (Pp - P0) * (DeltaP - Ysp) * absDisp
                       / (2 * DeltaP * Ysp + (DeltaP - 3 * Ysp) * absDisp);
            }
            else
            {
                return sign * (Pp - P0);
            }
        }

        // v22 修正: 塑性領域（|disp| ≥ DeltaP）の接線剛性は、以前 `0.0001 × k0`
        // （k0 は初期接線）だったが、降伏境界の解析接線と比較して約 1/500 と極端に小さく、
        // NR 反復で「塑性 ↔ 非線形」を行き来する springs があると K 行列が毎反復で激変し
        // ラインサーチが α=0.5 に張り付く収束停滞の主因だった。
        // 新実装: 降伏境界 |disp|=DeltaP の解析接線を基準として、その PostYieldTangentRatio 倍。
        // これにより不連続が約 1/10 に緩和される（500× → ~50×）。
        // 物理的には plateau 圧力 (Pp-P0) は厳密には一定だが、K_tan はあくまで NR 用の
        // 「regularization 済み近似接線」であり、力の計算は GetSecantStiffness が担うため
        // 物理量 F_int = K_sec × disp の値には影響しない。
        private const double PostYieldTangentRatio = 0.02;

        // 降伏境界 |disp|=DeltaP における有理近似式の解析接線
        // p(u) = (Pp-P0) × A × u / (B + C × u), A=DeltaP-Ysp, B=2×DeltaP×Ysp, C=DeltaP-3×Ysp
        // dp/du = (Pp-P0) × A × B / (B + C × u)²
        // |u|=DeltaP のとき B + C×DeltaP = DeltaP × A なので
        // dp/du(DeltaP) = (Pp-P0) × A × B / (DeltaP² × A²) = (Pp-P0) × B / (DeltaP² × A)
        //                = (Pp-P0) × 2×Ysp / (DeltaP × (DeltaP-Ysp))
        private double YieldBoundaryTangent()
        {
            double denom = DeltaP * (DeltaP - Ysp);
            if (Math.Abs(denom) < 1e-30) return 0.0;
            return (Pp - P0) * 2.0 * Ysp / denom;
        }

        // kN/m3
        public double GetTangentStiffness(double disp)
        {
            double absDisp = Math.Abs(disp);
            double fraction = Math.Pow(10, -6);
            double k0 = GetPressure(fraction) / fraction; // 初期剛性
            if (absDisp < fraction)
            {
                // 初期剛性
                return k0;
            }
            else if (absDisp < DeltaP)
            {
                // 非線形領域: 数値微分で接線剛性を求める
                return (GetPressure(absDisp + fraction) - GetPressure(absDisp - fraction)) / (2 * fraction);
            }
            else
            {
                // 塑性領域: 降伏境界接線の一定比率を採用（降伏境界との不連続を緩和）
                double yieldTan = YieldBoundaryTangent();
                if (!double.IsFinite(yieldTan) || yieldTan <= 0.0)
                {
                    // YieldBoundaryTangent が計算不可（極端な入力）な場合のフォールバック
                    return 0.0001 * k0;
                }
                return PostYieldTangentRatio * yieldTan;
            }
        }

        public double GetSecantStiffness(double disp)
        {
            double absDisp = Math.Abs(disp);
            double fraction = Math.Pow(10, -6);
            if (absDisp < fraction)
            {
                // 初期剛性
                return GetPressure(fraction) / fraction;
            }
            else if (absDisp < DeltaP)
            {
                // 非線形領域: 割線剛性 = P / |disp|
                return GetPressure(absDisp) / absDisp;
            }
            else
            {
                // 塑性領域: 圧力は最大値(Pp - P0)で一定
                // 割線剛性 = P_max / |disp|
                return (Pp - P0) / absDisp;
            }
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
}