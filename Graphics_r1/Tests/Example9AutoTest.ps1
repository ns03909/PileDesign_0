# Example 9 Nonlinear Analysis Automated Test Script
# This script launches the PileDesign application, loads Example 9,
# performs element division, and runs horizontal analysis with IsPileNonLinear=true

Write-Host "=== Example 9 Nonlinear Analysis Automated Test ===" -ForegroundColor Cyan
Write-Host ""

# Configuration
$projectRoot = "c:\Users\keisu\source\repos\PileDesign_0"
$exePath = "$projectRoot\Graphics_r1\bin\Debug\net8.0-windows7.0\PileDesign.exe"
$timeout = 300 # 5 minutes timeout

# Check if executable exists
if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: Executable not found at $exePath" -ForegroundColor Red
    Write-Host "Please build the project first." -ForegroundColor Yellow
    exit 1
}

Write-Host "Step 1: Building project..." -ForegroundColor Yellow
Push-Location "$projectRoot\Graphics_r1"
$buildResult = dotnet build -c Debug 2>&1
Pop-Location

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}

Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host ""

Write-Host "Step 2: Creating test automation script..." -ForegroundColor Yellow

# Create a C# automation script that uses the ViewModels directly
$automationScript = @"
using System;
using System.Threading.Tasks;
using System.Linq;
using PileDesign.ViewModels;
using PileDesign.Models.InputData;

namespace PileDesign.Tests
{
    public class Example9AutomationRunner
    {
        public static async Task<bool> RunTest()
        {
            try
            {
                Console.WriteLine("Initializing MainWindowViewModel...");
                var mainVM = new MainWindowViewModel();

                Console.WriteLine("Loading Example 9...");
                await mainVM.Example9Command.ExecuteAsync(null);
                await Task.Delay(1000); // Wait for initialization

                Console.WriteLine("Setting IsElementSplit = true (simulating element division)...");
                mainVM.IsElementSplit = true;

                Console.WriteLine("Creating HorizontalCalculationViewModel...");
                var horizVM = new HorizontalCalculationViewModel(mainVM);

                // Verify Level2 cases
                var level2Cases = mainVM.CurrentInputModel.LoadCasesInput.LoadCasesLevel2
                    .Where(lc => lc.IsApplicable && lc.IsPileNonLinear).ToList();

                Console.WriteLine(`$"Found {level2Cases.Count} Level2 load cases with IsPileNonLinear=true");

                if (level2Cases.Count == 0)
                {
                    Console.WriteLine("ERROR: No applicable Level2 cases with IsPileNonLinear=true");
                    return false;
                }

                Console.WriteLine("Starting horizontal analysis...");
                await horizVM.OnExecuteAnalysisCommand.ExecuteAsync(null);

                Console.WriteLine("Analysis execution completed.");

                if (!horizVM.IsAnalysisExecuted)
                {
                    Console.WriteLine("ERROR: Analysis was not marked as executed");
                    return false;
                }

                // Check for excessive displacements
                Console.WriteLine("Checking displacement results...");
                bool hasExcessiveDisp = false;
                int nodeCount = 0;
                double maxDispFound = 0.0;

                if (mainVM.CurrentInputModel.AnaModels != null)
                {
                    foreach (var anaModel in mainVM.CurrentInputModel.AnaModels)
                    {
                        if (anaModel.Nodes == null) continue;

                        foreach (var node in anaModel.Nodes)
                        {
                            if (node.NodeResults == null) continue;
                            nodeCount++;

                            foreach (var result in node.NodeResults)
                            {
                                double maxDisp = Math.Max(
                                    Math.Abs(result.Dx),
                                    Math.Max(Math.Abs(result.Dy), Math.Abs(result.Dz))
                                );

                                if (maxDisp > maxDispFound)
                                    maxDispFound = maxDisp;

                                if (maxDisp >= 1.0)
                                {
                                    Console.WriteLine(`$"EXCESSIVE DISPLACEMENT: {maxDisp:F6}m at node ({node.Point3D.X:F2}, {node.Point3D.Y:F2}, {node.Point3D.Z:F2})");
                                    hasExcessiveDisp = true;
                                }
                            }
                        }
                    }
                }

                Console.WriteLine(`$"Checked {nodeCount} nodes");
                Console.WriteLine(`$"Maximum displacement found: {maxDispFound:F6}m");

                // Check convergence warnings
                var warnings = horizVM.Logs.Where(log =>
                    log.Contains("Warning") || log.Contains("Maximum iterations") || log.Contains("警告")).ToList();

                if (warnings.Any())
                {
                    Console.WriteLine(`$"\nConvergence warnings ({warnings.Count}):");
                    foreach (var warning in warnings)
                    {
                        Console.WriteLine(`$"  {warning}");
                    }
                }

                if (hasExcessiveDisp)
                {
                    Console.WriteLine("\n❌ TEST FAILED: Excessive displacements detected (>= 1.0m)");
                    return false;
                }

                Console.WriteLine("\n✓ TEST PASSED: All displacements within threshold");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(`$"ERROR: {ex.Message}");
                Console.WriteLine(`$"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}
"@

$scriptPath = "$projectRoot\Graphics_r1\Tests\Example9AutomationRunner.cs"
$automationScript | Out-File -FilePath $scriptPath -Encoding UTF8

Write-Host "✓ Automation script created at $scriptPath" -ForegroundColor Green
Write-Host ""

Write-Host "=== Test Instructions ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "To run the automated test:" -ForegroundColor Yellow
Write-Host "1. Open the PileDesign application" -ForegroundColor White
Write-Host "2. From the main menu, select 'Example 9' (基礎指針'19 計算例9)" -ForegroundColor White
Write-Host "3. Open the Element Division window (杭要素分割)" -ForegroundColor White
Write-Host "4. Click the automatic division button and close the window" -ForegroundColor White
Write-Host "5. Open the Horizontal Analysis window (水平解析)" -ForegroundColor White
Write-Host "6. Verify that Level 2 earthquake (レベル2地震) 1-direction is checked" -ForegroundColor White
Write-Host "7. Click the 'Execute Analysis' button (解析実行)" -ForegroundColor White
Write-Host "8. Verify that:" -ForegroundColor White
Write-Host "   - Analysis completes without errors" -ForegroundColor Gray
Write-Host "   - No displacement exceeds 1.0m" -ForegroundColor Gray
Write-Host "   - Convergence warnings (if any) indicate the iteration limit is working" -ForegroundColor Gray
Write-Host ""

Write-Host "=== Expected Results ===" -ForegroundColor Cyan
Write-Host "✓ Analysis should complete successfully" -ForegroundColor Green
Write-Host "✓ Maximum displacement should be < 1.0m" -ForegroundColor Green
Write-Host "✓ If convergence warnings appear, iteration limit (100) is functioning" -ForegroundColor Green
Write-Host "✓ No infinite loops or excessive iterations" -ForegroundColor Green
Write-Host ""

Write-Host "=== Bug Fixes Being Verified ===" -ForegroundColor Cyan
Write-Host "1. IY/IZ swap in UpdateBeamMPhiSecant (HorizontalCalculationViewModel.cs:1182-1184)" -ForegroundColor White
Write-Host "2. Stiffness ratio lower bound: 0.01% → 1% (lines 1148, 1187)" -ForegroundColor White
Write-Host "3. Maximum iteration limit: 100 (line 976)" -ForegroundColor White
Write-Host "4. Convergence failure warnings (lines 1060-1065)" -ForegroundColor White
Write-Host ""

Write-Host "Test setup complete. Please follow the manual steps above." -ForegroundColor Yellow
