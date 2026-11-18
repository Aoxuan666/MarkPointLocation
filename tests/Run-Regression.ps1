param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    $msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    if (Test-Path $msbuild) {
        & $msbuild (Join-Path $root 'MarkPointLocation.sln') /p:Configuration=Release /p:Platform=x64 /nologo /v:minimal
    } else {
        dotnet build (Join-Path $root 'MarkPointLocation.sln') -c Release -p:Platform=x64 --nologo
    }
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (-not [Environment]::Is64BitProcess) { throw 'Run this script in x64 Windows PowerShell.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run Windows PowerShell with -STA.' }
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$bin = Join-Path $root 'src/Seprays.MarkVision/bin/Release'
$null = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'halcondotnet.dll'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Seprays.MarkVision.dll'))
$demoAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'demo/Seprays.MarkVision.Demo/bin/Release/Seprays.MarkVision.Demo.exe'))
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RegressionChecks.cs'))) -ReferencedAssemblies @(
    'System.Drawing', 'System.Windows.Forms', (Join-Path $bin 'Seprays.MarkVision.dll'))
[RegressionChecks]::BusyState()
[RegressionChecks]::SetupDefaults()
[RegressionChecks]::SdkContract((Join-Path $root 'artifacts/sdk-confirmation-verified.png'))
[RegressionChecks]::ReuseTeachModelForTestImage()
[RegressionChecks]::SetupControlSingleImageWorkflow()
[RegressionChecks]::SetupControlTestsCurrentImage((Join-Path $root 'artifacts/inspection-hud-verified.png'), $demoAssembly.GetType('Seprays.MarkVision.Demo.MainForm', $true))
[RegressionChecks]::MaskedModelAndPolarity()
[RegressionChecks]::LayoutAndRoiInteraction((Join-Path $root 'artifacts/single-image-roi-verified.png'), $demoAssembly.GetType('Seprays.MarkVision.Demo.MainForm', $true))
[RegressionChecks]::BrushMask()
[RegressionChecks]::BrushRealtime((Join-Path $root 'artifacts/brush-realtime-verified.png'))
[RegressionChecks]::CursorRepaint()
[RegressionChecks]::CircleRois()
[RegressionChecks]::Rotation(0, 1)
[RegressionChecks]::Rotation(20, 1.04)
[RegressionChecks]::Rotation(-20, 0.96)
[RegressionChecks]::AutoThresholdAndStability()
[RegressionChecks]::HalconCircleAndResultImage()
[RegressionChecks]::PageTestMatchesSdkRunAndDDriveLogging()
[RegressionChecks]::RoiInteractionResponsiveness()
