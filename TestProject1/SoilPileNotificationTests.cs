using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭体ウィンドウが出している計算値が、まとめて通知されること。
    ///
    /// あちらは <c>TemporarySoilPile</c> を組み直したあと
    /// <c>UpdateProperties()</c> → <c>NotifyAllPropertiesChanged()</c> を呼んで画面を更新する。
    /// この通知は<b>プロパティ名を手で並べたもの</b>なので、画面に列を足したときに
    /// 並べ忘れると<b>その欄だけ古い値のまま残る</b>。値は正しく、画面だけが古い形。
    /// 1.0.31-beta で同じ形を 4 か所直している (根入れの DX・DY、荷重ケースの ΣH、杭頭部の Ec)。
    ///
    /// <para><b>比べる相手は「画面が出している項目」で、公開プロパティ全部ではない。</b>
    /// 2026-09-10 に全部と比べたところ 81 個中 41 個が通知されていないと出たが、
    /// そのうち画面に束縛されているものは無かった。分母を間違えると、直す必要のないものを
    /// 41 件の不具合として報告してしまう。</para>
    /// </summary>
    [TestClass]
    public class SoilPileNotificationTests
    {
        [TestMethod]
        public void EveryValueShownByThePileBodyWindow_IsNotified()
        {
            string xaml = File.ReadAllText(
                Path.Combine(TestSource.Root(), "Graphics_r1", "Views", "PileBodyWindow.xaml"));

            // 画面が TemporarySoilPile から出している項目
            var shown = Regex.Matches(xaml, @"Binding\s+(?:Path=)?TemporarySoilPile\.([A-Za-z0-9_]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            TestSource.AssertScanned(shown.Count, 10, "杭体ウィンドウが出す土質杭の項目");

            // 実際に通知される名前を集める (ソースを読むのではなく呼んで確かめる)
            var raised = new List<string>();
            var probe = new SoilPile();
            ((INotifyPropertyChanged)probe).PropertyChanged += (s, e) => raised.Add(e.PropertyName ?? "");
            probe.NotifyAllPropertiesChanged();

            // 空文字は「全プロパティ」の意味なので、それが来ているなら全部通知されている
            if (raised.Contains("")) return;

            var missing = shown.Where(n => !raised.Contains(n)).ToList();

            Assert.AreEqual(0, missing.Count,
                "杭体ウィンドウが出している項目が通知されていません。"
                + "その欄だけ古い値のまま残ります (値は正しく、画面だけが古い形):"
                + Environment.NewLine + "  " + string.Join(", ", missing));
        }
    }
}
