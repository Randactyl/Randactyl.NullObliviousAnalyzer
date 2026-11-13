using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Randactyl.NullObliviousAnalyzer;

/// <summary>Reads the excluded_assemblies / excluded_namespaces options, per syntax tree with global fallback.</summary>
internal sealed class ExclusionSettings
{
    private const string AssembliesKey = "null_oblivious_analyzer.excluded_assemblies";
    private const string NamespacesKey = "null_oblivious_analyzer.excluded_namespaces";

    private readonly AnalyzerConfigOptionsProvider _provider;
    private readonly ConcurrentDictionary<SyntaxTree, Exclusions> _cache = new();

    public ExclusionSettings(AnalyzerConfigOptionsProvider provider) => _provider = provider;

    /// <summary>
    /// Exclusion is decided by the member that is used (its assembly and namespace), never by the types
    /// that appear in its signature.
    /// </summary>
    public bool IsExcluded(ISymbol? member, SyntaxTree useSite)
    {
        if (member is null)
            return false;

        var exclusions = _cache.GetOrAdd(useSite, Load);
        return !exclusions.IsEmpty && exclusions.Matches(member);
    }

    private Exclusions Load(SyntaxTree tree)
    {
        var options = _provider.GetOptions(tree);
        return new Exclusions(Read(options, AssembliesKey), Read(options, NamespacesKey));
    }

    private string Read(AnalyzerConfigOptions treeOptions, string key)
    {
        if (
            treeOptions.TryGetValue(key, out var value)
            || _provider.GlobalOptions.TryGetValue(key, out value)
        )
            return value;
        return string.Empty;
    }

    private sealed class Exclusions
    {
        private readonly List<string> _assemblyNames = new();
        private readonly List<string> _assemblyPrefixes = new();
        private readonly List<string> _assemblyFamilies = new();
        private readonly List<string> _namespaces = new();

        public Exclusions(string assemblies, string namespaces)
        {
            foreach (var entry in Split(assemblies))
            {
                // `System.*` is the name `System` and everything below it; `Sys*` is a plain prefix.
                if (entry.EndsWith(".*", StringComparison.Ordinal))
                    _assemblyFamilies.Add(entry.Substring(0, entry.Length - 2));
                else if (entry.EndsWith("*", StringComparison.Ordinal))
                    _assemblyPrefixes.Add(entry.Substring(0, entry.Length - 1));
                else
                    _assemblyNames.Add(entry);
            }

            foreach (var entry in Split(namespaces))
                _namespaces.Add(
                    entry.EndsWith(".*", StringComparison.Ordinal)
                        ? entry.Substring(0, entry.Length - 2)
                        : entry
                );
        }

        public bool IsEmpty =>
            _assemblyNames.Count
                + _assemblyPrefixes.Count
                + _assemblyFamilies.Count
                + _namespaces.Count
            == 0;

        public bool Matches(ISymbol member)
        {
            if (member.ContainingAssembly?.Name is { } assembly)
            {
                foreach (var name in _assemblyNames)
                    if (string.Equals(name, assembly, StringComparison.OrdinalIgnoreCase))
                        return true;
                foreach (var family in _assemblyFamilies)
                    if (IsSameOrChild(assembly, family))
                        return true;
                foreach (var prefix in _assemblyPrefixes)
                    if (assembly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
            }

            if (
                _namespaces.Count > 0
                && member.ContainingNamespace is { IsGlobalNamespace: false } ns
            )
            {
                var text = ns.ToDisplayString();
                foreach (var excluded in _namespaces)
                    if (IsSameOrChild(text, excluded))
                        return true;
            }

            return false;
        }

        private static bool IsSameOrChild(string name, string parent) =>
            string.Equals(name, parent, StringComparison.OrdinalIgnoreCase)
            || (
                name.Length > parent.Length
                && name[parent.Length] == '.'
                && name.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            );

        private static IEnumerable<string> Split(string list)
        {
            foreach (var part in list.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }
    }
}
