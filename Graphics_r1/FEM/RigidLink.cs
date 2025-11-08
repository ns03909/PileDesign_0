using System;

namespace PileDesign.FEM
{
    // capNode と pileNode を結ぶ専用リンク
    // - Ux,Uy,Uz,Rz は常時剛（タイで拘束）
    // - Rx,Ry は「タイ」ではなく可変（M-θにより SetKe で毎ステップ更新）
    // 実装は既存の組立を生かすため HorizontalSoilSpring をアダプタとして内部保持する。
    public sealed class RigidLink
    {
        public string Name { get; }
        public Node NodeI { get; }
        public Node NodeJ { get; }

        // 各自由度のタイ拘束フラグ（true=剛に結合、false=この要素では拘束しない）
        public bool TieUx { get; }
        public bool TieUy { get; }
        public bool TieUz { get; }
        public bool TieRx { get; }  // 杭頭回転は false にしておく（M-θで与えるため）
        public bool TieRy { get; }  // 同上
        public bool TieRz { get; }

        // 拘束（タイ）に用いる十分大きい剛性
        public double Kbig { get; }

        // 既存のマッピングに載せるためのアダプタばね
        public HorizontalSoilSpring AdapterSpring { get; }

        public RigidLink(string name, Node nodeI, Node nodeJ,
                         bool tieUx, bool tieUy, bool tieUz,
                         bool tieRx, bool tieRy, bool tieRz,
                         double kbig = 1e12)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NodeI = nodeI ?? throw new ArgumentNullException(nameof(nodeI));
            NodeJ = nodeJ ?? throw new ArgumentNullException(nameof(nodeJ));

            TieUx = tieUx;
            TieUy = tieUy;
            TieUz = tieUz;
            TieRx = tieRx;
            TieRy = tieRy;
            TieRz = tieRz;

            Kbig = kbig;

            // 既存のアセンブリを使うため、アダプタ名は HeadLink と同一命名に合わせる
            AdapterSpring = new HorizontalSoilSpring(name, nodeI, nodeJ);
        }

        // 剛結成分は常に Kbig、Rx/Ry は引数で上書き（nullなら 0）
        public void ApplyKe(bool isTan, double? kRx = null, double? kRy = null)
        {
            double kx = TieUx ? Kbig : 0.0;
            double ky = TieUy ? Kbig : 0.0;
            double kz = TieUz ? Kbig : 0.0;

            // Rx/Ry は「タイ」にしない前提（M-θで可変）。Tie=true にする設計なら Kbig を優先。
            double krx = TieRx ? Kbig : (kRx ?? 0.0);
            double kry = TieRy ? Kbig : (kRy ?? 0.0);

            double krz = TieRz ? Kbig : 0.0;

            // 既存の spring 要素経由で全 DOF を一括セット
            AdapterSpring.SetKe(kx, ky, kz, krx, kry, krz, isTan);
        }
    }
}