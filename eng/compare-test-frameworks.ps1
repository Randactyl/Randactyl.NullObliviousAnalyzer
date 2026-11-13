<#
.SYNOPSIS
  Compares the xUnit v3 and TUnit test projects: build time, run time, generated code, output size.

.DESCRIPTION
  Writes raw results to docs/experiments/xunit-vs-tunit-results.json (override with -OutFile).
  Run from anywhere on a quiet machine: it shuts down dotnet build servers, deletes bin/obj of the two test
  projects only, and builds/tests with Debug configuration unless noted. Expect 20-30 minutes at -Runs 10.

  Each timing is: one untimed warm-up, then -Runs timed runs, interleaved (xunit, tunit, xunit, ...) so slow
  drift on the machine hits both the same way. Reported as median, min, max, plus every sample.

  The 'NoOp' configuration compiles FrameworkOverheadNoOp.cs (one empty test) into each project; the normal
  configuration does not, so the suite stays at 105 tests.

.PARAMETER Runs
  Timed runs per measurement (minimum 10 for the report to mean anything).
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)][int]$Runs = 10,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutFile) { $OutFile = Join-Path $root 'docs/experiments/xunit-vs-tunit-results.json' }
Set-Location $root

$testingProj = Join-Path $root 'tests/Randactyl.NullObliviousAnalyzer.Testing/Randactyl.NullObliviousAnalyzer.Testing.csproj'
$testingBin  = Join-Path $root 'tests/Randactyl.NullObliviousAnalyzer.Testing/bin/Debug/net11.0'
$ns = 'Randactyl.NullObliviousAnalyzer.Tests'
$singleClass = 'CallResultTests'; $singleMethod = 'Method_from_oblivious_assembly'

$fw = [ordered]@{
    xunit = @{
        Name = 'xunit'; Dir = Join-Path $root 'tests/Randactyl.NullObliviousAnalyzer.Tests.XUnit'
        Proj = 'Randactyl.NullObliviousAnalyzer.Tests.XUnit'
        Single = @('--filter-method', "$ns.$singleClass.$singleMethod")
        NoOp   = @('--filter-method', "$ns.FrameworkOverheadNoOp.NoOp")
        # Run directly, the xunit v3 executable uses xunit's own runner (not Microsoft.Testing.Platform) with its own switches.
        ExeSingle = @('-method', "$ns.$singleClass.$singleMethod", '-nologo')
        ExeNoOp   = @('-method', "$ns.FrameworkOverheadNoOp.NoOp", '-nologo')
    }
    tunit = @{
        Name = 'tunit'; Dir = Join-Path $root 'tests/Randactyl.NullObliviousAnalyzer.Tests.TUnit'
        Proj = 'Randactyl.NullObliviousAnalyzer.Tests.TUnit'
        Single = @('--treenode-filter', "/*/*/$singleClass/$singleMethod")
        NoOp   = @('--treenode-filter', '/*/*/FrameworkOverheadNoOp/NoOp')
        ExeSingle = @('--treenode-filter', "/*/*/$singleClass/$singleMethod")
        ExeNoOp   = @('--treenode-filter', '/*/*/FrameworkOverheadNoOp/NoOp')
    }
}
foreach ($f in $fw.Values) { $f.Csproj = Join-Path $f.Dir "$($f.Proj).csproj" }

# ---------- helpers ----------

function Invoke-Timed {
    param([string]$File, [string[]]$Arguments, [switch]$AllowFailure)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = & $File @Arguments 2>&1 | Out-String
    $sw.Stop()
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "Failed ($LASTEXITCODE): $File $($Arguments -join ' ')`n$out"
    }
    [pscustomobject]@{ Ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 1); Output = $out }
}

function ConvertFrom-TestDuration([string]$text) {
    # "duration: 1m 6s 639ms" -> ms. Returns the LAST duration in the text (the run summary).
    $x = [regex]::Match($text, 'Time:\s*([0-9.]+)s\b')   # xunit native runner summary: "Time: 1.746s"
    if ($x.Success -and $text -notmatch 'Test run summary') { return [double]$x.Groups[1].Value * 1000 }
    $m = [regex]::Matches($text, 'duration:\s*([0-9hms ]+?)\s*(?:\r?\n|$)')
    if ($m.Count -eq 0) { return $null }
    $s = $m[$m.Count - 1].Groups[1].Value
    $ms = 0.0
    foreach ($p in [regex]::Matches($s, '(\d+(?:\.\d+)?)(h|ms|m|s)')) {
        $v = [double]$p.Groups[1].Value
        switch ($p.Groups[2].Value) { 'h' { $ms += $v * 3600000 } 'm' { $ms += $v * 60000 } 's' { $ms += $v * 1000 } 'ms' { $ms += $v } }
    }
    $ms
}

function Get-Summary([double[]]$v) {
    if (-not $v -or $v.Count -eq 0) { return $null }
    $s = @($v | Sort-Object)
    $n = $s.Count
    $median = if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 }
    [ordered]@{ n = $n; medianMs = [math]::Round($median, 1); minMs = $s[0]; maxMs = $s[-1]; samplesMs = $v }
}

function Stop-BuildServers { dotnet build-server shutdown 2>&1 | Out-Null }

function Assert-UnderRoot([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to touch outside repo: $full" }
    $full
}

function Remove-BinObj($f) {
    foreach ($d in 'bin', 'obj') {
        $p = Assert-UnderRoot (Join-Path $f.Dir $d)
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
}

function Get-ExePath($f, [string]$config = 'Debug') { Join-Path $f.Dir "bin/$config/net11.0/$($f.Proj).exe" }

# Runs $scriptblock-per-framework $Runs times (after a warm-up), interleaved. $Action gets ($f) and returns
# a pscustomobject with Ms and optionally SelfMs. Returns @{ xunit = summary; tunit = summary }.
function Measure-Interleaved {
    param([scriptblock]$Action, [scriptblock]$Prepare = $null, [string]$Label)
    Write-Host "== $Label"
    $wall = @{}; $self = @{}
    foreach ($k in $fw.Keys) { $wall[$k] = [Collections.Generic.List[double]]::new(); $self[$k] = [Collections.Generic.List[double]]::new() }
    for ($i = 0; $i -le $Runs; $i++) {          # i = 0 is the warm-up
        foreach ($k in $fw.Keys) {
            $f = $fw[$k]
            if ($Prepare) { & $Prepare $f }
            $r = & $Action $f
            if ($i -eq 0) { continue }
            $wall[$k].Add($r.Ms)
            if ($r.PSObject.Properties['SelfMs'] -and $null -ne $r.SelfMs) { $self[$k].Add([double]$r.SelfMs) }
        }
        if ($i -gt 0) { Write-Host ("   run {0}/{1}: xunit {2} ms, tunit {3} ms" -f $i, $Runs, $wall['xunit'][$i - 1], $wall['tunit'][$i - 1]) }
    }
    $res = [ordered]@{}
    foreach ($k in $fw.Keys) {
        $res[$k] = [ordered]@{ wallClock = Get-Summary $wall[$k].ToArray() }
        if ($self[$k].Count -gt 0) { $res[$k].selfReported = Get-Summary $self[$k].ToArray() }
    }
    $res
}

function Invoke-DotnetTest($f, [string[]]$extra = @(), [string]$config = 'Debug') {
    $r = Invoke-Timed 'dotnet' (@('test', '--project', $f.Csproj, '--no-build', '-c', $config) + $extra)
    if ($r.Output -notmatch 'Passed!') { throw "Tests did not pass for $($f.Name):`n$($r.Output)" }
    $r | Add-Member SelfMs (ConvertFrom-TestDuration $r.Output) -PassThru
}

function Invoke-Exe($f, [string[]]$extra = @(), [string]$config = 'Debug') {
    $r = Invoke-Timed (Get-ExePath $f $config) $extra
    $r | Add-Member SelfMs (ConvertFrom-TestDuration $r.Output) -PassThru
}

function Get-FileStats([string[]]$paths) {
    $files = 0; $lines = 0; $bytes = 0
    foreach ($p in $paths) {
        $files++
        $bytes += (Get-Item -LiteralPath $p).Length
        $lines += [IO.File]::ReadAllLines($p).Length
    }
    [ordered]@{ files = $files; lines = $lines; bytes = $bytes }
}

function Get-AssemblyStats([string]$dll) {
    $json = dotnet run (Join-Path $root 'eng/AssemblyStats.cs') -- $dll
    if ($LASTEXITCODE -ne 0) { throw "AssemblyStats failed for $dll" }
    $json | ConvertFrom-Json
}

# ---------- machine and versions ----------

Write-Host '== environment'
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$cs = Get-CimInstance Win32_ComputerSystem
$defender = try { (Get-MpComputerStatus).RealTimeProtectionEnabled } catch { 'unknown' }
$powerPlan = try { (powercfg /getactivescheme) -join ' ' } catch { 'unknown' }
$machine = [ordered]@{
    cpu = $cpu.Name.Trim(); physicalCores = ($cpu.NumberOfCores | Measure-Object -Sum).Sum
    logicalProcessors = $cpu.NumberOfLogicalProcessors; ramGB = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    os = (Get-CimInstance Win32_OperatingSystem).Caption + ' ' + [Environment]::OSVersion.Version
    sdk = (dotnet --version); powerPlan = $powerPlan; defenderRealTimeProtection = $defender
    timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
}

# ---------- 0. prepare: dependencies built, servers down ----------

Stop-BuildServers
dotnet build $testingProj -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Helper project build failed' }

$results = [ordered]@{}

# ---------- 1. clean build (project alone, dependencies already built) ----------

Stop-BuildServers
$results.buildClean = Measure-Interleaved -Label 'clean build of test project alone' `
    -Prepare {
        param($f)
        Remove-BinObj $f
        dotnet restore $f.Csproj -v q --nologo | Out-Null     # restore is not part of the timing
        if ($LASTEXITCODE -ne 0) { throw "restore failed: $($f.Name)" }
    } `
    -Action { param($f) Invoke-Timed 'dotnet' @('build', $f.Csproj, '--no-restore', '--no-dependencies', '-v', 'q', '--nologo') }

# ---------- 2. incremental build after touching one test file ----------

Stop-BuildServers
foreach ($f in $fw.Values) { dotnet build $f.Csproj --no-dependencies -v q --nologo | Out-Null; if ($LASTEXITCODE -ne 0) { throw "build failed: $($f.Name)" } }
$results.buildIncremental = Measure-Interleaved -Label 'incremental build after touching CallResultTests.cs' `
    -Prepare { param($f) (Get-Item (Join-Path $f.Dir 'CallResultTests.cs')).LastWriteTime = Get-Date } `
    -Action { param($f) Invoke-Timed 'dotnet' @('build', $f.Csproj, '--no-restore', '--no-dependencies', '-v', 'q', '--nologo') }

# ---------- 3. generated code (clean build with EmitCompilerGeneratedFiles) ----------

Write-Host '== generated code'
Stop-BuildServers
$generated = [ordered]@{}
foreach ($f in $fw.Values) {
    Remove-BinObj $f
    dotnet restore $f.Csproj -v q --nologo | Out-Null
    dotnet build $f.Csproj --no-restore --no-dependencies -v q --nologo -p:EmitCompilerGeneratedFiles=true | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "generated-files build failed: $($f.Name)" }
    $genRoot = Join-Path $f.Dir 'obj/Debug/net11.0/generated'
    $all = @(if (Test-Path $genRoot) { Get-ChildItem $genRoot -Recurse -File -Filter *.cs })
    $byGen = [ordered]@{}
    foreach ($g in ($all | Group-Object { $_.FullName.Substring($genRoot.Length + 1).Split([IO.Path]::DirectorySeparatorChar)[0..1] -join '/' } | Sort-Object Name)) {
        $byGen[$g.Name] = Get-FileStats @($g.Group | ForEach-Object FullName)
    }
    $msbuildGen = @(Get-ChildItem (Join-Path $f.Dir 'obj/Debug/net11.0') -File -Filter *.cs)  # AssemblyInfo, entry point etc., not Roslyn generators
    $generated[$f.Name] = [ordered]@{
        generatorOutputTotal = Get-FileStats @($all | ForEach-Object FullName)
        byGenerator          = $byGen
        msbuildGeneratedSources = [ordered]@{ detail = Get-FileStats @($msbuildGen | ForEach-Object FullName); names = @($msbuildGen | ForEach-Object Name) }
    }
}
$results.generatedCode = $generated

# ---------- 4. assembly, output folder, hand-written code ----------

Write-Host '== sizes'
$size = [ordered]@{}
function Get-FrameworkFileNames($f, [string]$rootPackage) {
    $assets = Get-Content (Join-Path $f.Dir 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $target = @($assets.targets.PSObject.Properties)[0].Value
    $byId = @{}
    foreach ($p in $target.PSObject.Properties) { $byId[$p.Name.Split('/')[0]] = $p.Value }
    $seen = [Collections.Generic.HashSet[string]]::new(); $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($rootPackage); $names = [Collections.Generic.HashSet[string]]::new()
    while ($queue.Count) {
        $id = $queue.Dequeue()
        if (-not $byId.ContainsKey($id) -or -not $seen.Add($id)) { continue }
        $e = $byId[$id]
        foreach ($section in 'runtime', 'resource', 'runtimeTargets') {
            if ($e.PSObject.Properties[$section]) { foreach ($k in $e.$section.PSObject.Properties.Name) { [void]$names.Add([IO.Path]::GetFileName($k)) } }
        }
        if ($e.PSObject.Properties['dependencies']) { foreach ($d in $e.dependencies.PSObject.Properties.Name) { $queue.Enqueue($d) } }
    }
    $names
}
dotnet build $testingProj -c Release -v q --nologo | Out-Null   # Release dependencies for the Release size build
if ($LASTEXITCODE -ne 0) { throw 'Helper Release build failed' }
foreach ($f in $fw.Values) {
    foreach ($cfg in 'Debug', 'Release') {
        Remove-BinObj $f
        dotnet restore $f.Csproj -v q --nologo | Out-Null
        dotnet build $f.Csproj --no-restore --no-dependencies -c $cfg -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "build failed ($cfg): $($f.Name)" }
        $frameworkFileNames = @{ $f.Name = Get-FrameworkFileNames $f $(if ($f.Name -eq 'xunit') { 'xunit.v3' } else { 'TUnit' }) }
        $out = Join-Path $f.Dir "bin/$cfg/net11.0"
        $dll = Join-Path $out "$($f.Proj).dll"
        $items = @(Get-ChildItem $out -Recurse -File)
        $dlls = @($items | Where-Object Extension -eq '.dll')
        # "Framework-owned" = DLLs in the output whose file name ships in the framework package or in any package
        # reachable from it (transitive closure in project.assets.json). Includes Microsoft.Testing.* and friends.
        $owned = @($dlls | Where-Object { $_.Name -in $frameworkFileNames[$f.Name] })
        $size["$($f.Name)-$cfg"] = [ordered]@{
            testAssembly = Get-AssemblyStats $dll
            outputFolder = [ordered]@{ files = $items.Count; bytes = ($items | Measure-Object Length -Sum).Sum; dlls = $dlls.Count }
            frameworkOwnedAssemblies = [ordered]@{ count = $owned.Count; bytes = ($owned | Measure-Object Length -Sum).Sum; names = @($owned | ForEach-Object Name | Sort-Object) }
        }
    }
}
$results.sizes = $size

$hand = [ordered]@{}
foreach ($f in $fw.Values) {
    $src = @(Get-ChildItem $f.Dir -Filter *.cs | Where-Object Name -ne 'FrameworkOverheadNoOp.cs')
    $hand[$f.Name] = Get-FileStats @($src | ForEach-Object FullName)
}
$differing = 0
$xs = Get-ChildItem $fw.xunit.Dir -Filter *.cs | Where-Object Name -ne 'FrameworkOverheadNoOp.cs'
foreach ($x in $xs) {
    $a = [IO.File]::ReadAllLines($x.FullName); $b = [IO.File]::ReadAllLines((Join-Path $fw.tunit.Dir $x.Name))
    if ($a.Length -ne $b.Length) { $differing += [math]::Abs($a.Length - $b.Length) }
    for ($i = 0; $i -lt [math]::Min($a.Length, $b.Length); $i++) { if ($a[$i] -ne $b[$i]) { $differing++ } }
}
$hand.linesDifferingBetweenProjects = $differing
$results.handWrittenCode = $hand

# ---------- 5. rebuild normally, then run-time measurements ----------

foreach ($f in $fw.Values) {
    Remove-BinObj $f
    dotnet restore $f.Csproj -v q --nologo | Out-Null
    dotnet build $f.Csproj --no-restore --no-dependencies -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "build failed: $($f.Name)" }
}

# package versions actually resolved
$pkgs = [ordered]@{}
foreach ($f in $fw.Values) {
    $assets = Get-Content (Join-Path $f.Dir 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $names = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^(xunit|TUnit|Microsoft\.Testing)' } | Sort-Object
    $pkgs[$f.Name] = @($names)
}

Stop-BuildServers
$results.runSuite_dotnetTest   = Measure-Interleaved -Label 'whole suite, dotnet test --no-build (default parallelism)' -Action { param($f) Invoke-DotnetTest $f }
$results.runSuite_exe          = Measure-Interleaved -Label 'whole suite, test executable directly (default parallelism)' -Action { param($f) Invoke-Exe $f }
$results.runSingle_dotnetTest  = Measure-Interleaved -Label "single test ($singleClass.$singleMethod), dotnet test --no-build" -Action { param($f) Invoke-DotnetTest $f $f.Single }
$results.runSingle_exe         = Measure-Interleaved -Label 'single test, executable directly' -Action { param($f) Invoke-Exe $f $f.ExeSingle }

# Equalised parallelism. xunit under Microsoft.Testing.Platform: --parallel none (one test at a time) or
# --max-threads N; TUnit: --maximum-parallel-tests N. N=1 means fully sequential for both.
$logical = [int]$machine.logicalProcessors
foreach ($cap in 1, 4, $logical) {
    Write-Host "== equalised parallelism: both capped at $cap"
    $c = $cap
    $results["runSuite_parallelism_$cap`_dotnetTest"] = Measure-Interleaved -Label "whole suite, both capped at $cap" -Action {
        param($f)
        if ($f.Name -eq 'tunit') { Invoke-DotnetTest $f @('--maximum-parallel-tests', "$c") }
        elseif ($c -eq 1) { Invoke-DotnetTest $f @('--parallel', 'none') }
        else { Invoke-DotnetTest $f @('--max-threads', "$c") }
    }
}

# ---------- 6. empty-test baseline (NoOp configuration) ----------

Write-Host '== building NoOp configuration'
foreach ($f in $fw.Values) {
    dotnet build $f.Csproj -c NoOp -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "NoOp build failed: $($f.Name)" }
}
Stop-BuildServers
$results.runNoOp_dotnetTest = Measure-Interleaved -Label 'one empty test, dotnet test --no-build' -Action { param($f) Invoke-DotnetTest $f $f.NoOp 'NoOp' }
$results.runNoOp_exe        = Measure-Interleaved -Label 'one empty test, executable directly' -Action { param($f) Invoke-Exe $f $f.ExeNoOp 'NoOp' }

# ---------- 7. leave the tree in its normal built state and write results ----------

Stop-BuildServers
foreach ($f in $fw.Values) { dotnet build $f.Csproj -v q --nologo | Out-Null }

$final = [ordered]@{
    machine  = $machine
    versions = [ordered]@{ packages = $pkgs; runsPerMeasurement = $Runs; configuration = 'Debug (Release only for the sizes section)' }
    results  = $results
}
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$final | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host "Wrote $OutFile"
