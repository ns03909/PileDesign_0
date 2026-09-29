using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;

namespace TestProject1;

/// <summary>
/// 入力の検査が投げた例外を画面に出すときの文言 (<see cref="MessageService.ShowInputRejected"/>)。
///
/// 検査の文言は利用者向けに書いてあるのでそのまま出すが、引数名の添え書き「 (Parameter 'value')」は
/// 内部の名前なので外す。利用者向けに書かれていない例外 (既定の英語の文言) は想定外の失敗として扱う。
/// </summary>
[TestClass]
public class InputRejectedMessageTests
{
    [TestMethod]
    public void TheParameterSuffixIsRemoved()
    {
        var ex = new ArgumentOutOfRangeException("value", "座標には有限の数値を入力してください。");
        StringAssert.Contains(ex.Message, "Parameter", "前提が崩れている (.NET が添え書きを付けなくなった)");
        Assert.AreEqual("座標には有限の数値を入力してください。", MessageService.UserFacingText(ex));
    }

    [TestMethod]
    public void AMessageWithoutParameterIsShownAsIs()
        => Assert.AreEqual("通り心の累積座標が数値の範囲外です。",
            MessageService.UserFacingText(new ArgumentException("通り心の累積座標が数値の範囲外です。")));

    /// <summary>既定の文言 (英語) は利用者向けでないので空を返し、呼び出し側は想定外の失敗として出す。</summary>
    [TestMethod]
    public void AFrameworkMessageIsNotTreatedAsUserFacing()
    {
        Assert.AreEqual("", MessageService.UserFacingText(new ArgumentNullException("items")));
        Assert.AreEqual("", MessageService.UserFacingText(new ArgumentException("Value does not fall within the expected range.")));
    }
}
