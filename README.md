# Randactyl.NullObliviousAnalyzer

Roslyn analyzers and code fixes that report every place where a value from code without nullable annotations enters your nullable-enabled code, until your code handles it.

C# nullable reference types only protect you when the compiler knows whether a value can be null. Code that was compiled without nullable annotations (an old NuGet package, a `#nullable disable` region) gives the compiler no information. The compiler calls such a value *null-oblivious* and stays silent about it. This analyzer reports each place such a value comes in. A report goes away once the code handles null right there, or hands the value to a declaration the compiler checks.

Here is the case that motivated the project. `System.DirectoryServices.AccountManagement` ships without nullable annotations, and `FindByIdentity` returns null when nothing matches:

```csharp
#nullable enable

GroupPrincipal group = GroupPrincipal.FindByIdentity(context, groupName);
UserPrincipal user = UserPrincipal.FindByIdentity(context, userName);

group.Members.Add(user);   // NullReferenceException if either lookup found nothing
```

The compiler reports nothing for this code. With the analyzer installed, the build reports both lookups (NOA001) and the read of `Members` (NOA002). Handling null where each value arrives clears the reports, and the compiler's own nullable warnings take over from there:

```csharp
if (GroupPrincipal.FindByIdentity(context, groupName) is not { } group)
    return;
var user = UserPrincipal.FindByIdentity(context, userName)
    ?? throw new InvalidOperationException($"No user named '{userName}'.");

group.Members!.Add(user);   // reviewed: Members is never null for a group that was found
```

Each report has code fixes that write these forms for you (see [Resolving a report](#resolving-a-report)).

Declaring the variable nullable is **not** enough: `GroupPrincipal? group = GroupPrincipal.FindByIdentity(...)` is still reported. The compiler treats an oblivious value as not null, and a nullable variable takes over that state, so `group.Members` would still compile without a warning.

## Is this for you?

Use it if your project has `<Nullable>enable</Nullable>` and calls libraries that were never annotated, and you want to be sure that every place where null can get in unnoticed is found and dealt with. The goal is zero reports: each one stays until the code handles null where the value arrives, or until you suppress it after review.

Terms used below:

- **Null-oblivious** (or just *oblivious*): a reference type that comes from code compiled without nullable annotations. In Roslyn it is a type with `NullableAnnotation.None`. It is neither "not null" (`string`) nor "maybe null" (`string?`).
- **Nullable-enabled code**: code where nullable annotations are on. The analyzer reports only there. A use site inside `#nullable disable` is never reported.
- **Handled**: the code that receives the value deals with null, or passes the value on to a declaration the compiler treats as maybe-null. See [Resolving a report](#resolving-a-report).

## What the sample build prints

`samples/DirectoryServicesSample` uses the analyzer (and its code fixes) through project references. Its `AddUser` method handles every value where it arrives and gets no reports. `PrintMembers` handles nothing yet. `dotnet build samples/DirectoryServicesSample --no-incremental` prints these warnings (paths shortened):

```
Program.cs(44,21): warning NOA001: 'GroupPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'GroupPrincipal'
Program.cs(46,28): warning NOA005: 'member' receives an element of the null-oblivious type 'Principal'
Program.cs(46,38): warning NOA002: Reading 'GroupPrincipal.Members' yields the null-oblivious type 'PrincipalCollection'
Program.cs(50,38): warning NOA006: Parameter 'member' is bound to a delegate parameter of the null-oblivious type 'Principal'
Program.cs(50,66): warning NOA002: Reading 'Principal.DistinguishedName' yields the null-oblivious type 'string'
Program.cs(53,35): warning NOA002: Reading 'GroupPrincipal.Members' yields the null-oblivious type 'PrincipalCollection'
Program.cs(53,35): warning NOA009: 'group.Members.ToList()' has null-oblivious elements or type arguments of type 'Principal', and 'List<Principal>' declares them non-null
```

One line of code can carry several reports. Line 46 is `foreach (Principal member in group.Members)`: NOA002 for reading `Members`, and NOA005 for the `Principal` that each loop step puts into `member`. `Console.WriteLine(member.Name)` on the next line is not reported, because `Console.WriteLine` takes a `string?`, and the compiler checks what happens to the value inside it. MSBuild prints every warning twice, once as it happens and once in its summary; the list above is the unique set.

Incremental builds do not run the compiler again when nothing changed, so they print no warnings. Use `--no-incremental` (or `dotnet build -t:Rebuild`) to see them again.

## Install

Add the package to the project you want to analyze:

```
dotnet add package Randactyl.NullObliviousAnalyzer
```

The package contains the analyzer DLL and the code fix DLL under `analyzers/dotnet/cs`, and this README. It has no `lib` folder and no dependencies, and it is marked as a development dependency, so it does not flow to projects that reference your project. The code fixes run in the IDE and with `dotnet format analyzers`; the build only needs the analyzer.

Release notes for every version are on the [GitHub releases page](https://github.com/Randactyl/Randactyl.NullObliviousAnalyzer/releases).

### Requirements

The analyzer and the code fixes are built against Roslyn 4.9, the oldest version that has every API they use (4.9 added the operations for collection expressions). Whatever runs them needs Roslyn 4.9 or later; with an older one the analyzer does not load, and the build only shows a CS9057 warning instead of NOA reports.

- **Builds** (`dotnet build`, CI) use the SDK's compiler: any .NET SDK from 8.0.200 on. Every supported SDK (10 and later) qualifies.
- **Live analysis and code fixes in an IDE** use the Roslyn the IDE bundles, not the SDK's: Visual Studio 2022 17.9 or later, or a current Rider or C# Dev Kit.

The tests run on the newest Roslyn (5.12) and on 4.9.2. I have run the package with .NET SDK 10.0.404 and 11.0.100; CI also runs it on SDK 8.0.2xx, the oldest host. I have not tested it in Visual Studio or Rider.

### Without the package: project reference to a copy of the source

An analyzer cannot be added to a project as `.cs` files. The compiler loads analyzers from compiled assemblies, so without the package the analyzer has to be built as a separate project that the project you want to analyze references.

1. Copy the folder `src/Randactyl.NullObliviousAnalyzer` into your repository (for example to `tools/Randactyl.NullObliviousAnalyzer`). Leave out `bin` and `obj`.
2. The copied project declares its two dependencies without versions, because this repository uses central package management. In your repository either add these entries to your `Directory.Packages.props`, or add a `Version` attribute to the two `PackageReference` lines in the copied `.csproj`:

   ```xml
   <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" />
   <PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
   ```

3. In the project you want to analyze, add a reference that treats the other project as an analyzer, not as a library:

   ```xml
   <ProjectReference Include="..\..\tools\Randactyl.NullObliviousAnalyzer\Randactyl.NullObliviousAnalyzer.csproj"
                     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```

`ReferenceOutputAssembly="false"` keeps the analyzer out of your compile references. `OutputItemType="Analyzer"` hands its DLL to the compiler as an analyzer. The sample project uses exactly this reference.

4. Optional, for the code fixes in the IDE: copy `src/Randactyl.NullObliviousAnalyzer.CodeFixes` next to the analyzer folder, add `<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.9.2" />`, and add a second `ProjectReference` like the one above for `Randactyl.NullObliviousAnalyzer.CodeFixes.csproj`. The analyzer project builds with or without that folder next to it.

The copied projects set their own language version and nullable setting, but they still inherit the rest of your `Directory.Build.props`. This repository sets `TreatWarningsAsErrors`; if yours does too, the copied projects must compile without warnings. I checked the copy-in steps in an empty scratch repository with only the `PackageVersion` entries, with and without the code fix folder, and the analyzer reported as expected.

## The rules

All rules are enabled by default. Examples below assume a library without nullable annotations:

```csharp
// compiled without nullable annotations
public class Foo { public string Name; }
public delegate void FooAction(Foo f);
public class Legacy
{
    public Foo GetFoo() => null;
    public Foo Prop { get; set; }
    public Foo Field;
    public Foo[] Array;
    public List<Foo> Items;
    public (Foo, int) GetPair() => default;
    public void Fill(out Foo f) { f = null; }
    public void Each(FooAction a) { }
    public virtual void Take(Foo f) { }
}
```

| ID | Reports | Default severity |
|---|---|---|
| NOA001 | A call, delegate invocation, user-defined operator or conversion, or `await` whose result is oblivious | Warning |
| NOA002 | A property or indexer read whose type is oblivious | Warning |
| NOA003 | A field read whose type is oblivious | Warning |
| NOA004 | An `out` or `ref` argument, or a deconstruction target, that may receive an oblivious value | Warning |
| NOA005 | An element of an oblivious collection that is read from an array, iterated, spread or bound to a query range variable | Warning |
| NOA006 | A lambda, anonymous method or method group parameter bound to an oblivious delegate parameter | Warning |
| NOA007 | An override or interface implementation whose parameter or setter value is oblivious in the base member | Warning |
| NOA009 | An oblivious type argument, array element or tuple element that reaches a type declaring it non-null | Warning |

NOA008 (nested oblivious type, Info) existed in 0.1.0 and is replaced by NOA009.

### NOA001: call result

Message: `'Legacy.GetFoo()' returns the null-oblivious type 'Foo'`.

```csharp
Foo a = lib.GetFoo();
```

### NOA002: property or indexer read

Message: `Reading 'Legacy.Prop' yields the null-oblivious type 'Foo'`. A plain assignment (`lib.Prop = f;`) is a write and is not reported. `lib.Prop ??= f;` reads and handles null at once, and is not reported either.

```csharp
var b = lib.Prop;
```

### NOA003: field read

Message: `Reading field 'Legacy.Field' yields the null-oblivious type 'Foo'`. Constants with a non-null value are skipped.

```csharp
var c = lib.Field;
```

### NOA004: out, ref and deconstruction

Message: `'var d' may receive a null-oblivious value of type 'Foo'`. The report sits on the argument. It also covers `var (a, b) = x;`, both when `Deconstruct` has oblivious `out` parameters and when `x` is a tuple with oblivious elements; each target that is not a discard gets a report.

```csharp
lib.Fill(out var d);
var (e, n) = lib.GetPair();
```

### NOA005: collection element

Message: `'e' receives an element of the null-oblivious type 'Foo'`. The name is the iteration variable or the expression text.

```csharp
foreach (var e in lib.Array) { }   // NOA005 on e; NOA003 on lib.Array
List<Foo> copy = [.. lib.Items];   // NOA005 on the spread
var q = from p in lib.Items select p;   // NOA005 on p
```

### NOA006: delegate parameter

Message: `Parameter 'f' is bound to a delegate parameter of the null-oblivious type 'Foo'`.

```csharp
lib.Each(f => f.Name.ToString());
```

### NOA007: override or implementation

Message: `'f' overrides or implements 'Legacy.Take(Foo)', whose incoming parameter or setter value has the null-oblivious type 'Foo'`. Callers of the base member may pass null into your override. Properties are reported on their type only when they have a setter or `init`.

```csharp
class Derived : Legacy
{
    public override void Take(Foo f) { }
}
```

### NOA009: nested oblivious type declared non-null

Message: `'lib.Items.ToArray()' has null-oblivious elements or type arguments of type 'Foo', and 'Foo[]' declares them non-null`.

```csharp
Foo[] items = lib.Items.ToArray();
```

The value itself is not oblivious, but an element, type argument or tuple element inside it is. Converting it to a type that declares that position non-null (`Foo[]`, `List<Foo>`, `IEnumerable<Foo>`, `(Foo, int)`) hides the oblivious elements: nothing reports them when they come out later. The target can be a variable, a parameter, a return type, a cast, a pattern, a lambda or override parameter, or an element of a tuple or collection expression.

A target that keeps them oblivious, such as a `var` local, is not reported; the elements are reported when they come out (NOA005, or NOA001 on `await`). LINQ keeps them oblivious too: `lib.Items.Where(...).ToList()` is reported once, where it lands in a declared type, not at every step.

## Resolving a report

A report goes away when the code that receives the value handles null right there, or passes the value on to a declaration that the compiler's nullable analysis treats as maybe-null. There is no flow analysis: a check on a later line does not count, and a check handles only the value it checks.

**Handled where the value arrives** (NOA001, NOA002, NOA003, NOA005 on an element read):

| Code | Why it counts |
|---|---|
| `lib.GetFoo()?.Name`, `lib.Array?[0]` | `?.` checks the value, and the result is maybe-null to the compiler |
| `lib.GetFoo() ?? fallback`, `lib.GetFoo() ?? throw ...`, `lib.Prop ??= f` | null is replaced or rejected |
| `lib.GetFoo() is { } foo`, `is Foo foo`, `is not null`, `== null`, `!= null` | null is tested; `is var x` does not count, because `x` can be null |
| `Use(lib.GetFoo())` where the parameter is `Foo?` | the compiler checks the value inside `Use`; this covers `Console.WriteLine`, `string.IsNullOrEmpty` and other annotated APIs |
| `return lib.GetFoo();` from a member that returns `Foo?` (also `Task<Foo?>`, `IEnumerable<Foo?>`, lambdas) | the compiler checks the value at every caller |
| `(Foo?)lib.GetFoo()` | the cast gives the value a maybe-null state |
| `lib.GetFoo()!` | an explicit decision that the value is not null |

**Not handled:** storing the value in a variable declared nullable. `Foo? x = lib.GetFoo();` is still reported, and so are `out Foo? x`, `foreach (Foo? e in ...)` and `(Foo? a, int n) = ...`. The compiler gives an oblivious value a not-null state, and the variable takes over that state, so `x.Name` on the next line compiles without a warning.

**Handled at every read** (NOA004, NOA005 on a `foreach` variable, NOA006 on a lambda parameter): when the value arrives in a variable that is declared right there (`out var x`, `foreach (var e in ...)`, `var (a, b) = ...`, a lambda parameter), the report goes away when the variable is never assigned again and every read of it handles null as above. A variable that is never read counts as handled, which covers unused event handler parameters. A plain dereference after a check is still a read that does not handle null:

```csharp
lib.Fill(out var raw);
if (raw is not { } d) return;        // handled: the only read of raw checks it

lib.Fill(out var d2);
if (d2 is null) return;
d2.Name.ToString();                  // still reported: this read does not handle null
```

Existing variables (`lib.Fill(out existing)`, `ref f`, `(x, y) = ...`) are not followed, because their earlier reads cannot be told apart without flow analysis. Declare a new variable there instead.

**Declared nullable** (NOA006, NOA007): a lambda, override or implementation parameter declared `Foo?` is handled, because the compiler checks every use of a parameter declared nullable.

**Nested positions** (NOA009): declare the position nullable (`Foo?[]`, `List<Foo?>`, `(Foo?, int)`), use `var`, or filter (`lib.Items.OfType<Foo>()` returns only the non-null elements).

### Code fixes

| Report | Fixes |
|---|---|
| NOA001, NOA002, NOA003, NOA005 on an element read | *Return early if the value is null* (turns `X x = value;` into `if (value is not X x) return;` in functions that return nothing); *Use ?.* (when the access is a call statement or its result is a reference type); *Throw if the value is null* (`?? throw new InvalidOperationException(...)`); *Assert that the value is not null (!)* |
| NOA004 on `out var x` or a deconstruction target | renames the variable to `xOrNull` and adds `if (xOrNull is not { } x) throw ...;` (or `return;`) after the statement, so the code that uses `x` stays as it is |
| NOA005 on a `foreach` variable | renames it and adds `if (eOrNull is not { } e) continue;` at the top of the loop body |
| NOA006 | declares the lambda parameter nullable, writing out the delegate's parameter types if the lambda had none |
| NOA007 | declares the parameter or the overriding property type nullable |
| NOA009 | adds `?` to the positions of the target type that receive oblivious elements, wherever that type is declared |

Every fix supports Fix All (document, project, solution). No fix is offered for NOA004 on an existing variable or a `ref` argument, NOA005 on a spread or a query range variable, NOA006 on a method group, or NOA009 when the target type is not written in source (a `var` local with an explicit type argument, a parameter of a compiled library).

### Suppressing and severity

Suppress what you have reviewed and decided to keep: one place with `#pragma warning disable NOA001` / `#pragma warning restore NOA001`, a member with `[SuppressMessage("Nullability", "NOA001")]`, or a folder with a section in `.editorconfig`.

Set severity per rule in `.editorconfig`:

```
[*.cs]
dotnet_diagnostic.NOA002.severity = none
```

`none` turns a rule off; `suggestion`, `warning` and `error` work as for any analyzer. In a project that sets `TreatWarningsAsErrors`, the NOA warnings fail the build; either lower them with `dotnet_diagnostic.NOA00x.severity = suggestion`, or add them to `WarningsNotAsErrors` as the sample project does.

### Counting what is left

A file logger writes each warning once:

```
dotnet build --no-incremental -v:q -flp:"logfile=audit.log;warningsonly;verbosity=minimal"
```

Then count by rule, for example in a POSIX shell:

```
grep -o "warning NOA[0-9]*" audit.log | sort | uniq -c
```

The first command rewrites `audit.log` on each run, so the file is a snapshot you can keep and compare.

## Configuration

Two options exclude members by where they are declared. They exist for libraries you cannot change and have already accepted.

| Option | Value | Matching |
|---|---|---|
| `null_oblivious_analyzer.excluded_assemblies` | Comma-separated assembly names | Case-insensitive. An entry ending in `.*` matches the name before the dot and every dotted child: `System.*` matches `System` and `System.Xml`, but not `SystemFoo`. An entry ending in `*` without a dot is a plain prefix: `Sys*` matches `System` and `SystemFoo`. |
| `null_oblivious_analyzer.excluded_namespaces` | Comma-separated namespaces | Case-insensitive. A namespace also covers its child namespaces. `Vendor` and `Vendor.*` mean the same. A child namespace does not cover its parent. A `*` without a dot (`Ven*`) is not a wildcard in this option and matches nothing. |

Whitespace around entries is ignored. Set them in an `.editorconfig` section for C# files or in a `.globalconfig`:

```
# .editorconfig
[*.cs]
null_oblivious_analyzer.excluded_assemblies = System.DirectoryServices.*, Vendor.Legacy
null_oblivious_analyzer.excluded_namespaces = Vendor.Generated
```

```
# .globalconfig
is_global = true
null_oblivious_analyzer.excluded_assemblies = System.DirectoryServices.*
```

A value that applies to the file (from `.editorconfig`) wins. The global value is used only for files that have no value of their own.

Exclusion follows the member that is used, never the types in its signature. If `Holder.Items` is declared in an excluded namespace but you call the LINQ method `ToArray()` on it, `ToArray()` is a different member, in `System.Linq`, and its result is still reported (as NOA009, where it lands in a declared type) unless that assembly is excluded too.

**.NET Framework and netstandard2.0.** Their reference assemblies have no nullable annotations, so the whole base class library is oblivious: even `"x".Trim()` and `list[0]` are reported when you build such a project with nullable enabled. Exclude the framework assemblies there. I verified that this line removes every report for BCL members on both `netstandard2.0` and `net48`:

```
[*.cs]
null_oblivious_analyzer.excluded_assemblies = netstandard, mscorlib, System.*
```

`System.*` matches the assembly named exactly `System` as well as `System.Xml` and the other `System.` assemblies, so one entry covers both. On current .NET the base class library is annotated and this is not needed: the `net11.0` projects in this repository report nothing for BCL members.

## Known limits

Each of these is pinned by a test in `tests/Randactyl.NullObliviousAnalyzer.Tests` unless noted.

- **No flow analysis.** A null check handles only the value it checks; a later read of the same member is reported again. Test: `PinnedBehaviorTests.A_null_check_handles_only_the_value_it_checks_because_there_is_no_flow_analysis`.
- **A type argument inferred from an oblivious argument is not itself oblivious.** `Task.FromResult(lib.GetFoo())` reports the argument (NOA001), but the compiler infers `Task<Foo>` with a non-null `Foo`, so awaiting the task reports nothing. Handle the argument where it arrives. This is a limit of what Roslyn exposes. Test: `PinnedBehaviorTests.Type_argument_inferred_from_an_oblivious_argument_is_not_oblivious`.
- **Converting to a type without elements hides them.** `object o = list;` (or `IEnumerable`, non-generic) is not reported, and neither is the cast back with `(List<Foo>)o`. There is no position to compare, and the cast is a type you write yourself. Test: `PinnedBehaviorTests.Converting_to_a_type_without_nested_positions_is_not_reported`.
- **Switch statements and switch expressions do not count as handling null**, even with a `null` arm. Only `is` patterns and `==` / `!=` comparisons do. Test: `PinnedBehaviorTests.Switch_with_a_null_arm_does_not_count_as_handling_null`.
- **A `params` element does not count as handled**, even when the element type is `object?`: `string.Format("{0}", lib.GetFoo())` is reported. Only an argument bound directly to a `T?` parameter counts. Test: `PinnedBehaviorTests.Params_element_does_not_count_as_handling_null`.
- **Only values coming in are covered.** Passing a value by value to an oblivious method is not reported. Only `out` and `ref` arguments are. Test: `RefOutArgumentTests.By_value_arguments_are_not_reported`.
- **Overrides are checked on the incoming side only.** NOA007 looks at parameters and setter values. A read-only property override is not reported. Test: `PropertyOverrideTests.Read_only_and_nullable_overrides_are_not_reported`.
- **Only nullable-enabled use sites are reported.** A use inside `#nullable disable`, or inside a region with `#nullable enable warnings` (annotations off), is skipped. Tests: `CallResultTests.Use_site_inside_nullable_disable_is_not_reported`, `CallResultTests.Use_site_with_annotations_disabled_is_not_reported`.
- **Generated code is skipped**, whether it is marked by an `<auto-generated/>` header or by a `.g.cs` file name. Tests: `CallResultTests.Generated_code_is_skipped_by_header`, `CallResultTests.Generated_code_is_skipped_by_file_name`.
- **Writes are not reads.** `lib.Prop = f`, `lib.Field = f`, `lib[0] = f` and `lib.Array[0] = f` assign and are not reported for the assigned member. Tests: `MemberReadTests.Plain_assignment_is_a_write_and_coalesce_assignment_is_handled`, `MemberReadTests.Field_writes_are_not_reads_and_coalesce_assignment_is_handled`, `CollectionElementTests.Array_element_write_is_not_a_read`.
- **`nameof` operands are never reported.** Tests: `CallResultTests.Nameof_operand_is_never_reported`, `MemberReadTests.Nameof_operand_is_never_reported`.
- **Exclusions follow the member used, not the types in its signature.** See Configuration. Tests: `ConfigurationTests.Namespace_exclusion_is_decided_by_the_member_not_the_type_argument`, `ConfigurationTests.Assembly_exclusion_is_decided_by_the_member_not_the_type_argument`.
- **Exclusions follow a value through an initialized local, and no further.** With the defining assembly or namespace excluded, `var arr = lib.Array; foreach (var e in arr) { }` is silent, and so are `arr[0]`, `[.. items]` and `await task` when the local was initialized from an excluded member. The analyzer follows the initializer through further locals, parentheses, implicit conversions and `await`. It does not follow a local that is assigned again anywhere in the same member, a local with no initializer, a parameter (including a lambda parameter), or any other expression. Those are still reported. Tests: `ConfigurationTests.Excluded_assembly_follows_a_local_into_foreach` and the tests next to it, `ConfigurationTests.Local_that_is_reassigned_is_still_reported`, `ConfigurationTests.Parameter_origin_cannot_be_determined_and_is_still_reported`.

## How the analyzers are tested

Analyzer tests are ordinary unit tests that compile a small piece of C# source in memory, run the analyzer on it, and compare the diagnostics it produced with the diagnostics the test expects. This repository uses the `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` packages (version 1.1.4) for that. The in-memory compilations use Roslyn 5.12 by default; `dotnet test -p:RoslynTestVersion=4.9.2` runs them on the version the analyzer is built against, as CI does.

The expected diagnostics are written into the test source with markup. `{|NOA001:lib.GetFoo()|}` means "the analyzer must report NOA001 on exactly this span of text". The test fails if an expected diagnostic is missing, if an extra one appears, or if a span is off. Source without any markup therefore asserts that the analyzer reports nothing. Markup can be nested, as in `{|NOA001:{|NOA003:lib.Items|}.First()|}`.

This is a real test from the suite (`CallResultTests.cs`):

```csharp
[Test]
public Task Method_from_oblivious_assembly()
{
    const string source = """
        class C
        {
            void M(Lib lib)
            {
                var a = {|NOA001:lib.GetFoo()|};
                var b = {|NOA001:Lib.StaticFoo()|};
                var s = {|NOA001:lib.GetString()|};
            }
        }
        """;
    return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
}
```

The analyzer needs a value that is oblivious, which means code compiled without nullable annotations. `tests/Randactyl.NullObliviousAnalyzer.Testing/AnalyzerVerifier.cs` provides that. `VerifyAsync(source, Libs.Oblivious)` builds two projects in memory. The main project holds the test source and has nullable enabled. A second project, named `ObliviousLib`, holds the library source from `Libs.cs` (the `Lib`, `Foo` and `Box<T>` types used in the examples) and has nullable disabled, so every reference type it exposes is oblivious. The main project references the second. Tests that need the real world instead pass a NuGet package: `DirectoryServicesIntegrationTests` adds `System.DirectoryServices.AccountManagement` 11.0.0. Options on the helper set an `.editorconfig` or `.globalconfig` and the file name (to test generated-code detection). The helper uses a plain `DefaultVerifier`, which throws ordinary exceptions, so it works with any test runner.

Code fix tests (`CodeFixTests.cs`) use `CodeFixVerifier<TCodeFix>.VerifyAsync(source, fixedSource, equivalenceKey, Libs.Oblivious)` from the same project. It applies the fix chosen by its equivalence key to every diagnostic it is offered for, one at a time and through Fix All, and compares the result with `fixedSource`. Markup in `fixedSource` lists the reports that remain.

Run the tests from the repository root:

```
dotnet test
```

Expect 154 passing tests. `dotnet test` in the root finds the solution file and runs the one test project. `global.json` makes it use the Microsoft.Testing.Platform runner, which TUnit is built on.

Two things to know when the tests run:

- They download from nuget.org: the .NET reference assemblies the in-memory compilations need, and the `System.DirectoryServices.AccountManagement` package for the integration tests. The first run needs network access.
- The test platform sends Microsoft telemetry unless you set `DOTNET_CLI_TELEMETRY_OPTOUT=1` or `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`.

## Repository layout and build commands

```
src/Randactyl.NullObliviousAnalyzer/             the analyzer (netstandard2.0), rules NOA001-NOA007 and NOA009
src/Randactyl.NullObliviousAnalyzer.CodeFixes/   the code fixes (netstandard2.0), packed next to the analyzer
tests/Randactyl.NullObliviousAnalyzer.Testing/   AnalyzerVerifier and CodeFixVerifier, the shared test helpers
tests/Randactyl.NullObliviousAnalyzer.Tests/     154 tests (TUnit)
samples/DirectoryServicesSample/                 uses the analyzer and code fixes through project references
Randactyl.NullObliviousAnalyzer.slnx             the solution
eng/smoke-test.sh                                installs the packed analyzer into a throwaway project
.github/workflows/                               CI on every push, release on a v* tag
cliff.toml                                       git-cliff configuration for the release notes
Directory.Build.props, Directory.Packages.props  shared settings, central package versions
global.json                                      selects the Microsoft.Testing.Platform runner
```

```
dotnet build Randactyl.NullObliviousAnalyzer.slnx                         # whole solution
dotnet build samples/DirectoryServicesSample --no-incremental             # see the NOA warnings
dotnet test                                                              # 154 tests
dotnet pack src/Randactyl.NullObliviousAnalyzer -c Release -o artifacts/pkg   # the NuGet package
eng/smoke-test.sh artifacts/pkg 0.2.0 11                                  # check the package in a clean project on SDK 11
```

To release, push a tag such as `v0.2.0` that matches `<Version>` in the analyzer's `.csproj`. The release workflow runs CI, creates the GitHub release with the package attached and release notes generated from the commit messages, and pushes the package to GitHub Packages. Publishing to nuget.org is a separate step.

`Directory.Build.props` sets `TreatWarningsAsErrors` for every project. The sample project lists its NOA rules in `WarningsNotAsErrors` so that its purpose, showing the warnings, does not fail the build.
