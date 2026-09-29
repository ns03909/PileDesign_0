using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1;

/// <summary>
/// 画面の変換器の逆変換・自動保存の重なり・解析結果の控えの失敗の知らせ。
/// </summary>
[TestClass]
[DoNotParallelize]
public class RobustnessFollowUpTests
{
    /// <summary>
    /// 逆変換を使わない変換器は、例外ではなく「書き戻さない」を返すこと。
    /// 以前は NotImplementedException を投げ、束縛を双方向に変えると画面の操作で例外になった。
    /// </summary>
    [TestMethod]
    public void DoubleLessThanConverter_ConvertBackDoesNotThrow()
    {
        var converter = new DoubleLessThanConverter();
        Assert.AreSame(System.Windows.Data.Binding.DoNothing,
            converter.ConvertBack(true, typeof(double), "1", CultureInfo.InvariantCulture));
    }
}
