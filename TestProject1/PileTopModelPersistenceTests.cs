using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 杭頭接合部のモデルが、保存ファイルを往復しても値を保つこと。
    ///
    /// <c>public double X { get; private set; }</c> は<b>書き出されるが復元されない</b>。
    /// System.Text.Json は private セッターを呼びません。読込後は宣言の既定値に戻り、
    /// しかもファイルの中には正しい値が書いてあるので、ファイルを見ても気づけません。
    ///
    /// FT-Pile の杭径がこれでした。φ1000 の杭で保存して開き直すと、杭頭 M-θ だけが
    /// 既定の φ600 / φ400 で計算されます。
    /// </summary>
    [TestClass]
    public class PileTopModelPersistenceTests
    {
        // 保存と同じ設定 (MainWindowViewModel._jsonOptions)
        private static readonly JsonSerializerOptions SaveOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        private static T RoundTrip<T>(T value)
            => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SaveOptions), SaveOptions)!;

        [TestMethod]
        public void FtPile_KeepsThePileDiameter_AcrossASaveAndLoad()
        {
            var pile = new FTPilePile();
            pile.SetDimensions(1000.0, 800.0);

            var restored = RoundTrip(pile);

            Assert.AreEqual(1000.0, restored.D1, 1e-9,
                "杭の外径が既定 (600) に戻っています。杭頭 M-θ の耐力 (Ap 経由) と "
                + "初期回転剛性 K0 (D1³ − D2³) が別の杭のものになります");
            Assert.AreEqual(800.0, restored.D2, 1e-9, "杭の内径が既定 (400) に戻っています");
            Assert.AreEqual(pile.Ap, restored.Ap, 1e-6, "杭頭面積 Ap も合わせて戻すこと");
        }

        [TestMethod]
        public void FtPileCap_KeepsThePileCapConcrete_AcrossASaveAndLoad()
        {
            var cap = new FTPileCap(42.0, 30000.0);

            var restored = RoundTrip(cap);

            Assert.AreEqual(42.0, restored.Fc, 1e-9,
                "パイルキャップの Fc が既定 (24) に戻っています");
            Assert.AreEqual(30000.0, restored.E, 1e-9,
                "パイルキャップの Ec が既定に戻っています");
        }
    }
}
