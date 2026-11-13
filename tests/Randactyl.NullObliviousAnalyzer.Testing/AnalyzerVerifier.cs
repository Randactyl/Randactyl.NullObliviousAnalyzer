using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Randactyl.NullObliviousAnalyzer.Testing;

/// <summary>A NuGet package added to the compilation under test.</summary>
public sealed record NuGetPackage(string Id, string Version);

public sealed class AnalyzerTestOptions
{
    /// <summary>Contents of an .editorconfig; wrapped in a [*.cs] section when it has none.</summary>
    public string? EditorConfig { get; set; }

    /// <summary>Contents of a global analyzer config (is_global is added).</summary>
    public string? GlobalConfig { get; set; }

    public IReadOnlyList<NuGetPackage> Packages { get; set; } = [];

    /// <summary>Source of a second, nullable-disabled project referenced by the test project.</summary>
    public string? ObliviousLibrarySource { get; set; }

    public string ObliviousLibraryName { get; set; } = "ObliviousLib";

    /// <summary>File path of the main source (must sit under the editorconfig root), e.g. to test generated-code detection.</summary>
    public string FileName { get; set; } = "/0/Test0.cs";
}

/// <summary>Framework-neutral entry point: DefaultVerifier throws plain exceptions that any runner reports.</summary>
public static class AnalyzerVerifier
{
    public static Task VerifyAsync(string source) => VerifyAsync(source, _ => { });

    public static Task VerifyAsync(string source, string obliviousLibrarySource) =>
        VerifyAsync(source, o => o.ObliviousLibrarySource = obliviousLibrarySource);

    public static Task VerifyAsync(string source, string obliviousLibrarySource, Action<AnalyzerTestOptions> configure) =>
        VerifyAsync(source, o =>
        {
            o.ObliviousLibrarySource = obliviousLibrarySource;
            configure(o);
        });

    public static Task VerifyAsync(string source, Action<AnalyzerTestOptions> configure)
    {
        var options = new AnalyzerTestOptions();
        configure(options);
        return CreateTest(source, options).RunAsync();
    }

    private static CSharpAnalyzerTest<NullObliviousAnalyzer, DefaultVerifier> CreateTest(string source, AnalyzerTestOptions options)
    {
        var references = ReferenceAssemblies.Net.Net100;
        if (options.Packages.Count > 0)
        {
            var packages = new List<PackageIdentity>();
            foreach (var package in options.Packages)
                packages.Add(new PackageIdentity(package.Id, package.Version));
            references = references.AddPackages([.. packages]);
        }

        var test = new CSharpAnalyzerTest<NullObliviousAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = references,
            
        };
        test.TestState.Sources.Add((options.FileName, SourceText.From(source)));

        if (options.EditorConfig is { } editorConfig)
        {
            var text = editorConfig.Contains('[') ? editorConfig : "[*.cs]\n" + editorConfig;
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n" + text));
        }

        if (options.GlobalConfig is { } globalConfig)
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true\n" + globalConfig));

        if (options.ObliviousLibrarySource is { } librarySource)
        {
            var library = test.TestState.AdditionalProjects[options.ObliviousLibraryName];
            library.Sources.Add(("Lib0.cs", SourceText.From(librarySource)));
            test.TestState.AdditionalProjectReferences.Add(options.ObliviousLibraryName);
        }

        // Nullable is off by default in the harness; the project under test enables it, the library never does.
        var libraryName = options.ObliviousLibraryName;
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            var nullable = project.Name == libraryName ? NullableContextOptions.Disable : NullableContextOptions.Enable;
            var compilationOptions = ((CSharpCompilationOptions)project.CompilationOptions!).WithNullableContextOptions(nullable);
            return solution.WithProjectCompilationOptions(projectId, compilationOptions);
        });

        return test;
    }
}
