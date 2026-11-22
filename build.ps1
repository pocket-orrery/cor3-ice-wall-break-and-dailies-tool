param(
    [string]$ReferenceScreenshot,
    [string]$PartialScreenshot,
    [string]$FullScreenshot
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
$src = Join-Path $root 'src'
$tests = Join-Path $root 'tests'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Path $build -Force | Out-Null
$common = @(
    '/nologo', '/utf8output', '/codepage:65001', '/warnaserror+', '/optimize+',
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    "/win32manifest:$src\app.manifest"
)
$engine = @("$src\Matcher.cs", "$src\TargetPattern.cs")
$finder = @("$src\Program.cs", "$src\SelectionWindow.cs", "$src\LanguagePreference.cs", "$src\Theme.cs", "$src\JobRound.cs", "$src\JobGuideWindow.cs", "$src\DailyTasks.cs", "$src\LogClockRecognizer.cs", "$src\WindowsOcr.cs", "$src\DailyTaskWindow.cs",
    "$src\TerminalPuzzle.cs", "$src\TerminalPuzzleWindow.cs")

& $compiler @common /main:TestChecks "/out:$build\TestChecks.exe" @engine @finder "$tests\TestBoards.cs" "$tests\TestChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the fixture checks.' }
$fixtures = Join-Path $build 'fixtures'
$checkArgs = @($fixtures)
if ($ReferenceScreenshot) {
    $checkArgs += (Resolve-Path -LiteralPath $ReferenceScreenshot).ProviderPath
}
if ($PartialScreenshot) {
    $checkArgs += '--partial-frame', (Resolve-Path -LiteralPath $PartialScreenshot).ProviderPath
}
if ($FullScreenshot) {
    $checkArgs += '--full-frame', (Resolve-Path -LiteralPath $FullScreenshot).ProviderPath
}
& "$build\TestChecks.exe" @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'A fixture check failed.' }

& $compiler @common /target:winexe "/out:$build\TriangleGlyphFinder.exe" "/resource:$fixtures\03-three-tile-match.png,sample.png" @engine @finder
if ($LASTEXITCODE -ne 0) { throw 'Could not compile TriangleGlyphFinder.' }
& $compiler @common /target:winexe "/out:$build\TriangleGlyphSimulator.exe" @engine "$tests\TestBoards.cs" "$tests\Simulator.cs"
if ($LASTEXITCODE -ne 0) { throw 'Could not compile TriangleGlyphSimulator.' }
Write-Output "Built $build\TriangleGlyphFinder.exe and $build\TriangleGlyphSimulator.exe"
