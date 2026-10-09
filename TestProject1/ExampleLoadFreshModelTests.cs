using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestProject1;

/// <summary>
/// 計算例の読み込みが、<b>直前に開いていたプロジェクトに左右されない</b>こと。
///
/// <para>以前は画面の入力の複製の上に計算例を書き込んでいたので、計算例のファイルが持たない項目
/// (基本設定の材料オプション・荷重ケースの非線形の指定と荷重角度・杭先端の P-S ばね・
/// 基礎のねじれ拘束・kh0 の手入力など) が前のプロジェクトから計算例へ持ち込まれていた。
/// 確認のダイアログは「現状の入力内容は削除されます」と言っている。</para>
///
/// <para>設定を揺らしたあとで読み込んだ結果と、起動直後に読み込んだ結果を、保存と同じ直列化で比べる。</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class ExampleLoadFreshModelTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    private static Task Load(MainWindowViewModel vm, bool group, string name, string? supplementary = null) =>
        (Task)typeof(MainWindowViewModel).GetMethod(group ? "LoadGroupSettlementExampleAsync" : "LoadPileExampleAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, group ? [name, "Test"] : [name, "Test", supplementary])!;

    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            task.GetAwaiter().OnCompleted(() => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Sta(Action action)
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
        Assert.IsFalse(timedOut, "STA スレッドで時間内に終わりませんでした");
        if (error is AssertFailedException || error is AssertInconclusiveException) throw error;
        Assert.IsNull(error, error?.ToString());
    }

    private static JsonSerializerOptions SaveOptions() => new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    // InputNode.Id は読み込みのたびに振り直される番号 (DeepCopyIsSaveEquivalentTests と同じ扱い)。
    // 節点の UniqueId (GUID) は作るたびに新しく振られるので、読み込みごとに違って当然。
    // 出てきた順の番号に置き換え、どれがどれを指すか (基礎梁の NodeI_Id など) は比べる
    private static readonly Regex _idLineRegex = new("\"Id\": \\d+", RegexOptions.Compiled);
    private static readonly Regex _guidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private static string Signature(InputModel input)
    {
        string json = _idLineRegex.Replace(JsonSerializer.Serialize(input, SaveOptions()), "\"Id\": *");
        var order = new Dictionary<string, int>();
        return _guidRegex.Replace(json, m =>
        {
            if (!order.TryGetValue(m.Value, out int n)) order[m.Value] = n = order.Count;
            return $"guid#{n}";
        });
    }

    /// <summary>プロセス共有の材料オプションを、設定できる全項目について読み取る。</summary>
    private static Dictionary<string, object?> CaptureConcreteModelOptions() =>
        typeof(ConcreteModelOptions).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanWrite && p.GetSetMethod() != null)
            .ToDictionary(p => p.Name, p => p.GetValue(null));

    private static void AssertSameSignature(string expected, string actual, string what)
    {
        if (expected == actual) return;
        var a = expected.Split('\n'); var b = actual.Split('\n');
        var diffs = new List<string>();
        for (int i = 0; i < Math.Min(a.Length, b.Length) && diffs.Count < 10; i++)
            if (a[i] != b[i]) diffs.Add($"  起動直後 {a[i].Trim()} / 設定を変えた後 {b[i].Trim()}");
        Assert.Fail($"{what}: 直前のプロジェクトの設定が計算例へ持ち込まれました (行数 {a.Length} / {b.Length})。"
            + Environment.NewLine + string.Join(Environment.NewLine, diffs));
    }

    /// <summary>
    /// 計算例のファイルが持たない設定を、ひととおり既定から外す。
    /// 材料のオプションは画面と同じく ApplyConcreteModelOptions でプロセス共有の状態へも流す。
    /// </summary>
    private static void PerturbSettings(MainWindowViewModel vm)
    {
        var input = vm.CurrentInputModel!;
        var f = input.FundamentalInput;
        f.IgnoreConcreteTensileStrength = !f.IgnoreConcreteTensileStrength;
        f.UseGuideYoungsModulus = !f.UseGuideYoungsModulus;
        f.UseFiberMPhi = !f.UseFiberMPhi;
        f.UseNotification1113Shear = !f.UseNotification1113Shear;
        f.UseUnitGsiForConcreteE = !f.UseUnitGsiForConcreteE;
        vm.ApplyConcreteModelOptions();

        foreach (var lc in input.LoadCasesInput.EveryLoadCase)
        {
            lc.IsPileNonLinear = !lc.IsPileNonLinear;
            lc.LoadAngle += 15.0;
        }

        input.UsePsSpringAtPileTip = true;
        input.IsVLAnalysisEnabled = true;
        input.RestrainFoundationTorsion = true;
        input.UseAnalysisAxialForce = true;

        // 土層-杭セットの手入力 (GenerateSoilPiles が (地盤番号・杭体番号・杭頭 Z) の一致で引き継ぐもの)
        foreach (var sp in input.ElementDivision.SoilPiles)
        {
            sp.Kh0LayerOverrides ??= [];
            sp.Kh0LayerOverrides.Add(new Kh0LayerOverride { LayerName = "手入力", Kh0 = 12345.0 });
            sp.GroupPileLoadDia = 9.9;
        }
    }

    [DataTestMethod]
    [DataRow(false, "PileExample3_1", null)]
    [DataRow(false, "PileExample5", "GroupSettlement5")]
    [DataRow(true, "GroupSettlement5", null)]
    public void LoadedExample_DoesNotDependOnThePreviousProject(bool group, string name, string? supplementary) => Sta(() =>
    {
        try
        {
            // 起動直後に読み込んだもの
            string expected;
            Dictionary<string, object?> expectedOptions;
            using (var fresh = new MainWindowViewModel())
            {
                fresh.MarkProjectReplaced();
                Wait(Load(fresh, group, name, supplementary));
                expected = Signature(fresh.CurrentInputModel!);
                expectedOptions = CaptureConcreteModelOptions();
            }

            // 同じ計算例を読み、設定を変えてから、もう一度読み込む
            using var vm = new MainWindowViewModel();
            vm.MarkProjectReplaced();
            Wait(Load(vm, group, name, supplementary));
            Assert.IsTrue(vm.CurrentInputModel!.ElementDivision.SoilPiles.Count > 0, "土層-杭セットが作られていません");
            PerturbSettings(vm);
            Assert.AreNotEqual(expected, Signature(vm.CurrentInputModel!), "設定を揺らせていません");
            vm.MarkProjectReplaced();
            Wait(Load(vm, group, name, supplementary));

            AssertSameSignature(expected, Signature(vm.CurrentInputModel!), name);
            CollectionAssert.AreEquivalent(expectedOptions.ToList(), CaptureConcreteModelOptions().ToList(),
                "材料のオプション (プロセス共有) が計算例の既定値に戻っていません");

            // 署名にも含まれるが、持ち込まれ方がいちばん見えにくいものは名指しで確かめる
            Assert.IsTrue(vm.CurrentInputModel!.ElementDivision.SoilPiles
                .All(sp => sp.Kh0LayerOverrides == null || sp.Kh0LayerOverrides.Count == 0),
                "前のプロジェクトの kh0 の手入力が計算例へ持ち込まれました");
        }
        finally
        {
            // プロセス共有の状態を既定へ戻す (ほかのテストを汚さない)
            using var reset = new MainWindowViewModel();
            reset.ApplyConcreteModelOptions();
            PileDesign.Common.AxialForceModeContext.IsVariationMode = false;
        }
    });

    /// <summary>
    /// 計算例の組み立てが使う材料オプションの対応 (SyncStaticOptionsFor) が、
    /// 画面の正の経路 (ApplyConcreteModelOptions) と<b>全項目で</b>同じであること。
    /// 片方にだけ項目を足すと、計算例の断面の諸元がその項目だけ前のプロジェクトの値で計算される。
    /// </summary>
    [TestMethod]
    public void SyncStaticOptions_MatchesApplyConcreteModelOptions() => Sta(() =>
    {
        try
        {
            using var vm = new MainWindowViewModel();
            // 既定は白紙の入力から画面の経路で作る (前のテストが残した状態を拾わない)
            vm.ApplyConcreteModelOptions();
            var defaults = CaptureConcreteModelOptions();

            // 基本設定を全項目、既定から外す
            var f = vm.CurrentInputModel!.FundamentalInput;
            int touched = 0;
            foreach (var p in typeof(FundamentalInput).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (p.PropertyType == typeof(bool)) { p.SetValue(f, !(bool)p.GetValue(f)!); touched++; }
                else if (p.PropertyType == typeof(double)) { p.SetValue(f, (double)p.GetValue(f)! + 0.123); touched++; }
                else if (p.PropertyType == typeof(int) && p.Name.Contains("Notification1113")) { p.SetValue(f, (int)p.GetValue(f)! == 1 ? 2 : 1); touched++; }
            }
            Assert.IsTrue(touched > 10, $"基本設定の項目を {touched} 個しか揺らせていません");

            vm.ApplyConcreteModelOptions();
            var viaApply = CaptureConcreteModelOptions();

            // 一度既定へ戻してから、計算例の経路でそろえる
            typeof(MainWindowViewModel).GetMethod("SyncStaticOptionsFor", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [null]);
            CollectionAssert.AreEquivalent(defaults.ToList(), CaptureConcreteModelOptions().ToList(),
                "入力が無いときに既定へ戻りません");
            typeof(MainWindowViewModel).GetMethod("SyncStaticOptionsFor", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [vm.CurrentInputModel]);
            var viaSync = CaptureConcreteModelOptions();

            foreach (var (key, value) in viaApply)
                Assert.AreEqual(value, viaSync[key], $"ConcreteModelOptions.{key} が ApplyConcreteModelOptions と食い違います");

            // 揺らした結果が既定と違う項目が十分にあること (網が素通りしていない)
            int changed = viaApply.Count(kv => !Equals(kv.Value, defaults[kv.Key]));
            Assert.IsTrue(changed >= 10, $"既定から動いた材料オプションが {changed} 個しかありません");
        }
        finally
        {
            using var reset = new MainWindowViewModel();
            reset.ApplyConcreteModelOptions();
        }
    });
}
