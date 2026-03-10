# 001 — C# Parser: Core Patterns (Namespace, Class, Interface, Record, Enum, Struct)

## Summary

Phase 2 language parser for C# source files. Regex/pattern-based extraction of top-level and nested type declarations — namespaces, classes, interfaces, records, enums, and structs. Handles access modifiers, generic type parameters, inheritance, XML doc comments, and nested type parent tracking.

## Dependencies

- **Feature 01** — Project scaffold, `CodeCompress.Core.csproj`.
- **Feature 02** — Core models and interfaces (`ILanguageParser`, `SymbolInfo`, `ParseResult`, `SymbolKind`, `Visibility`).

## Scope

### 1. CSharpParser Class (`src/CodeCompress.Core/Parsers/CSharpParser.cs`)

Implements `ILanguageParser`:

| Property/Method | Detail |
|---|---|
| `LanguageId` | `"csharp"` |
| `FileExtensions` | `[".cs"]` |
| `Parse(string filePath, ReadOnlySpan<byte> content)` | Returns `ParseResult` with extracted symbols and dependencies |

### 2. Type Declaration Patterns

| Pattern | Regex Target | SymbolKind | Notes |
|---|---|---|---|
| Namespace (file-scoped) | `namespace Foo.Bar;` | `Module` | Single per file; no brace tracking needed |
| Namespace (block-scoped) | `namespace Foo.Bar { }` | `Module` | Brace-depth tracking for body extent |
| Class | `[modifiers] class Name[<T>] [: Base, IFoo]` | `Class` | Includes generic parameters and constraints |
| Interface | `[modifiers] interface IName[<T>] [: IBase]` | `Interface` | |
| Record | `[modifiers] record [class\|struct] Name(params)` | `Class` | Primary constructor captured in signature |
| Enum | `[modifiers] enum Name [: Type]` | `Type` | Underlying type captured if specified |
| Struct | `[modifiers] struct Name[<T>]` | `Class` | |

### 3. Modifier Detection

Modifiers parsed from the declaration line: `public`, `internal`, `private`, `protected`, `static`, `abstract`, `sealed`, `partial`, `readonly`, `file`, `required`, `new`.

### 4. Visibility Derivation

| Modifier(s) | Visibility |
|---|---|
| `public` | `Public` |
| `internal` | `Public` (visible within project) |
| `protected internal` | `Public` |
| `private` | `Private` |
| `protected` | `Private` |
| `private protected` | `Private` |
| No modifier (top-level) | `Public` (C# default for top-level types is `internal`, mapped to `Public`) |
| No modifier (nested) | `Private` (C# default for nested types) |

### 5. Parent Tracking

Nested types (class within class, enum within class, etc.) have their `ParentSymbol` set to the enclosing type's name. Tracked via a brace-depth stack during parsing — when entering a type's body, it is pushed onto the parent stack; when exiting, it is popped.

### 6. Symbol Capture Fields

| Field | Source |
|---|---|
| `Name` | Type name (without generic parameters for matching, with parameters in signature) |
| `Kind` | As mapped above |
| `Signature` | Full declaration line(s), trimmed, up to opening brace or semicolon |
| `ByteOffset` | Byte position of the declaration start in the file |
| `ByteLength` | Byte length from declaration start to closing brace (brace-depth tracking) |
| `LineStart` | 1-based line number of the declaration |
| `LineEnd` | 1-based line number of the closing brace |
| `Visibility` | Derived from modifiers as above |
| `DocComment` | `///` XML comments immediately preceding the declaration, joined into a single string |

### 7. Brace-Depth Tracking

A state machine tracks brace depth to determine symbol body boundaries:
- Increment on `{`, decrement on `}`.
- Skip braces inside string literals (`"..."`, `@"..."`, `$"..."`, `"""..."""`), character literals, and comments (`//`, `/* */`).
- When depth returns to the level before the type declaration, the type's body ends.

### 8. Tests (`tests/CodeCompress.Core.Tests/Parsers/CSharpParserTests.cs`)

| Test | Description |
|---|---|
| FileScopedNamespace_Extracted | `namespace Foo.Bar;` parsed as Module |
| BlockScopedNamespace_Extracted | `namespace Foo.Bar { }` parsed as Module with correct extent |
| PublicClass_WithBaseAndInterfaces | `public class Foo : Bar, IBaz` — Name, Kind, Signature, Visibility |
| InternalClass_VisibilityPublic | `internal class Foo` — Visibility is Public |
| AbstractClass_Extracted | `abstract class Foo` — modifiers in signature |
| SealedClass_Extracted | `sealed class Foo` — modifiers in signature |
| StaticClass_Extracted | `static class Foo` — modifiers in signature |
| PartialClass_Extracted | `partial class Foo` — modifiers in signature |
| Interface_WithIPrefix | `public interface IFoo` — Kind is Interface |
| Record_WithPrimaryConstructor | `public record Foo(int X, string Y)` — constructor in signature |
| RecordStruct_Extracted | `public record struct Point(int X, int Y)` — Kind is Class |
| Enum_Extracted | `public enum Color { Red, Green, Blue }` — Kind is Type |
| Enum_WithUnderlyingType | `public enum Flags : byte` — underlying type in signature |
| Struct_Extracted | `public struct Vector3` — Kind is Class |
| GenericClass_WithConstraints | `public class Repo<T> where T : IEntity` — generics in signature |
| NestedClass_ParentSet | Class inside class — ParentSymbol is outer class name |
| NestedEnum_ParentSet | Enum inside class — ParentSymbol is enclosing class name |
| XmlDocComment_Captured | `///` comments before a class are captured in DocComment |
| MultiLineXmlDoc_Captured | Multiple `///` lines joined correctly |
| PrivateNestedClass_VisibilityPrivate | No modifier on nested class — Visibility is Private |
| EmptyFile_ReturnsEmptyResult | Empty `.cs` file — ParseResult with no symbols |
| CommentOnlyFile_ReturnsEmptyResult | File with only comments — no symbols extracted |
| MultipleNamespaces_BlockScoped | File with multiple block-scoped namespaces — both extracted |
| GenericInterface_Extracted | `interface IRepo<T>` — generics captured |

## Acceptance Criteria

- [ ] `CSharpParser` implements `ILanguageParser` with `LanguageId = "csharp"` and `FileExtensions = [".cs"]`.
- [ ] File-scoped and block-scoped namespaces are extracted as `SymbolKind.Module`.
- [ ] Classes, interfaces, records, enums, and structs are extracted with correct `SymbolKind`.
- [ ] Generic type parameters and constraints are captured in the signature.
- [ ] Inheritance (base class, interfaces) is captured in the signature.
- [ ] Access modifiers are parsed and mapped to `Visibility` correctly.
- [ ] Nested types have `ParentSymbol` set to the enclosing type.
- [ ] XML doc comments (`///`) are captured in `DocComment`.
- [ ] Brace-depth tracking correctly determines `ByteLength`, `LineStart`, `LineEnd`.
- [ ] Braces inside strings, character literals, and comments are ignored by the depth tracker.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Core.Tests`.
- [ ] 95%+ code coverage for `CSharpParser` core patterns.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Core/Parsers/CSharpParser.cs` | C# language parser implementing `ILanguageParser` |
| `tests/CodeCompress.Core.Tests/Parsers/CSharpParserTests.cs` | Unit tests for core C# patterns |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Server/Program.cs` | Register `CSharpParser` in DI: `AddSingleton<ILanguageParser, CSharpParser>()` |

## Out of Scope

- Methods, properties, constructors — covered in 12-002.
- Using statements as dependencies — covered in 12-002.
- Roslyn-based parsing — regex/pattern-based only per project architecture.
- Preprocessor directives (`#if`, `#region`) — not tracked.
- Attribute extraction — attributes on types are not captured as separate symbols.

## Notes / Decisions

1. **Regex-based, not Roslyn.** Per project architecture, all parsers are regex/pattern-based for zero-dependency, cross-platform operation. This means some edge cases (deeply nested generics, complex preprocessor branches) may not be perfectly handled. The parser targets ~95% accuracy on real-world C# code.
2. **Record mapped to Class.** Records are semantically classes in C# and are mapped to `SymbolKind.Class`. The signature preserves the `record` keyword so consumers can distinguish them.
3. **Visibility mapping.** `internal` is mapped to `Public` because within a CodeCompress project context, internal types are part of the project's API surface. The distinction between `public` and `internal` is less meaningful when the goal is to show "what symbols exist in this codebase."
4. **Brace-depth state machine.** This is the most complex part of the parser. It must handle string interpolation (`$"...{expr}..."`), raw string literals (`"""..."""`), verbatim strings (`@"..."`), and their combinations. Edge cases should be covered by specific tests.
