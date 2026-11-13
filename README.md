# Randactyl.NullObliviousAnalyzer

Roslyn analyzers that list every place where a value from code without nullable annotations enters your nullable-enabled code.

C# nullable reference types only protect you when the compiler knows whether a value can be null. Code that was compiled without nullable annotations (an old NuGet package, a `#nullable disable` region) gives the compiler no information. The compiler calls such a value *null-oblivious* and stays silent about it. This analyzer reports each place such a value comes in, so you can decide, one by one, whether it can be null.

Here is the case that motivated the project. `System.DirectoryServices.AccountManagement` ships without nullable annotations, and `FindByIdentity` returns null when nothing matches:

```csharp
#nullable enable

GroupPrincipal group = GroupPrincipal.FindByIdentity(context, groupName);
UserPrincipal user = UserPrincipal.FindByIdentity(context, userName);

group.Members.Add(user);   // NullReferenceException if either lookup found nothing
```

The compiler reports nothing for this code. With the analyzer installed, the build prints this for the sample project's version of that code (`Program.cs` in `samples/DirectoryServicesSample`):

```
Program.cs(24,32): warning NOA001: 'GroupPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'GroupPrincipal'
Program.cs(25,30): warning NOA001: 'UserPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'UserPrincipal'
Program.cs(27,9): warning NOA002: Reading 'GroupPrincipal.Members' yields the null-oblivious type 'PrincipalCollection'
```

To fix a report, declare the receiving variable nullable (`GroupPrincipal? group = ...`) and check it. The compiler's own nullable warnings then protect every later use.

## Is this for you?

Use it if your project has `<Nullable>enable</Nullable>` and calls libraries that were never annotated, and you want a list of the places where null can get in unnoticed. It is an audit tool. If you want warnings only where a null can really be dereferenced, this is the wrong tool: the analyzer does not look at what happens to a value after it arrives (see [What it does not do](#what-it-does-not-do)).

Terms used below:

- **Null-oblivious** (or just *oblivious*): a reference type that comes from code compiled without nullable annotations. In Roslyn it is a type with `NullableAnnotation.None`. It is neither "not null" (`string`) nor "maybe null" (`string?`).
- **Nullable-enabled code**: code where nullable annotations are on. The analyzer reports only there. A use site inside `#nullable disable` is never reported.

## What the sample build prints

`samples/DirectoryServicesSample` uses the analyzer through a project reference. `dotnet build samples/DirectoryServicesSample --no-incremental` prints these warnings (paths shortened):

```
Program.cs(24,32): warning NOA001: 'GroupPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'GroupPrincipal'
Program.cs(33,21): warning NOA001: 'GroupPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'GroupPrincipal'
Program.cs(25,30): warning NOA001: 'UserPrincipal.FindByIdentity(PrincipalContext, string)' returns the null-oblivious type 'UserPrincipal'
Program.cs(27,9): warning NOA002: Reading 'GroupPrincipal.Members' yields the null-oblivious type 'PrincipalCollection'
Program.cs(35,28): warning NOA005: 'member' receives an element of the null-oblivious type 'Principal'
Program.cs(35,38): warning NOA002: Reading 'GroupPrincipal.Members' yields the null-oblivious type 'PrincipalCollection'
Program.cs(36,31): warning NOA002: Reading 'Principal.Name' yields the null-oblivious type 'string'
Program.cs(39,38): warning NOA006: Parameter 'member' is bound to a delegate parameter of the null-oblivious type 'Principal'
Program.cs(39,66): warning NOA002: Reading 'Principal.DistinguishedName' yields the null-oblivious type 'string'
```

One line of code can carry several reports. Line 35 is `foreach (Principal member in group.Members)`: NOA002 for reading `Members`, and NOA005 for the `Principal` that each loop step puts into `member`. MSBuild prints every warning twice, once as it happens and once in its summary; the list above is the unique set.

Incremental builds do not run the compiler again when nothing changed, so they print no warnings. Use `--no-incremental` (or `dotnet build -t:Rebuild`) to see them again.

## Install

An analyzer cannot be added to a project as `.cs` files. The compiler loads analyzers from compiled assemblies, so the analyzer has to be built as a separate project and the project you want to analyze must reference the result. There are two ways to do that.

### Option 1: project reference to a copy of the source

This is the way to drop the analyzer into a repository without publishing a package.

1. Copy the folder `src/Randactyl.NullObliviousAnalyzer` into your repository (for example to `tools/Randactyl.NullObliviousAnalyzer`). Leave out `bin` and `obj`.
2. The copied project declares its two dependencies without versions, because this repository uses central package management. In your repository either add these entries to your `Directory.Packages.props`, or add a `Version` attribute to the two `PackageReference` lines in the copied `.csproj`:

   ```xml
   <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
   <PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
   ```

3. In the project you want to analyze, add a reference that treats the other project as an analyzer, not as a library:

   ```xml
   <ProjectReference Include="..\..\tools\Randactyl.NullObliviousAnalyzer\Randactyl.NullObliviousAnalyzer.csproj"
                     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```

`ReferenceOutputAssembly="false"` keeps the analyzer out of your compile references. `OutputItemType="Analyzer"` hands its DLL to the compiler as an analyzer. The sample project uses exactly this reference.

The copied project sets its own language version and nullable setting, but it still inherits the rest of your `Directory.Build.props`. This repository sets `TreatWarningsAsErrors`; if yours does too, the copied analyzer project must compile without warnings. I checked the copy-in steps in an empty scratch repository with only the two `PackageVersion` entries, and the analyzer reported as expected. The analyzer is built against Roslyn 4.14 and runs in a compiler at least that new. I have only run it with .NET SDK 11.0.100.

### Option 2: the NuGet package, built locally

The package is not published anywhere. Build it yourself:

```
dotnet pack src/Randactyl.NullObliviousAnalyzer -c Release -o artifacts/pkg
```

This creates `artifacts/pkg/Randactyl.NullObliviousAnalyzer.0.1.0.nupkg`. Make the folder a package source, for example with a `nuget.config` next to your solution:

```xml
<configuration>
  <packageSources>
    <add key="local-analyzers" value="C:\path\to\artifacts\pkg" />
  </packageSources>
</configuration>
```

Then reference the package from the project you want to analyze:

```xml
<PackageReference Include="Randactyl.NullObliviousAnalyzer" Version="0.1.0" PrivateAssets="all" />
```

The package contains only the analyzer DLL under `analyzers/dotnet/cs` and this README. It has no `lib` folder and no dependencies, and it is marked as a development dependency, so it does not flow to projects that reference your project. The package has no licence, repository URL or project URL yet; the owner has to choose them.

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
| NOA004 | An `out` or `ref` argument, or a `Deconstruct` target, that may receive an oblivious value | Warning |
| NOA005 | An element of an oblivious collection that is read from an array, iterated, spread or bound to a query range variable | Warning |
| NOA006 | A lambda, anonymous method or method group parameter bound to an oblivious delegate parameter | Warning |
| NOA007 | An override or interface implementation whose parameter or setter value is oblivious in the base member | Warning |
| NOA008 | A value whose type is not oblivious itself but contains an oblivious type argument, array element or tuple element | Info |

**NOA001, call result.** Message: `'Legacy.GetFoo()' returns the null-oblivious type 'Foo'`.

```csharp
Foo a = lib.GetFoo();
```

**NOA002, property or indexer read.** Message: `Reading 'Legacy.Prop' yields the null-oblivious type 'Foo'`. A plain assignment (`lib.Prop = f;`) is a write and is not reported. A compound assignment such as `lib.Prop ??= f;` reads first and is reported.

```csharp
var b = lib.Prop;
```

**NOA003, field read.** Message: `Reading field 'Legacy.Field' yields the null-oblivious type 'Foo'`. Constants with a non-null value are skipped.

```csharp
var c = lib.Field;
```

**NOA004, out, ref and deconstruction.** Message: `The callee may assign a null-oblivious value of type 'Foo' through parameter 'f'`. The report sits on the argument. It also covers `var (a, b) = x;` when `Deconstruct` has oblivious `out` parameters; each target that is not a discard and not explicitly nullable gets a report.

```csharp
lib.Fill(out var d);
```

**NOA005, collection element.** Message: `'e' receives an element of the null-oblivious type 'Foo'`. The name is the iteration variable or the expression text.

```csharp
foreach (var e in lib.Array) { }   // NOA005 on e; NOA003 on lib.Array
List<Foo> copy = [.. lib.Items];   // NOA005 on the spread
var q = from p in lib.Items select p;   // NOA005 on p
```

**NOA006, delegate parameter.** Message: `Parameter 'f' is bound to a delegate parameter of the null-oblivious type 'Foo'`. A lambda parameter you declare as nullable (`(object? s, EventArgs? e) => ...`) is not reported.

```csharp
lib.Each(f => { });
```

**NOA007, override or implementation.** Message: `'f' overrides or implements 'Legacy.Take(Foo)', whose incoming parameter or setter value has the null-oblivious type 'Foo'`. Callers of the base member may pass null into your override. Declaring the parameter nullable (`Foo? f`) clears the report. Properties are reported on their type only when they have a setter or `init`.

```csharp
class Derived : Legacy
{
    public override void Take(Foo f) { }
}
```

**NOA008, nested oblivious type.** Message: `'Legacy.GetPair()' has type '(Foo, int)' which contains a null-oblivious reference type`.

```csharp
var pair = lib.GetPair();
```

NOA008 is the only rule that defaults to Info, and Info diagnostics do not appear in `dotnet build` output. Its subject is different from the others. The value itself is not oblivious (a tuple, a `List<Foo>`, an array, a `ValueTask<Foo>` is never null-oblivious by itself); only something inside it is. Nothing is reported on the inner value until it comes out, and the other rules report it then (for example NOA005 when you iterate). A tuple deconstructed with `var (a, n) = pair` is the exception: no other rule reports it (see the limits below), so NOA008 is the only signal. Because it can fire on every intermediate LINQ result, I kept it at Info so it does not drown the warnings. To see it in the build, raise it in `.editorconfig`:

```
[*.cs]
dotnet_diagnostic.NOA008.severity = warning
```

NOA008 is not reported when the value is awaited, iterated or spread right where it is produced and that step already produces a NOA001 or NOA005 report, because the report would only repeat it.

## What it does not do

The analyzer does no flow analysis, on purpose. It looks at declarations: where does an oblivious value come from? It does not look at what the code does with it afterwards. A report stays even when you check for null on the next line:

```csharp
if (lib.Prop != null)       // NOA002 on this read
{
    var x = lib.Prop;       // NOA002 on this one too
}
```

The owner wants an audit list, not a second nullable analysis. The compiler's flow analysis already exists and works once a value is typed as nullable.

How to work with that:

1. **Fix.** Receive the value in a variable declared nullable (`GroupPrincipal? group = ...`) and check it before use, or check it right away. The NOA report on that line remains, because the analyzer reports where the value arrives, not how the variable is declared. The compiler's nullable warnings now guard the later uses.
2. **Suppress or baseline what you have reviewed.** Suppress one place with `#pragma warning disable NOA001` / `#pragma warning restore NOA001`, or on a member with `[SuppressMessage("Nullability", "NOA001")]`. To silence a rule in a folder, give that folder a section in `.editorconfig`. There is no built-in baseline file. A simple baseline is to save the audit output (below) in the repository and compare it in review or CI.
3. **Set severity per rule** in `.editorconfig`:

   ```
   [*.cs]
   dotnet_diagnostic.NOA002.severity = none
   dotnet_diagnostic.NOA008.severity = warning
   ```

   `none` turns a rule off; `suggestion`, `warning` and `error` work as for any analyzer. In a project that sets `TreatWarningsAsErrors`, the NOA warnings fail the build; either lower them with `dotnet_diagnostic.NOA00x.severity = suggestion`, or add them to `WarningsNotAsErrors` as the sample project does.

### Using it as an audit list

Raise the rules you want to count to warnings, rebuild, and write the warnings to a file. A file logger writes each warning once:

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

Exclusion follows the member that is used, never the types in its signature. If `Holder.Items` is declared in an excluded namespace but you call the LINQ method `ToArray()` on it, `ToArray()` is a different member, in `System.Linq`, and its result is still reported (as NOA008) unless that assembly is excluded too.

**.NET Framework and netstandard2.0.** Their reference assemblies have no nullable annotations, so the whole base class library is oblivious: even `"x".Trim()` and `list[0]` are reported when you build such a project with nullable enabled. Exclude the framework assemblies there. I verified that this line removes every report for BCL members on both `netstandard2.0` and `net48`:

```
[*.cs]
null_oblivious_analyzer.excluded_assemblies = netstandard, mscorlib, System.*
```

`System.*` matches the assembly named exactly `System` as well as `System.Xml` and the other `System.` assemblies, so one entry covers both. On current .NET the base class library is annotated and this is not needed: the `net11.0` projects in this repository report nothing for BCL members.

## Known limits

Each of these is pinned by a test in `tests/Randactyl.NullObliviousAnalyzer.Tests` unless noted.

- **A type argument inferred from an oblivious argument is not itself oblivious.** `Task.FromResult(lib.GetFoo())` reports the argument (NOA001), but the compiler infers `Task<Foo>` with a non-null `Foo`, so awaiting the task reports nothing. This is a limit of what Roslyn exposes. Test: `PinnedBehaviorTests.Type_argument_inferred_from_an_oblivious_argument_is_not_oblivious`.
- **An explicitly typed local does not carry obliviousness.** `var inferred = lib.Array; var a = inferred[0];` reports the element (NOA005), but `Foo[] declared = lib.Array; var b = declared[0];` does not, because `declared` is a non-null `Foo[]` as far as the compiler knows. The report on `lib.Array` itself remains. Test: `PinnedBehaviorTests.Var_locals_carry_obliviousness_but_explicitly_typed_locals_do_not`.
- **No flow analysis.** Null-checked values are still reported. Test: `PinnedBehaviorTests.Null_checked_values_are_still_reported_because_there_is_no_flow_analysis`.
- **Tuple elements are not reported one by one.** `var (a, n) = lib.GetPair()` gets NOA008 on the call and nothing on `a`, because a tuple is not deconstructed through a method. Test: `DeconstructionTests.Tuples_are_left_to_NOA008_and_annotated_deconstructors_are_not_reported`.
- **Only values coming in are covered.** Passing a value by value to an oblivious method is not reported. Only `out` and `ref` arguments are. Test: `RefOutArgumentTests.By_value_arguments_are_not_reported`.
- **Overrides are checked on the incoming side only.** NOA007 looks at parameters and setter values. A read-only property override is not reported. Test: `PropertyOverrideTests.Read_only_and_nullable_overrides_are_not_reported`.
- **Only nullable-enabled use sites are reported.** A use inside `#nullable disable`, or inside a region with `#nullable enable warnings` (annotations off), is skipped. Tests: `CallResultTests.Use_site_inside_nullable_disable_is_not_reported`, `CallResultTests.Use_site_with_annotations_disabled_is_not_reported`.
- **Generated code is skipped**, whether it is marked by an `<auto-generated/>` header or by a `.g.cs` file name. Tests: `CallResultTests.Generated_code_is_skipped_by_header`, `CallResultTests.Generated_code_is_skipped_by_file_name`.
- **Writes are not reads.** `lib.Prop = f`, `lib.Field = f`, `lib[0] = f` and `lib.Array[0] = f` assign and are not reported for the assigned member. Tests: `MemberReadTests.Plain_assignment_is_a_write_but_compound_assignment_is_a_read`, `MemberReadTests.Field_writes_are_not_reads`, `CollectionElementTests.Array_element_write_is_not_a_read`.
- **`nameof` operands are never reported.** Tests: `CallResultTests.Nameof_operand_is_never_reported`, `MemberReadTests.Nameof_operand_is_never_reported`.
- **Exclusions follow the member used, not the types in its signature.** See Configuration. Tests: `ConfigurationTests.Namespace_exclusion_is_decided_by_the_member_not_the_type_argument`, `ConfigurationTests.Assembly_exclusion_is_decided_by_the_member_not_the_type_argument`.
- **Exclusions follow a value through an initialized local, and no further.** With the defining assembly or namespace excluded, `var arr = lib.Array; foreach (var e in arr) { }` is silent, and so are `arr[0]`, `[.. items]` and `await task` when the local was initialized from an excluded member. The analyzer follows the initializer through further locals, parentheses, implicit conversions and `await`. It does not follow a local that is assigned again anywhere in the same member, a local with no initializer, a parameter (including a lambda parameter), or any other expression. Those are still reported. Tests: `ConfigurationTests.Excluded_assembly_follows_a_local_into_foreach` and the tests next to it, `ConfigurationTests.Local_that_is_reassigned_is_still_reported`, `ConfigurationTests.Parameter_origin_cannot_be_determined_and_is_still_reported`.

## How the analyzers are tested

Analyzer tests are ordinary unit tests that compile a small piece of C# source in memory, run the analyzer on it, and compare the diagnostics it produced with the diagnostics the test expects. This repository uses the `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` package (version 1.1.4) for that.

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

Run the tests from the repository root:

```
dotnet test
```

Expect 123 passing tests. `dotnet test` in the root finds the solution file and runs the one test project. `global.json` makes it use the Microsoft.Testing.Platform runner, which TUnit is built on.

Two things to know when the tests run:

- They download from nuget.org: the .NET reference assemblies the in-memory compilations need, and the `System.DirectoryServices.AccountManagement` package for the integration tests. The first run needs network access.
- The test platform sends Microsoft telemetry unless you set `DOTNET_CLI_TELEMETRY_OPTOUT=1` or `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`.

## Repository layout and build commands

```
src/Randactyl.NullObliviousAnalyzer/             the analyzer (netstandard2.0), rules NOA001-NOA008
tests/Randactyl.NullObliviousAnalyzer.Testing/   AnalyzerVerifier, the shared test helper
tests/Randactyl.NullObliviousAnalyzer.Tests/     123 tests (TUnit)
samples/DirectoryServicesSample/                 uses the analyzer through a project reference
Randactyl.NullObliviousAnalyzer.slnx             the solution
Directory.Build.props, Directory.Packages.props  shared settings, central package versions
global.json                                      selects the Microsoft.Testing.Platform runner
```

```
dotnet build Randactyl.NullObliviousAnalyzer.slnx                         # whole solution
dotnet build samples/DirectoryServicesSample --no-incremental             # see the NOA warnings
dotnet test                                                              # 123 tests
dotnet pack src/Randactyl.NullObliviousAnalyzer -c Release -o artifacts/pkg   # the NuGet package
```

`Directory.Build.props` sets `TreatWarningsAsErrors` for every project. The sample project lists NOA001 to NOA007 in `WarningsNotAsErrors` so that its purpose, showing the warnings, does not fail the build.
