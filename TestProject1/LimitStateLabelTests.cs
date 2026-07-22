using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 2025年版 構造関係技術基準解説書 準拠オプション時の表示名切替
    /// （使用限界→長期許容 / 損傷限界→短期許容）の検証。
    ///
    /// UI・グラフ・計算書・Help の 4 面で同じ ConcreteModelOptions.MapLimitStateText を
    /// 通す設計のため、ここを固めておけば表示の一貫性が保たれる。
    /// </summary>
    [TestClass]
    public class LimitStateLabelTests
    {
        [TestInitialize]
        [TestCleanup]
        public void ResetOptions()
        {
            // static オプションはテスト間で共有されるため必ず既定へ戻す
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
        }

        [TestMethod]
        public void オプションOFFなら表示名は変換されない()
        {
            Assert.IsFalse(ConcreteModelOptions.UseAllowableStressLabels);
            Assert.AreEqual("使用限界支持力", ConcreteModelOptions.MapLimitStateText("使用限界支持力"));
            Assert.AreEqual("損傷限界引抜力", ConcreteModelOptions.MapLimitStateText("損傷限界引抜力"));
        }

        [TestMethod]
        public void 圧縮オプションONで使用限界と損傷限界が長期短期許容になる()
        {
            ConcreteModelOptions.UseNotification1113Compression = true;

            Assert.IsTrue(ConcreteModelOptions.UseAllowableStressLabels);
            Assert.AreEqual("長期許容支持力", ConcreteModelOptions.MapLimitStateText("使用限界支持力"));
            Assert.AreEqual("短期許容引抜力", ConcreteModelOptions.MapLimitStateText("損傷限界引抜力"));
        }

        [TestMethod]
        public void せん断オプションのみONでも表示名が切り替わる()
        {
            ConcreteModelOptions.UseNotification1113Shear = true;

            Assert.IsTrue(ConcreteModelOptions.UseAllowableStressLabels);
            Assert.AreEqual("短期許容", ConcreteModelOptions.MapLimitStateText("損傷限界"));
        }

        [TestMethod]
        public void 安全限界は変換対象外()
        {
            ConcreteModelOptions.UseNotification1113Compression = true;

            // 安全限界は告示の許容応力度の対象外なので名称は変わらない
            Assert.AreEqual("安全限界", ConcreteModelOptions.MapLimitStateText("安全限界"));
        }

        /// <summary>
        /// GraphWindow / DocxOutputWindow のツールチップは ConverterParameter に
        /// 文章まるごとを渡している。文中の限界状態名だけが置換されること。
        /// </summary>
        [TestMethod]
        public void ツールチップ文中の限界状態名が置換される()
        {
            ConcreteModelOptions.UseNotification1113Compression = true;

            Assert.AreEqual(
                "レベル1: 短期許容 / レベル2: 安全限界",
                ConcreteModelOptions.MapLimitStateText("レベル1: 損傷限界 / レベル2: 安全限界"));

            Assert.AreEqual(
                "レベル1→短期許容、レベル2+グレードS→短期許容、レベル2+グレードA→安全限界 (低減後値) を自動選択",
                ConcreteModelOptions.MapLimitStateText(
                    "レベル1→損傷限界、レベル2+グレードS→損傷限界、レベル2+グレードA→安全限界 (低減後値) を自動選択"));
        }

        [TestMethod]
        public void 空文字やnullでも例外にならない()
        {
            ConcreteModelOptions.UseNotification1113Compression = true;

            Assert.AreEqual(string.Empty, ConcreteModelOptions.MapLimitStateText(string.Empty));
            Assert.IsNull(ConcreteModelOptions.MapLimitStateText(null));
        }
    }
}
