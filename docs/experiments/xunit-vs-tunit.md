# xUnit v3 vs TUnit on this repository

Date: 2026-11-12. Raw numbers: `docs/experiments/xunit-vs-tunit-results.json` (performance, from the 25-minute run) and `docs/experiments/xunit-vs-tunit-authoring.json` (code you write). Rerun with `pwsh eng/compare-test-frameworks.ps1` (about 25 minutes) and `pwsh eng/compare-authoring.ps1` (a few seconds, measures files only).

## Recommendation

TUnit, narrowly and with low confidence. For the 123 tests in this repository the choice makes no measurable difference, and a switch can be undone as cheaply as it was done (the port was two text substitutions).

- **Performance: tie.** Whole-suite time is the same within noise. About 98% of the time is the shared Roslyn compilation in the helper, not the framework. xUnit is nominally ahead in the small measurements (build 120 to 140 ms faster, direct start 0.29 s vs 0.57 s, 11% faster with parallelism capped at 4 but with overlapping ranges). None of these changes how long a developer waits for this suite.
- **Code you write: TUnit, by a small margin.** Across 12 scenarios TUnit needed 20 fewer lines (367 vs 387, 5%), 214 fewer non-whitespace characters (2.5%) and 9 fewer lines of framework ceremony (89 vs 98, 9%). The number of files is the same (13). Only one scenario differs a lot: ordering and dependencies (16 vs 26 lines), where xUnit has no built-in way to say "run B after A" and needs a custom orderer. xUnit also needed a third-party package for retry. This repository's tests use none of these features.
- **Documentation: TUnit scores higher** (27 vs 20 out of 30 on my ten tasks). xUnit's docs are better dated and versioned and have an API reference, but have gaps for ordering, assertions, migration and custom extension points. Writing the 12 scenarios found gaps in both sets of docs (listed below). Scores are one reviewer's judgment.

The most useful finding is not about the frameworks. Both run this suite faster with parallelism capped at 4 than with their defaults (about 5.0 s to 5.6 s vs 7.6 s), because the tests are CPU-bound Roslyn work. That tuning applies to either framework (see "Parallelism").

### How confident, and what would tip it

Confidence is low. TUnit wins two of three questions, but the margin on "code you write" is small, "documentation" is a single reviewer's scoring, and "performance" is a tie in which xUnit is nominally ahead. Counting the verdicts gives TUnit, but the evidence does not separate them strongly.

This would tip it to xUnit:

- You value things this comparison did not measure: age and size of the ecosystem, IDE test-runner behaviour (Visual Studio, Rider, VS Code), versioned documentation and an API reference. I measured none of them.
- Measuring TUnit in your IDE shows slower discovery or runs than xUnit.
- A rescoring of the documentation by a second reviewer closes the 27 to 20 gap.
- You do not expect to need ordering, dependencies, retry or lifecycle hooks beyond plain facts. Then the code-you-write edge is nil for you.

This would make it firmer for TUnit: you plan to use dependencies between tests, retry, class or assembly hooks, or a custom skip condition. The scenarios for those are where TUnit wrote less or needed no extra package.

## Machine and versions

| Item | Value |
|---|---|
| CPU | 12th Gen Intel Core i5-12600K, 10 cores, 16 logical processors |
| RAM | 31.7 GB |
| OS | Windows 11 Pro 10.0.26300, High performance power plan |
| Defender real-time protection | on (not excluded) |
| .NET SDK | 11.0.100, target net11.0, Debug configuration |
| xUnit | `xunit.v3` 4.0.3 (runner packages `xunit.v3.mtp-v2` 4.0.3), Microsoft.Testing.Platform 2.5.2 |
| TUnit | `TUnit` 1.84.0 (latest stable on nuget.org on 2026-11-12), Microsoft.Testing.Platform 2.5.3, Microsoft.Testing.Extensions.CodeCoverage 18.12.0 |

Microsoft.Testing.Platform (MTP) is Microsoft's newer test host. `global.json` selects it for `dotnet test`. VSTest is the older host.

## Performance

The timings were taken when each suite had 105 tests. Both suites now have 123 (18 more analyzer tests were added afterwards). I did not rerun the timings. The added tests are the same kind (a Roslyn compilation per test), so I expect the same ranking, but that is unverified.

Each row is one warm-up run followed by 10 timed runs, with xUnit and TUnit runs interleaved. Values are median (min to max) in milliseconds. "Self" is the duration the framework prints for itself.

### Build

Build of the test project alone, with the analyzer and helper projects already built. Restore is not timed. Clean means that project's `bin` and `obj` were deleted first.

| Measurement | xUnit | TUnit |
|---|---|---|
| Clean build | 640 (593 to 857) | 779 (609 to 891) |
| Incremental build after touching `CallResultTests.cs` | 622 (578 to 760) | 739 (653 to 837) |

TUnit is slower by about 140 ms (clean) and 117 ms (incremental). The ranges overlap, but the medians differ in the same direction in both tests. This is real but small. Both builds are under a second, so a compiler server (the long-lived Roslyn process that `dotnet build` reuses) was warm after the warm-up run. Cold-server builds will be slower for both.

### Running tests

| Measurement | xUnit wall | xUnit self | TUnit wall | TUnit self |
|---|---|---|---|---|
| Whole suite, `dotnet test --no-build`, default parallelism | 7,640 (4,912 to 9,484) | 7,051 | 7,639 (5,749 to 9,682) | 7,057 |
| Whole suite, test executable run directly, default parallelism | 6,379 (4,410 to 10,044) | 6,161 | 7,056 (5,027 to 11,118) | 6,557 |
| One test by filter, `dotnet test --no-build` | 2,894 (2,832 to 2,967) | 2,304 | 2,807 (2,717 to 2,868) | 2,214 |
| One test by filter, executable directly | 1,923 (1,845 to 1,964) | 1,717 | 2,164 (2,136 to 2,227) | 1,678 |
| One empty test, `dotnet test --no-build` | 1,276 (1,247 to 1,331) | 697 | 1,165 (1,133 to 1,257) | 580 |
| One empty test, executable directly | 295 (280 to 319) | 109 | 568 (547 to 629) | 99 |

Reading the table:

- **Whole suite at default settings: tie.** The medians are identical (7,640 vs 7,639 ms). Run-to-run spread (about 4.9 s to 9.7 s) is much larger than any difference between frameworks.
- **Single test: tie within about 90 ms.** TUnit is slightly faster through `dotnet test`, xUnit is faster when launched directly (by 240 ms).
- **Startup floor (empty test).** Through `dotnet test` the cost is about 1.2 s for both, mostly the CLI and the test host starting. Run directly, xUnit finishes in 0.30 s and TUnit in 0.57 s. TUnit's own reported time is lower (99 ms vs 109 ms), so its extra 270 ms is process startup and extension loading outside the timed run. For one test this is noticeable; for this suite it is 4% of the total.
- **Share of time.** Run fully sequentially (below), the suite takes about 7.5 s and the empty-test self-report is about 0.1 s. At least 98% of the suite time is the shared workload (Roslyn compiling and analyzing the test sources, plus first-call JIT cost). The single real test takes 2.8 s against 1.2 s for an empty one, so the first Roslyn call alone costs about 1.6 s. Neither framework can improve on that much.
- **Executable vs `dotnet test`.** Running `Tests.XUnit.exe` directly uses xUnit's own console runner, not MTP, with different switches (`-method` instead of `--filter-method`). TUnit's executable always uses MTP. The "executable directly" rows therefore compare different runners for xUnit. The `dotnet test` rows compare MTP to MTP.

### Parallelism

Defaults:

- **xUnit v3:** tests inside a class run one at a time, different classes run in parallel, using up to one thread per logical processor (16 here). This comes from the xUnit docs (`running-tests-in-parallel`). The project had 14 test classes when timed, so at most 14 tests ran at once.
- **TUnit:** the TUnit docs say at most 2 x the processor count of tests run at once (32 here). I did not observe the actual peak, so treat this as the documented value.

Because the defaults differ, I reran with both capped to the same number. xUnit options: `--parallel none` and `--max-threads N`. TUnit option: `--maximum-parallel-tests N`.

| Parallelism cap | xUnit wall | TUnit wall |
|---|---|---|
| Default (xUnit 16 threads, TUnit documented 32) | 7,640 (4,912 to 9,484) | 7,639 (5,749 to 9,682) |
| 1 (fully sequential) | 7,476 (7,430 to 7,963) | 7,464 (7,096 to 7,855) |
| 4 | 5,030 (4,863 to 5,164) | 5,646 (4,608 to 6,698) |
| 16 | 5,953 (5,164 to 8,082) | 6,645 (5,650 to 8,598) |

Findings:

- **Sequential is a tie** (7,476 vs 7,464 ms, both very stable).
- **At a cap of 4, xUnit is about 600 ms (11%) faster at the median and much steadier.** TUnit's range (4.6 to 6.7 s) overlaps xUnit's, so this is a small edge, not a clear win. At 16 the gap is similar (about 700 ms) with heavily overlapping ranges.
- **More parallelism is not faster here.** Four threads beat both 16 and the defaults for both frameworks, and beat sequential by about 30%. The Roslyn work is CPU-heavy and shares caches, so extra threads contend. If suite time matters, set `--max-threads 4` (xUnit) or `--maximum-parallel-tests 4` (TUnit). I did not test values between 1 and 16 other than 4.

## Code you write

This section measures what a developer writes by hand: files, lines, characters and framework ceremony. It does not look at anything a framework generates.

### Why a new test was needed

The two real test projects are the same size. They differ in 137 lines of the same files: 123 attribute lines (`[Fact]` vs `[Test]`) and 14 `using` lines. That is true but uninformative: every test there is a plain fact that calls one helper, so none of the places where the frameworks ask for different amounts of code are exercised. I therefore wrote the same 12 scenarios once per framework, using what each framework's documentation recommends.

### What was built

Two small projects, `experiments/authoring/XUnit/` and `experiments/authoring/TUnit/` (net11.0, same package versions as the main test projects, not in the solution file), test the same framework-free code in `experiments/authoring/Subject/` (a calculator, a greeting service, an expensive resource, a concurrency probe). Each has 35 tests; both build with 0 warnings under `TreatWarningsAsErrors` and pass (34 passed, 1 skipped on purpose, per project). One file per scenario, same file name on both sides. `experiments/authoring/Directory.Packages.props` (shared) adds the versions of `Microsoft.Extensions.DependencyInjection` 11.0.0 and `xRetry.v3` 1.0.0 on top of the repository's.

### Method

`eng/compare-authoring.ps1` reads the hand-written files (never `bin`, `obj`, or `Subject`) and writes `xunit-vs-tunit-authoring.json`. For each scenario and framework it counts:

- **Files** a developer authors (a file belongs to one scenario).
- **Lines**, excluding blank lines.
- **Characters**, excluding whitespace.
- **Ceremony lines**: non-blank lines that exist only because of the framework, by these rules, applied identically to both:
  - R1: a `using` directive.
  - R2: a line that holds only an attribute list (`[Fact]`, `[InlineData(1, 2)]`, `[assembly: ...]`).
  - R3: a class declaration whose base list names a framework contract type (`IClassFixture`, `ICollectionFixture`, `IAsyncLifetime`, `IAsyncInitializer`, `IAsyncDisposable`, `IDisposable`, `IParallelLimit`, `ITestMethodOrderer`, `FactAttribute`, `SkipAttribute`, `DependencyInjectionDataSourceAttribute`).
  - R4: the signature line of a member the framework calls by name (`InitializeAsync`, `DisposeAsync`, `Dispose`, `OrderTestMethods`, `ShouldSkip`, `CreateScope`, `Create`, `Limit`) and a `where` constraint naming `ITestMethod`.
  - R5: every line of a `.csproj` or `.json` file except `TargetFramework` and `ProjectReference` lines.
  - Not counted: braces, method bodies, wrapper classes that only hold hooks, namespace lines. The ceremony numbers understate both sides.

A project-file line that mentions the DI package counts toward scenario 7, and one that mentions xRetry toward scenario 10. All other project-file lines are scenario 1.

### Totals

| Measure | xUnit | TUnit | TUnit minus xUnit |
|---|---|---|---|
| Files | 13 | 13 | 0 |
| Lines | 387 | 367 | -20 (-5.2%) |
| Non-whitespace characters | 8,582 | 8,368 | -214 (-2.5%) |
| Ceremony lines | 98 | 89 | -9 (-9.2%) |

All differences are small. 20 lines over 12 scenarios is under two lines per scenario. The character gap is smaller than the line gap because TUnit's `await Assert.That(...).IsEqualTo(...)` is longer per line than `Assert.Equal(...)`. The ceremony gap depends on the rules above; a different rule set would give a different number, but I do not see one that reverses the sign.

### Per scenario

Each scenario has one file per framework, except scenario 1 (project file and `global.json`). Format: xUnit / TUnit.

| # | Scenario | Lines | Characters | Ceremony lines |
|---|---|---|---|---|
| 1 | Project setup | 19 / 16 (-16%) | 400 / 351 (-12%) | 17 / 14 (-18%) |
| 2 | Plain sync and async test | 17 / 17 | 368 / 365 | 3 / 3 |
| 3 | Parameterised tests | 34 / 34 | 796 / 863 (+8%) | 8 / 8 |
| 4 | Assertions | 57 / 60 (+5%) | 1,339 / 1,471 (+10%) | 9 / 9 |
| 5 | Lifecycle | 58 / 54 (-7%) | 1,235 / 1,062 (-14%) | 13 / 8 (-38%) |
| 6 | Shared expensive fixture | 38 / 35 (-8%) | 826 / 920 (+11%) | 10 / 9 |
| 7 | Dependency injection | 29 / 28 (-3%) | 722 / 830 (+15%) | 7 / 8 |
| 8 | Parallelism control | 26 / 30 (+15%) | 631 / 684 (+8%) | 7 / 9 |
| 9 | Ordering and dependencies | 26 / 16 (-38%) | 532 / 250 (-53%) | 8 / 3 (-63%) |
| 10 | Skip, timeout, cancellation, retry, repeat, traits | 48 / 48 | 1,070 / 974 (-9%) | 11 / 12 |
| 11 | Diagnostic output | 12 / 12 | 222 / 238 | 2 / 2 |
| 12 | Custom attribute | 23 / 17 (-26%) | 441 / 360 (-18%) | 3 / 4 |
| | **Total** | **387 / 367 (-5%)** | **8,582 / 8,368 (-2.5%)** | **98 / 89 (-9%)** |

One line per scenario: which needs less, and does it matter in practice?

1. **Setup: TUnit, 3 lines less.** xUnit's template adds `<Using Include="Xunit" />`; TUnit's package brings its global usings. Does not matter. Both need a `global.json` entry for `dotnet test` on MTP.
2. **Plain tests: tie.** xUnit's analyzer (xUnit1051) asks async calls to receive `TestContext.Current.CancellationToken`, so I passed it; TUnit does not ask. One argument. (I did not test what happens without it.) Does not matter.
3. **Parameterised: tie on lines, TUnit 8% more characters.** Both have inline, method and display-name-per-case forms. Does not matter.
4. **Assertions: xUnit, 3 lines and 10% fewer characters.** TUnit needs `await` on every assertion and a `using (Assert.Multiple())` block. Does not matter; this is style.
5. **Lifecycle: TUnit, 4 lines less and 5 fewer ceremony lines.** xUnit needs fixture classes for class- and assembly-level setup; TUnit uses hook attributes (but TUnit requires assembly hooks to live in a class of their own, so it needs a wrapper there too). Matters a little if you use class or assembly setup a lot.
6. **Shared fixture: TUnit, 3 lines less.** xUnit needs a collection-definition class. (xUnit v3's assembly fixture would save about as much, at a different scope.) Does not matter.
7. **Dependency injection: tie (29 vs 28).** Neither is built in. TUnit has a documented data-source attribute for containers; xUnit has no DI idiom, so I wrote a class fixture that owns a container. Both need `Microsoft.Extensions.DependencyInjection`. TUnit is the cleaner fit, but the line count says tie.
8. **Parallelism: xUnit, 4 lines less.** Limiting overall threads is one attribute in xUnit; TUnit needs a limiter class plus the attribute. Does not matter.
9. **Ordering and dependencies: TUnit, 10 lines (38%) less.** TUnit has `[DependsOn]`. xUnit has no concept of dependency; I wrote a method orderer and the tests run in name order, so a failing step 1 does not stop step 2. **This one matters** if you need it. This repository does not.
10. **Skip, timeout, token, retry, repeat, traits: tie on lines, but xUnit needed a third-party package** (`xRetry.v3`) for retry and has no repeat (I used a theory with three data rows). TUnit has all of it built in. Matters if you want retry.
11. **Output: tie.** Does not matter.
12. **Custom attribute: TUnit, 6 lines less.** xUnit v3's `FactAttribute` base class takes caller-file and line arguments that a subclass has to pass on; TUnit's `SkipAttribute` override is shorter. Small.

### Where a workaround or third-party package was needed

- **Retry (xUnit):** `xRetry.v3` 1.0.0, a third-party package. No built-in retry.
- **Repeat (xUnit):** no built-in; a theory with three data rows runs the test three times. Closest idiom.
- **Ordering and dependencies (xUnit):** no dependency feature; custom `ITestMethodOrderer`. Test order is by method name.
- **Dependency injection (xUnit):** no support; a class fixture holding a `ServiceProvider`. Closest idiom. Both frameworks need `Microsoft.Extensions.DependencyInjection` for a container.
- **Dependency injection (TUnit):** built in, but the container package is still needed.
- **Assembly hooks (TUnit):** the TUnit analyzer (error `TUnit0042`) refuses hooks mixed with test methods for assembly level, so they sit in a separate static class.

### Documentation pages followed, per scenario

| # | xUnit | TUnit |
|---|---|---|
| 1 | `xunit.net/docs/getting-started/v3/getting-started`, `.../v3/microsoft-testing-platform` | `tunit.dev/docs/getting-started/installation` |
| 2 | same getting-started page; `.../v3/whats-new` (cancellation token) | installation page |
| 3 | `.../v3/theory-data-attributes` | `tunit.dev/docs/writing-tests/arguments`, `.../method-data-source` |
| 4 | no prose assertion page; API compiled against `xunit.v3` 4.0.3 | `tunit.dev/docs/assertions/getting-started` |
| 5 | `xunit.net/docs/shared-context` | `tunit.dev/docs/writing-tests/hooks` |
| 6 | `xunit.net/docs/shared-context` (collection fixture) | `tunit.dev/docs/writing-tests/class-data-source` |
| 7 | none (xUnit docs have no DI page); `shared-context` for the fixture | `tunit.dev/docs/writing-tests/dependency-injection` |
| 8 | `xunit.net/docs/running-tests-in-parallel` | `tunit.dev/docs/execution/parallelism` |
| 9 | `learn.microsoft.com/dotnet/core/testing/order-unit-tests` (shows the v2 API; see below) | same Learn page (TUnit tab), `tunit.dev/docs/writing-tests/ordering` |
| 10 | `.../v3/whats-new` (skip, timeout), `.../v3/microsoft-testing-platform` (`--filter-trait`); `xRetry.v3` not read | `tunit.dev/docs/execution/timeouts`, `.../retrying`, `.../repeating`, `.../test-filters`, `.../writing-tests/skip` |
| 11 | `xunit.net/docs/capturing-output` | `tunit.dev/docs/writing-tests/test-context` |
| 12 | `.../v3/migration-extensibility` (does not cover a skip condition; the pattern is from the API) | `tunit.dev/docs/writing-tests/skip`, `.../extending/extension-points` |

### Documentation gaps found while writing the scenarios

I did not change the documentation scores below because of these; they are extra evidence.

- **xUnit:** the Microsoft Learn ordering page shows the xUnit v2 API (`Xunit.Abstractions`, `ITestCaseOrderer` on a collection of cases). On `xunit.v3` 4.0.3 an `ITestCaseOrderer` is called once per test method and so does not order methods; the working form is `ITestMethodOrderer` with `[TestMethodOrderer]`. I found this by running it. The running-tests-in-parallel page names `[assembly: Parallelization(MaxThreads = n)]` but not its namespace (`Xunit.v3`); the older `CollectionBehavior(MaxParallelThreads = ...)` is an error in 4.0.3.
- **TUnit:** the docs omit `using` directives. `IParallelLimit` and `IAsyncInitializer` need `using TUnit.Core.Interfaces;`. Two pages I tried (`writing-tests/categories`, `writing-tests/test-data-row-metadata`) returned 404. The per-case display name for method data (`TestDataRow<T>` with `DisplayName`) I found by compiling, not from a page. The `[Category]` attribute is not shown on the filter page.

## Documentation

Method: I tried to answer the same ten tasks from each framework's official docs (xunit.net and tunit.dev), as a newcomer would. Scores: 0 = no answer found, 1 = partial or misleading, 2 = answer found with gaps, 3 = complete and correct. Tasks 1, 2 and 7 I tried for real in the projects here (details below).

| # | Task | xUnit page and score | TUnit page and score |
|---|---|---|---|
| 1 | Install and run a first test with `dotnet test` on MTP | `xunit.net/docs/getting-started/v3/getting-started`, plus `.../v3/microsoft-testing-platform` for `global.json`. **2.** Works, but the sample uses `4.0.0-pre.108` and SDK 10.0.102 (current is 4.0.3), and the getting-started page does not show `global.json` contents. | `tunit.dev/docs/getting-started/installation`, `.../running-your-tests`. **2.** Install is clear. `dotnet test` is not on the install page. The run page says flags need `--` (not needed on SDK 11 with `global.json`) and never mentions `global.json`. |
| 2 | Parameterised tests: inline data and from a method | `.../v3/theory-data-attributes`. **2.** `InlineData` clear. `MemberData` shown only with a property, no method example. I tried a method returning `TheoryDataRow` and it works. | `tunit.dev/docs/writing-tests/arguments`, `.../method-data-source`. **3.** Both covered, with return-type advice. I tried both and they work. |
| 3 | Async tests, cancellation, timeouts | `.../v3/whats-new`, analyzer rule pages `xUnit1051`, `xUnit1069`. **2.** The answer is correct but spread over a what's-new page and analyzer rules. I found no single task page. | `tunit.dev/docs/execution/timeouts`, `.../cancellation`. **3.** `[Timeout]`, token parameter, precedence, retries. |
| 4 | Setup and teardown per test, class, assembly | `xunit.net/docs/shared-context`. **3.** Constructor/`IDisposable`, `IClassFixture`, collections, `AssemblyFixture` (v3). | `tunit.dev/docs/writing-tests/hooks`, `.../lifecycle`. **3.** `[Before]`/`[After]` for Test, Class, Assembly, TestSession; static rules stated. |
| 5 | Control parallelism and ordering | `xunit.net/docs/running-tests-in-parallel`. **2.** Parallelism excellent, with per-version notes. I found no prose page on ordering; only API reference and release notes (v4.0.0 changes orderers). | `tunit.dev/docs/execution/parallelism`, `.../writing-tests/ordering`. **3.** `NotInParallel`, `ParallelLimiter`, `--maximum-parallel-tests`, `DependsOn`. The documented default (2x processors) was not verified. |
| 6 | Dependency injection or shared expensive fixture | `xunit.net/docs/shared-context`. **2.** Fixtures covered. DI is not mentioned; no guidance for containers. | `tunit.dev/docs/writing-tests/dependency-injection`, `.../property-injection`. **3.** `IClassConstructor`, DI data source attribute, `ClassDataSource` with `SharedType`. The DI page does not link the sharing page clearly. |
| 7 | Filter tests from the command line | `.../v3/microsoft-testing-platform`, `xunit.net/docs/query-filter-language`. **3.** I ran `--filter-class`, `--filter-method` and `--filter-query "/*/*/TempDocCheck/Inline"`; all worked. The page also says options need `--` on SDK 8/9 only. | `tunit.dev/docs/execution/test-filters`. **3.** I ran `--treenode-filter "/*/*/CallResultTests/Method_from_oblivious_assembly"`; it worked. The page says `--filter` is unsupported; I confirmed `Unknown option '--filter'`. |
| 8 | Migrate from another framework | `.../v3/migration`. **1.** Covers xUnit v2 to v3 only. Nothing for NUnit or MSTest. | `tunit.dev/docs/migration/xunit`, `/nunit`, `/mstest`. **2.** Mapping tables and a code fixer (`TUXU0001`). The xUnit page omits skip, timeouts and several assertions. |
| 9 | Assertion library and extending it | `xunit.net/docs/nuget-packages-v3`. **2.** The `xunit.v3.assert.source` package lets you add methods to the partial `Assert` class. I found no prose guide to the assertion methods; the list lives in the API reference. | `tunit.dev/docs/assertions/getting-started`, `.../extensibility/custom-assertions`. **3.** Fluent `await Assert.That(...)`, plus an `Assertion<T>` example for custom assertions. |
| 10 | Custom attribute or extension point | `.../v3/migration-extensibility`. **1.** Covers custom `IFactAttribute` and test case classes. Says other parts are "still forthcoming". Nothing on before/after attributes or data attributes. | `tunit.dev/docs/extending/extension-points`. **2.** Event receiver interfaces with one working example. The `Order` property and several interfaces are not explained. |
| | **Total (of 30)** | **20** | **27** |

Tried for real:

- **Task 1:** both projects build and pass with `dotnet test --project <path>` on SDK 11.0.100 and `global.json` selecting MTP.
- **Task 2:** I added a temporary class to each project with two inline-data cases and a method-sourced theory (`MemberData` with a method returning `TheoryDataRow<int,int>` for xUnit; `Arguments` and `MethodDataSource` returning tuples for TUnit). All four tests per project passed. I then deleted both files.
- **Task 7:** as in the table, plus xUnit also accepts the VSTest-style `--filter "FullyQualifiedName~..."`, which TUnit rejects. In the authoring projects I also ran `--filter-trait "Category=Fast"` (xUnit) and `--treenode-filter "/*/*/*/*[Category=Fast]"` (TUnit); each selected the one test.

Site features:

| Feature | xUnit (xunit.net) | TUnit (tunit.dev) |
|---|---|---|
| Search | Not confirmed. The fetched page text showed no search box, but the fetch tool may not render scripts. | A Search control in the header (Docusaurus). |
| Version clarity | Good. Doc titles carry dates (for example "[2026 May 2]") and v3 pages sit under `/v3/`. Weakness: sample versions on the getting-started page are stale (4.0.0-pre.108). Some pages mix v2 and v3 notes in one page. | Weak. One unversioned doc set. No version selector and no "since version" notes (for example in the parallelism page). Changes appear only as inline caveats. |
| API reference | Yes: `api.xunit.net/v3/<version>/` has versioned class pages. | None found. No link to an API reference on the pages fetched. |

## Port friction

All of this is evidence about adoption cost. There was very little.

- **The port was two substitutions:** `using Xunit;` to `using TUnit.Core;` and `[Fact]` to `[Test]`. Test bodies returning `Task` needed no change. All tests passed on the first run (105 at the time; 123 now). No analyzer warnings, even with `TreatWarningsAsErrors`.
- **Project file:** `<PackageReference Include="TUnit" />` replaces `xunit.v3`. I kept `OutputType` `Exe` in both for symmetry; I did not test whether TUnit needs it.
- **Extra files:** every TUnit run writes an HTML report into `TestResults\` at the repo root (already git-ignored). xUnit wrote none.
- **Different option names per framework:** filtering (`--filter-method` vs `--treenode-filter`) and parallelism (`--max-threads`/`--parallel` vs `--maximum-parallel-tests`). My first guess for xUnit, `--parallel-mode`, was rejected; the right name is on the MTP docs page.
- **xUnit executable run directly uses a different runner** than `dotnet test` (see Performance).
- **TUnit rejects VSTest `--filter`** with an "Unknown option" error. The error message is clear, and the docs warn about it.
- **Docs mismatch:** the TUnit run page says `dotnet test` needs `--` before flags. On SDK 11 with `global.json` it does not.

## Files created or changed

- Created: `tests/Randactyl.NullObliviousAnalyzer.Tests.TUnit/` (15 `.cs` files, the `.csproj`, and `FrameworkOverheadNoOp.cs`), `tests/Randactyl.NullObliviousAnalyzer.Tests.XUnit/FrameworkOverheadNoOp.cs`, `eng/compare-test-frameworks.ps1`, `eng/compare-authoring.ps1`, `experiments/authoring/` (`Subject/`, `XUnit/`, `TUnit/`, `Directory.Packages.props`), `docs/experiments/xunit-vs-tunit.md`, `docs/experiments/xunit-vs-tunit-results.json`, `docs/experiments/xunit-vs-tunit-authoring.json`.
- Changed: `Directory.Packages.props` (added `TUnit` 1.84.0), `Randactyl.NullObliviousAnalyzer.slnx` (added the TUnit project), and the xUnit `.csproj` (one `ItemGroup` that removes the no-op file from compilation unless `-c NoOp`). The no-op files are measurement scaffolding and are not part of the 123 tests.
- Not changed: the helper project and `src/`.

## Method and caveats

- **Order and warm-up.** Each measurement had one untimed warm-up and 10 timed runs, interleaved (xUnit, TUnit, xUnit, ...). `dotnet build-server shutdown` was run before each phase, so the warm-up run restarted the compiler server.
- **Clean builds** delete only that project's `bin` and `obj`, run `dotnet restore` untimed, then time `dotnet build --no-restore --no-dependencies`. Dependencies were built first.
- **The empty-test baseline** uses a `NoOp` build configuration that compiles one empty test class (`FrameworkOverheadNoOp`) into each project. In the normal configuration the suite has only the real tests.
- **Test counts.** Timings are from when the suites had 105 tests; they have 123 now and were not retimed.
- **Noise.** The default-parallelism whole-suite runs varied by almost 2x between runs (4.9 to 9.7 s). Differences under about 1 s there are not meaningful. The sequential runs were stable and are the cleanest comparison. Windows Defender real-time protection was on; excluding the repo could change absolute numbers for both.
- **Debug configuration** was used for timings. Release timings may differ, especially for TUnit.
- **TUnit's engine mode.** I used the default engine mode. I did not test its reflection mode.
- **Earlier fields not used.** `xunit-vs-tunit-results.json` still holds fields (`generatedCode`, `sizes`) from an earlier reading of the question, as code the framework generates and deploys. They are not used in this report, the script no longer produces them, and they were left untouched in the file. A rerun of the script writes a file without them.
- **Not measured:** memory use, CPU time, performance of large suites (thousands of tests), IDE discovery and run speed (Visual Studio, Rider, VS Code), AOT, and the time to write the code (only its size).
- **Authoring comparison limits.** One author wrote both sides, so style choices are mine, though I held formatting rules equal (file-scoped namespaces, braces on their own lines, one blank line between members). The 12 scenarios are my choice and cover features where the frameworks differ; a real suite weights them differently, and the repository's own tests use almost none of them. Line and character counts depend on formatting. Ceremony counts depend on the rules above and treat data attributes like `[InlineData(...)]` as ceremony on both sides. Behaviour was checked by running each project: the sequential-group check fails when I remove the grouping attribute (verified in both), xUnit teardown ran after each test and at class and assembly level (verified); in TUnit the three teardown levels each ran at least once (verified), but my probe could not show every per-test run because it wrote to one file concurrently.
- **Both frameworks' behaviour with this repository's 123 tests** was checked only as pass or fail. Features such as theories, fixtures, hooks and retries were exercised in the authoring projects, not in the analyzer tests.
- **Documentation scoring is one reviewer's judgment, from one pass.** Pages were fetched through a tool that summarizes page text with a small model, not read raw. Some xUnit answers (async and cancellation, assertions, ordering) came from search results and the what's-new page, not from dedicated pages, and I did not open every linked page. In the authoring work, the `xRetry.v3` documentation and the xUnit assertion methods (including `Assert.Multiple`) were not read; I used the API and confirmed behaviour by running it. The xUnit site search box and any version selector could not be confirmed. Unverified: TUnit's documented default parallelism (2x processors) and TUnit's claim that the xUnit code fixer handles 80 to 90% of a suite. Treat scores as an indication, not a measurement.
