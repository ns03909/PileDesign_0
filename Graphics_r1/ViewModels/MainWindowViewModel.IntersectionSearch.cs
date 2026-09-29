using PileDesign.Models;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    internal const double SplitPointDistanceTolerance = PileDesign.Common.GeometryTolerance.MinMemberLength; // 分割点が端から近すぎれば分割しない (部材の長さの下限と同じ)
    private const int MaxIntersectionPairs = 1_000_000;
    internal readonly record struct BeamIntersection(int A, int B, Point3D Point, double TA, double TB);
    private sealed class SplitSearchProgress(IProgress<AnalysisProgress>? target) : IProgress<AnalysisProgress>
    {
        public void Report(AnalysisProgress value)
        {
            value.Percentage *= 0.6;
            target?.Report(value);
        }
    }

    internal static List<BeamIntersection> SearchBeamIntersections(
        IReadOnlyList<(Point3D Start, Point3D End)> segments, double tolerance,
        CancellationToken token, IProgress<AnalysisProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        int axis = SpatialAxis.Choose(segments.SelectMany(s => new[] { s.Start, s.End }), token);
        Point3D Rotate(Point3D p) => axis == 0 ? p : axis == 1 ? new Point3D(p.Y,p.X,p.Z) : new Point3D(p.Z,p.Y,p.X);
        int sortChecks = 0;
        var sorted = MaterializeForSplit(() => segments.Select(s => { token.ThrowIfCancellationRequested(); return (OriginalStart:s.Start, OriginalEnd:s.End, Start:Rotate(s.Start), End:Rotate(s.End)); }).Select((s, i) => (Start:s.OriginalStart, End:s.OriginalEnd, Index: i,
            MinX: Math.Min(s.Start.X, s.End.X), MaxX: Math.Max(s.Start.X, s.End.X),
            MinY: Math.Min(s.Start.Y, s.End.Y), MaxY: Math.Max(s.Start.Y, s.End.Y),
            MinZ: Math.Min(s.Start.Z, s.End.Z), MaxZ: Math.Max(s.Start.Z, s.End.Z)))
            .OrderBy(s => s.MinX, Comparer<double>.Create((a,b) =>
            {
                if ((++sortChecks & 1023) == 0) token.ThrowIfCancellationRequested();
                return a.CompareTo(b);
            })).ThenBy(s => s.Index).ToArray(), token);
        var result = new List<BeamIntersection>();
        long maximumPairs = (long)segments.Count * (segments.Count - 1) / 2;
        long compared = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = sorted[i];
            for (int j = i + 1; j < sorted.Length; j++)
            {
                if ((j & 1023) == 0) token.ThrowIfCancellationRequested();
                var b = sorted[j];
                if (b.MinX > a.MaxX + tolerance) break;
                if (b.MinY > a.MaxY + tolerance || a.MinY > b.MaxY + tolerance ||
                    b.MinZ > a.MaxZ + tolerance || a.MinZ > b.MaxZ + tolerance) continue;
                compared++;
                var intersection = FindSegmentIntersection(a.Start, a.End, b.Start, b.End, tolerance, out var problem);
                if (problem != null) throw new ArgumentException(problem);
                if (intersection.HasValue)
                {
                    var (point, ta, tb) = intersection.Value;
                    result.Add(new BeamIntersection(a.Index, b.Index, point, ta, tb));
                    if (result.Count > MaxIntersectionPairs)
                        throw new ArgumentException("交差する梁の組合せが100万件を超えました。対象の梁を分けて実行してください。");
                }
            }
            if ((i & 31) == 0 || i == sorted.Length - 1)
                progress?.Report(new AnalysisProgress
                {
                    Percentage = 100.0 * (i + 1) / sorted.Length,
                    CurrentStepNumber = i + 1, TotalSteps = sorted.Length,
                    CurrentStep = $"交差点探索：最大 {maximumPairs:N0} 組／詳細判定 {compared:N0} 組／交差候補 {result.Count:N0} 件",
                });
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static T[] MaterializeForSplit<T>(Func<T[]> build, CancellationToken token)
    {
        try { return build(); }
        catch (InvalidOperationException) when (token.IsCancellationRequested)
        { token.ThrowIfCancellationRequested(); throw; }
    }

    private List<BeamIntersection> RunIntersectionSearch(IReadOnlyList<(Point3D Start, Point3D End)> segments, double tolerance)
        => RunIntersectionWork(segments.Count, (token, progress) => SearchBeamIntersections(segments, tolerance, token, progress));

    private T RunIntersectionWork<T>(int count, Func<CancellationToken, IProgress<AnalysisProgress>?, T> work)
    {
        var app = Application.Current;
        if (count < 100 || MessageService.IsUnattended || app == null || !app.Dispatcher.CheckAccess())
            return work(CancellationToken.None, null);

        using var cancellation = new CancellationTokenSource();
        var owner = app.MainWindow?.IsVisible == true ? app.MainWindow : null;
        var window = new ProgressWindow(cancellation,
            "交差点分割を中断しますか？\n\n入力は変更されず、今回の分割案は破棄されます。")
        { Title = "交差点分割", Owner = owner };
        window.UpdateProgress(new AnalysisProgress
        {
            CurrentStep = $"選択梁 {count:N0} 本／判定上限 {(long)count * (count - 1) / 2:N0} 組",
            TotalSteps = count,
        });
        T result = default!;
        Exception? failure = null;
        bool completed = false;
        window.Closing += (_, e) =>
        {
            if (!completed)
            {
                cancellation.Cancel();
                e.Cancel = true;
            }
        };
        window.Loaded += async (_, _) =>
        {
            var progress = new Progress<AnalysisProgress>(window.UpdateProgress);
            try
            {
                result = await Task.Run(() => work(cancellation.Token, progress));
                cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // Closing after completion or cancellation is always permitted.
                completed = true;
                cancellation.Cancel();
                window.Close();
            }
        };
        window.ShowDialog();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
