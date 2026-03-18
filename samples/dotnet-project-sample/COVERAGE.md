# .NET Project Sample — Parser Construct Coverage

Constructs exercised by this sample project against `DotNetProjectParser`.

## Project Properties
- [x] TargetFramework (single)
- [x] TargetFrameworks (multi-target: net8.0;net10.0)
- [x] LangVersion
- [x] OutputType
- [x] RootNamespace
- [x] Nullable
- [x] ImplicitUsings
- [x] AssemblyName

## Package References
- [x] PackageReference with Version attribute
- [x] PackageReference without Version (centrally managed)
- [x] PackageReference with nested `<Version>` element

## Package Versions (Directory.Packages.props)
- [x] PackageVersion with Include and Version

## Project References
- [x] ProjectReference with Include path
- [x] Dependency graph edge from ProjectReference

## Other
- [x] Central package management (`ManagePackageVersionsCentrally`)
