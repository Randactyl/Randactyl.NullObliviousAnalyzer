<#
.SYNOPSIS
  Measures how much a developer has to write for the same test scenarios in xUnit v3 and TUnit.

.DESCRIPTION
  Reads the hand-written files in experiments/authoring/XUnit and experiments/authoring/TUnit (never bin, obj or
  TestResults, never the shared experiments/authoring/Subject code) and writes
  docs/experiments/xunit-vs-tunit-authoring.json. It builds and runs nothing; the output depends only on file
  contents, so a rerun on unchanged files gives an identical JSON file.

  Measures, per scenario and in total, for each framework:
    files        files whose primary scenario this is (a file belongs to exactly one scenario; see $fileScenario)
    lines        non-blank lines
    chars        non-whitespace characters on those lines
    ceremony     non-blank lines that exist only because of the framework, by the rules below

  A line of a .csproj that mentions Microsoft.Extensions.DependencyInjection counts toward scenario 7 and a line that
  mentions xRetry toward scenario 10, because those packages exist only for those scenarios. Every other project
  file line counts toward scenario 1. The files count stays with scenario 1.

  Ceremony rules. A non-blank line is ceremony if it matches the first rule that applies, by file type.
  .cs files:
    R1 using    a using directive (using X; / global using X; / using static X;). Not 'using var' or 'using (...)'.
    R2 attr     a line that holds only an attribute list, e.g. [Fact], [InlineData(1, 2)], [assembly: ...].
    R3 base     a class/struct/record declaration line, or a continuation line that starts with ':', whose base list names
                a framework contract type: IClassFixture, ICollectionFixture, IAsyncLifetime, IAsyncInitializer,
                IAsyncDisposable, IDisposable, IParallelLimit, ITestMethodOrderer, FactAttribute, SkipAttribute,
                DependencyInjectionDataSourceAttribute.
    R4 contract the signature line of a member the framework calls by name (InitializeAsync, DisposeAsync, Dispose,
                OrderTestMethods, ShouldSkip, CreateScope, Create, Limit), and a 'where' constraint line naming ITestMethod.
  .csproj and .json files:
    R5 config   every non-blank line except the ones containing TargetFramework or ProjectReference.
  Not counted as ceremony, on both sides: braces, method bodies (even fixture plumbing bodies), wrapper classes that
  only exist to hold hooks, and namespace lines. The counts therefore understate ceremony on both sides.
#>
[CmdletBinding()]
param([string]$OutFile)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutFile) { $OutFile = Join-Path $root 'docs/experiments/xunit-vs-tunit-authoring.json' }
$authoring = Join-Path $root 'experiments/authoring'

$scenarios = [ordered]@{
    '1'  = 'Project setup'
    '2'  = 'Plain sync and async tests'
    '3'  = 'Parameterised tests'
    '4'  = 'Assertions'
    '5'  = 'Lifecycle'
    '6'  = 'Shared expensive fixture'
    '7'  = 'Dependency injection'
    '8'  = 'Parallelism control'
    '9'  = 'Ordering and dependencies'
    '10' = 'Skip, timeout, cancellation, retry, repeat, traits'
    '11' = 'Diagnostic output'
    '12' = 'Custom attribute'
}

$fileScenario = @{
    'Authoring.Tests.csproj'      = '1'
    'global.json'                 = '1'
    'BasicTests.cs'               = '2'
    'ParameterizedTests.cs'       = '3'
    'AssertionTests.cs'           = '4'
    'LifecycleTests.cs'           = '5'
    'SharedFixtureTests.cs'       = '6'
    'DependencyInjectionTests.cs' = '7'
    'ParallelismTests.cs'         = '8'
    'OrderingTests.cs'            = '9'
    'ControlTests.cs'             = '10'
    'OutputTests.cs'              = '11'
    'CustomAttributeTests.cs'     = '12'
}

$frameworkTypes = 'IClassFixture|ICollectionFixture|IAsyncLifetime|IAsyncInitializer|IAsyncDisposable|IDisposable|IParallelLimit|ITestMethodOrderer|FactAttribute|SkipAttribute|DependencyInjectionDataSourceAttribute'
$contractMembers = 'InitializeAsync|DisposeAsync|Dispose|OrderTestMethods|ShouldSkip|CreateScope|Create'

function Get-CeremonyRule([string]$line, [string]$extension) {
    if ($extension -in '.csproj', '.json') {
        if ($line -match 'TargetFramework|ProjectReference') { return $null }
        return 'R5-config'
    }
    if ($line -match '^\s*(global\s+)?using\s+(static\s+)?[A-Za-z_][\w.]*\s*;') { return 'R1-using' }
    if ($line -match '^\s*\[[^\]]*\]\s*$') { return 'R2-attribute' }
    if ($line -match "^\s*(.*\b(class|struct|record)\b.*)?:\s.*\b($frameworkTypes)\b" -and
        ($line -match '\b(class|struct|record)\b' -or $line -match '^\s*:')) { return 'R3-base-type' }
    if ($line -match "^\s*(public|protected|internal)\s+(override\s+)?[\w<>?,.\s\[\]]*\b($contractMembers)\b\s*(<[^>]*>)?\(") { return 'R4-contract-member' }
    if ($line -match '^\s*public\s+int\s+Limit\b') { return 'R4-contract-member' }
    if ($line -match '^\s*where\s+\w+\s*:.*\bITestMethod\b') { return 'R4-contract-member' }
    return $null
}

function Get-Scenario([string]$fileName, [string]$line, [string]$extension) {
    if ($extension -eq '.csproj') {
        if ($line -match 'Microsoft\.Extensions\.DependencyInjection') { return '7' }
        if ($line -match 'xRetry') { return '10' }
    }
    return $fileScenario[$fileName]
}

function Measure-Framework([string]$name) {
    $dir = Join-Path $authoring $name
    $files = @(Get-ChildItem $dir -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|TestResults)[\\/]' -and $_.Extension -in '.cs', '.csproj', '.json'
        })
    $stats = [ordered]@{}
    foreach ($id in $scenarios.Keys) {
        $stats[$id] = [ordered]@{ files = 0; fileNames = @(); lines = 0; chars = 0; ceremonyLines = 0; ceremonyByRule = [ordered]@{} }
    }
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($dir.Length + 1).Replace('\', '/')
        if (-not $fileScenario.ContainsKey($rel)) { throw "Unmapped file in ${name}: $rel (add it to `$fileScenario)" }
        $primary = $fileScenario[$rel]
        $stats[$primary].files++
        $stats[$primary].fileNames += $rel
        foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $id = Get-Scenario $rel $line $f.Extension
            $s = $stats[$id]
            $s.lines++
            $s.chars += ($line -replace '\s', '').Length
            $rule = Get-CeremonyRule $line $f.Extension
            if ($rule) {
                $s.ceremonyLines++
                if (-not $s.ceremonyByRule.Contains($rule)) { $s.ceremonyByRule[$rule] = 0 }
                $s.ceremonyByRule[$rule]++
            }
        }
    }
    foreach ($key in $fileScenario.Keys) {
        if (-not ($files | Where-Object { $_.FullName.Substring($dir.Length + 1).Replace('\', '/') -eq $key })) {
            throw "Expected file missing in ${name}: $key"
        }
    }
    $stats
}

function Get-Total($stats) {
    $t = [ordered]@{ files = 0; lines = 0; chars = 0; ceremonyLines = 0 }
    foreach ($s in $stats.Values) { foreach ($k in @($t.Keys)) { $t[$k] += $s[$k] } }
    $t
}

function Get-Delta([double]$x, [double]$t) {
    [ordered]@{
        xunit    = $x
        tunit    = $t
        tunitMinusXunit = $t - $x
        tunitVsXunitPercent = if ($x -eq 0) { $null } else { [math]::Round(($t - $x) / $x * 100, 1) }
        lessToWrite = if ($t -lt $x) { 'tunit' } elseif ($x -lt $t) { 'xunit' } else { 'tie' }
    }
}

$x = Measure-Framework 'XUnit'
$t = Measure-Framework 'TUnit'

$perScenario = [ordered]@{}
foreach ($id in $scenarios.Keys) {
    $perScenario[$id] = [ordered]@{
        name          = $scenarios[$id]
        xunit         = $x[$id]
        tunit         = $t[$id]
        files         = Get-Delta $x[$id].files $t[$id].files
        lines         = Get-Delta $x[$id].lines $t[$id].lines
        chars         = Get-Delta $x[$id].chars $t[$id].chars
        ceremonyLines = Get-Delta $x[$id].ceremonyLines $t[$id].ceremonyLines
    }
}

$xt = Get-Total $x; $tt = Get-Total $t
$totals = [ordered]@{
    xunit         = $xt
    tunit         = $tt
    files         = Get-Delta $xt.files $tt.files
    lines         = Get-Delta $xt.lines $tt.lines
    chars         = Get-Delta $xt.chars $tt.chars
    ceremonyLines = Get-Delta $xt.ceremonyLines $tt.ceremonyLines
}

$final = [ordered]@{
    description = 'Hand-written test code for the same 12 scenarios in xUnit v3 and TUnit. Rules are in eng/compare-authoring.ps1.'
    scenarios   = $perScenario
    totals      = $totals
}
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$final | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host "Wrote $OutFile"
foreach ($id in $scenarios.Keys) {
    $p = $perScenario[$id]
    '{0,2} {1,-52} lines x={2,3} t={3,3}  chars x={4,5} t={5,5}  ceremony x={6,3} t={7,3}' -f $id, $p.name, $p.lines.xunit, $p.lines.tunit, $p.chars.xunit, $p.chars.tunit, $p.ceremonyLines.xunit, $p.ceremonyLines.tunit
}
'   TOTAL files x={0} t={1}  lines x={2} t={3}  chars x={4} t={5}  ceremony x={6} t={7}' -f $xt.files, $tt.files, $xt.lines, $tt.lines, $xt.chars, $tt.chars, $xt.ceremonyLines, $tt.ceremonyLines
