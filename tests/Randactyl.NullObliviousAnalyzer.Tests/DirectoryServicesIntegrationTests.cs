using System;
using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using TUnit.Core;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>
/// Real package, real metadata. System.DirectoryServices.AccountManagement ships no nullable
/// annotations in any target framework, so every reference type it exposes is oblivious. Its dependency
/// System.DirectoryServices IS annotated, so DirectoryEntry.Properties / PropertyValueCollection.Value are not reported.
/// </summary>
public class DirectoryServicesIntegrationTests
{
    private static void UsePackage(AnalyzerTestOptions options) =>
        options.Packages = [new NuGetPackage("System.DirectoryServices.AccountManagement", "11.0.0")];

    [Test]
    public Task FindByIdentity_and_Members_are_reported()
    {
        const string source = """
            using System.DirectoryServices.AccountManagement;
            class C
            {
                void AddUser(PrincipalContext context, string identity, UserPrincipal user)
                {
                    GroupPrincipal group = {|NOA001:GroupPrincipal.FindByIdentity(context, identity)|};
                    {|NOA002:group.Members|}.Add(user);
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, UsePackage);
    }

    [Test]
    public Task Foreach_over_Members_reports_the_oblivious_Principal_element()
    {
        const string source = """
            using System.DirectoryServices.AccountManagement;
            class C
            {
                void Each(GroupPrincipal group)
                {
                    foreach (Principal {|NOA005:p|} in {|NOA002:group.Members|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, UsePackage);
    }

    [Test]
    public Task Principal_properties_and_underlying_object_are_oblivious()
    {
        const string source = """
            using System.DirectoryServices;
            using System.DirectoryServices.AccountManagement;
            class C
            {
                void M(Principal principal)
                {
                    var name = {|NOA002:principal.Name|};
                    var entry = (DirectoryEntry){|NOA001:principal.GetUnderlyingObject()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, UsePackage);
    }

    [Test]
    public Task DirectoryEntry_from_System_DirectoryServices_is_annotated_and_not_reported()
    {
        const string source = """
            using System.DirectoryServices;
            class C
            {
                void M(DirectoryEntry entry)
                {
                    var value = entry.Properties["cn"].Value;
                    var path = entry.Path;
                    var children = entry.Children;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, UsePackage);
    }

    [Test]
    public Task Excluding_the_package_assembly_silences_it()
    {
        const string source = """
            using System.DirectoryServices.AccountManagement;
            class C { void M(PrincipalContext context) { var g = GroupPrincipal.FindByIdentity(context, "x"); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, o =>
        {
            UsePackage(o);
            o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = System.DirectoryServices.*";
        });
    }
}
