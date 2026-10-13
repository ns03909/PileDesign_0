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
                         double kbig = 1e6)  // アーム変換後の条件数を改善するため1e6に低減
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

    }
}