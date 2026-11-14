#!/usr/bin/env bash
# Installs the packed analyzer into a throwaway project outside this repository
# and checks that a build reports NOA warnings.
#
# Usage: eng/smoke-test.sh <package-dir> <version> <sdk>
#   eng/smoke-test.sh artifacts/pkg 0.2.0 10        # newest installed 10.0.x SDK
#   eng/smoke-test.sh artifacts/pkg 0.2.0 8.0.200   # newest installed patch of that feature band
set -euo pipefail

pkg_dir="$(cd "$1" && pwd)"
version="$2"
sdk="$3"
sdk_major="${sdk%%.*}"
if [[ "$sdk" == *.* ]]; then
  sdk_version="$sdk"; roll_forward="latestPatch"
else
  sdk_version="${sdk}.0.100"; roll_forward="latestFeature"
fi

work="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/noa-smoke.XXXXXX")"
trap 'rm -rf "$work"' EXIT
cd "$work"

# Empty files stop MSBuild from walking up into any parent directory's settings.
echo '<Project />' > Directory.Build.props
echo '<Project />' > Directory.Build.targets
echo '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' > Directory.Packages.props

cat > global.json <<EOF
{ "sdk": { "version": "${sdk_version}", "rollForward": "${roll_forward}", "allowPrerelease": false } }
EOF

# A separate packages folder so a cached copy of the same version is never used.
cat > nuget.config <<EOF
<configuration>
  <config><add key="globalPackagesFolder" value="$work/packages" /></config>
  <packageSources>
    <clear />
    <add key="local" value="$pkg_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="Randactyl.NullObliviousAnalyzer" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF

cat > Smoke.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net${sdk_major}.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Randactyl.NullObliviousAnalyzer" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF

cat > Legacy.cs <<'EOF'
#nullable disable
public class Legacy
{
    public string GetName() => null;
}
EOF

cat > Use.cs <<'EOF'
public static class Use
{
    public static int Length(Legacy legacy) => legacy.GetName().Length;
}
EOF

echo "SDK: $(dotnet --version)"
dotnet build --no-incremental -nologo -v:q | tee build.log
if grep -q 'warning NOA001' build.log; then
  echo "OK: NOA001 reported with SDK $(dotnet --version)"
else
  echo "FAIL: no NOA001 warning with SDK $(dotnet --version)" >&2
  exit 1
fi
