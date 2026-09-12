using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;

namespace TestProject1
{
    /// <summary>
    /// テストの実行中は<b>ダイアログを出さない</b>。
    ///
    /// <para>回帰テストは水平解析の入口 (<c>HorizontalCalculationViewModel</c>) を通る。
    /// そこには <c>MessageService.Show</c> が 20 か所あり、どれかに当たると
    /// <b>誰も押せないダイアログの前で実行が止まる</b>。落ちないので、待っているのか
    /// 計算しているのかも分からない。</para>
    ///
    /// <para>2026-09-12、例題ビルダーを実機の読込に寄せた拍子に「基礎梁が定義されて
    /// いないため剛体連結モードに切り替えて解析を実行します」が出て、全体実行が固まった。
    /// 出た理由 (基礎梁の既定が剛床連結) は実機と同じ扱いで正しいので、
    /// ダイアログの側を無人実行モードにする。</para>
    /// </summary>
    [TestClass]
    public static class UnattendedRunSetup
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            MessageService.IsUnattended = true;
        }
    }

    /// <summary>無人実行モードが実際に効いていること。</summary>
    [TestClass]
    public class UnattendedRunSetupTests
    {
        [TestMethod]
        public void DialogsAreSuppressedDuringTheRun()
        {
            Assert.IsTrue(MessageService.IsUnattended,
                "無人実行モードが立っていません。解析の入口のダイアログでテストが止まります");
        }
    }
}
