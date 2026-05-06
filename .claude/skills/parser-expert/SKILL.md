---
name: parser-expert
description: Language parser development expert for CodeCompress. Use this skill whenever adding, modifying, or debugging a language parser — covers tree-sitter AST-based parsing (C#, TypeScript/JS, Python, Go, Rust, Java), regex-based parsing (Luau, Terraform, Blazor), the ILanguageParser strategy pattern, query S-expression syntax, SymbolInfo/DependencyInfo extraction, byte offset tracking, and integration test patterns. Invoke for any parser work: new language support, fixing symbol extraction bugs, adding a new SymbolKind, or updating edge-case handling in an existing parser.
argument-hint: [language-or-parser-file]
disable-model-invocation: true
---

# Parser Expert — CodeCompress

You are a language parser development expert for the CodeCompress project. Guide the implementation, debugging, and testing of language parsers that extract symbols from source files.

For .NET project conventions, see [dotnet-reference.md](../../references/dotnet-reference.md).

## Documentation Lookup Policy (Mandatory)

**Never rely on training data for language grammar rules or tree-sitter node types.** Always verify.

Use the **Context7 MCP** and **Ref MCP** for:
- Language grammar specs (C# spec, Python grammar, Go spec, Rust reference, Java spec)
- TreeSitter.DotNet API documentation
- Tree-sitter node type reference for each language grammar
- .NET Regex API and `[GeneratedRegex]` source generator patterns

## Parser Architecture

### Strategy Pattern

All parsers implement `ILanguageParser`:

```csharp
public interface ILanguageParser
{
    string LanguageId { get; }
    IReadOnlyList<string> FileExtensions { get; }
    ParseResult Parse(string filePath, ReadOnlySpan<byte> content);
}
```

The `IndexEngine` auto-resolves parsers by file extension via DI. Adding a new parser = one class + one DI registration — no other wiring needed.

### ParseResult and Models

```csharp
public sealed record ParseResult(
    IReadOnlyList<SymbolInfo> Symbols,
    IReadOnlyList<DependencyInfo> Dependencies);
```

**SymbolInfo fields:**

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Symbol name (e.g., `ProcessAttack`) |
| `Kind` | `SymbolKind` | Class, Method, Function, Interface, Record, Enum, Constant, Module, Type, Export, ConfigKey |
| `Signature` | `string` | Full declaration (no body) |
| `ParentSymbol` | `string?` | Enclosing symbol name (null for top-level) |
| `ByteOffset` | `int` | Byte position in file — used by `get_symbol`/`expand_symbol` for seek |
| `ByteLength` | `int` | Byte length from declaration to end |
| `LineStart` | `int` | 1-indexed declaration line |
| `LineEnd` | `int` | 1-indexed closing line |
| `Visibility` | `Visibility` | Public, Private, Protected, Internal |
| `DocComment` | `string?` | Doc comment preceding declaration |
| `BodyLineStart` | `int?` | First line of body (after opening brace) — for `expand_symbol` |
| `BodyLineEnd` | `int?` | Last line of body (before closing brace) — for `expand_symbol` |

`ByteOffset` and `ByteLength` are critical — the `get_symbol` and `expand_symbol` tools use them to seek directly to a symbol without re-reading the file. If they're wrong, symbol retrieval breaks silently.

### DI Registration

```csharp
// In ServiceCollectionExtensions.AddCodeCompressCore():
services.AddSingleton<ILanguageParser, MyNewParser>();
```

## Parser Taxonomy

| Approach | When to use | Examples |
|----------|------------|---------|
| **Tree-sitter** | Languages with complex nesting, generics, operators, multi-line constructs | C#, TypeScript/JS, Python, Go, Rust, Java |
| **Regex + state machine** | Simpler scripting languages or config DSLs where token structure is predictable | Luau, Terraform, Blazor (directives) |
| **Structured format library** | Data formats with a dedicated .NET parser | JSON (`JsonDocument`), YAML (`YamlDotNet`), XML (`.csproj` via `XDocument`) |

**Always prefer tree-sitter** for a new programming language. Regex parsers are fragile against edge cases (nested strings, multi-line declarations, operator overloads). Tree-sitter provides an exact AST — no edge case surprises.

## Tree-Sitter Parsers

### Available Built-in Languages

The `TreeSitter.DotNet` package ships pre-compiled grammars. Instantiate with the exact string identifier:

| Language | Constructor argument |
|----------|-------------------|
| C# | `new Language("c-sharp")` |
| TypeScript | `new Language("typescript")` |
| JavaScript | `new Language("javascript")` |
| Python | `new Language("python")` |
| Go | `new Language("go")` |
| Rust | `new Language("rust")` |
| Java | `new Language("java")` |

All `Language`, `Parser`, `Tree`, and `Query` objects are `IDisposable` — always `using`.

### Core Pattern: Parse → Query → Two-Pass Build

Every tree-sitter parser follows this exact structure:

```csharp
public sealed class MyParser : ILanguageParser
{
    // S-expression query — define once as a const
    private const string SymbolQuery = """
        [
          (class_declaration name: (identifier) @name body: (declaration_list) @body) @decl
          (method_declaration name: (identifier) @name body: (block) @body) @decl
          (method_declaration name: (identifier) @name) @decl
        ]
        """;

    public string LanguageId => "my-language";
    public IReadOnlyList<string> FileExtensions { get; } = [".ext"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        // 1. Build AST
        using var language = new Language("my-language");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        var deps = new List<DependencyInfo>();

        ExtractDependencies(tree.RootNode, language, deps);

        // 2. Pass 1 — collect all declarations into a map (byte offset → node tuple)
        //    The same node can appear in multiple matches (e.g., method with and without body).
        //    Merge by keeping the first decl, filling in name/body as they appear.
        var declMap = new Dictionary<int, (Node Decl, Node? Name, Node? Body)>();
        using var symQuery = new Query(language, SymbolQuery);
        foreach (var match in symQuery.Execute(tree.RootNode).Matches)
        {
            Node? decl = null, name = null, body = null;
            foreach (var cap in match.Captures)
            {
                switch (cap.Name)
                {
                    case "decl": decl = cap.Node; break;
                    case "name": name = cap.Node; break;
                    case "body": body = cap.Node; break;
                }
            }
            if (decl is null) continue;

            var key = decl.StartIndex;
            if (!declMap.TryGetValue(key, out var existing))
                declMap[key] = (decl, name, body);
            else
                declMap[key] = (existing.Decl, existing.Name ?? name, existing.Body ?? body);
        }

        // 3. Pass 2 — build SymbolInfo in document order
        foreach (var (_, (decl, nameNode, body)) in declMap.OrderBy(kv => kv.Key))
        {
            var symbolName = nameNode?.Text;
            if (string.IsNullOrEmpty(symbolName)) continue;

            var kind = GetKind(decl, body);
            var byteOffset = decl.StartIndex;
            var byteLength = decl.EndIndex - decl.StartIndex;
            var lineStart = decl.StartPosition.Row + 1;   // tree-sitter rows are 0-indexed
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var vis = DeriveVisibility(decl, parentName is not null);
            var doc = ExtractDocComment(lines, lineStart);

            // BodyLineStart/BodyLineEnd for expand_symbol (skip brace lines)
            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null)
            {
                var bls = body.StartPosition.Row + 2;
                var ble = body.EndPosition.Row;
                if (bls <= ble) { bodyLineStart = bls; bodyLineEnd = ble; }
            }

            symbols.Add(new SymbolInfo(
                Name: symbolName,
                Kind: kind,
                Signature: sig,
                ParentSymbol: parentName,
                ByteOffset: byteOffset,
                ByteLength: byteLength,
                LineStart: lineStart,
                LineEnd: lineEnd,
                Visibility: vis,
                DocComment: doc,
                BodyLineStart: bodyLineStart,
                BodyLineEnd: bodyLineEnd));
        }

        return new ParseResult(symbols, deps);
    }
```

### Query S-Expression Syntax

Tree-sitter queries use S-expressions (Lisp-like syntax). Learn by reading existing parsers, not from memory.

```
; Match a node type, optionally binding field children
(class_declaration name: (identifier) @name body: (declaration_list) @body) @decl

; Wildcard child — any node type
(namespace_declaration name: (_) @name) @decl

; OR: match any of several patterns
[ (class_declaration ...) @decl  (interface_declaration ...) @decl ]

; Nested: match a grandchild pattern
(type_declaration (type_spec name: (type_identifier) @name type: (struct_type) @body)) @decl
```

**Capture names used in this project:**
- `@decl` — the full declaration node (provides byte offsets, line numbers)
- `@name` — the identifier node (provides symbol name via `.Text`)
- `@body` — the body/block node (provides BodyLineStart/End and signature boundary)

**Node type strings** are language-grammar-specific. Always verify against the tree-sitter grammar or by inspecting `node.Type` on a parsed tree. There is no central constants class — each parser hardcodes its own node type strings.

### Node API Reference

```csharp
Node node = ...;

node.Type           // Grammar node type, e.g. "class_declaration"
node.Text           // Source text of this node (UTF-8 decoded)
node.StartIndex     // Byte offset of start (same unit as SymbolInfo.ByteOffset)
node.EndIndex       // Byte offset of end
node.StartPosition.Row  // 0-indexed line number — add 1 for SymbolInfo.LineStart
node.EndPosition.Row    // 0-indexed line number
node.Parent         // Parent node (walk up for parent resolution)
node.Children       // IReadOnlyList<Node> of all children (named + anonymous)
```

**Signature extraction** — slice the byte array directly using offsets:
```csharp
var sig = Encoding.UTF8.GetString(bytes, decl.StartIndex, body.StartIndex - decl.StartIndex).TrimEnd();
```

**Parent resolution** — walk `node.Parent` upward, stopping when you hit a container or stop node:
```csharp
private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
{
    "class_declaration", "struct_declaration", "interface_declaration"
};

private static string? FindParentName(Node decl, Dictionary<int, (Node, Node?, Node?)> declMap)
{
    var current = decl.Parent;
    while (current is not null)
    {
        if (ContainerNodeTypes.Contains(current.Type))
            return declMap.TryGetValue(current.StartIndex, out var e) ? e.Item2?.Text : null;
        current = current.Parent;
    }
    return null;
}
```

### Dependency Extraction

Use a separate query — don't mix into the symbol query:

```csharp
private static void ExtractDependencies(Node root, Language language, List<DependencyInfo> deps)
{
    using var q = new Query(language, "(using_directive) @u");
    foreach (var node in q.Execute(root).Captures.Select(cap => cap.Node))
    {
        // parse node.Text to extract the import path/name
        deps.Add(new DependencyInfo(RequirePath: ..., Alias: null));
    }
}
```

Note: `.Execute().Captures` (flat list) is fine for dependency queries where you just need matching nodes. `.Execute().Matches` (grouped by match) is needed for symbol queries where you need all captures from the same pattern instance together.

## Current Parsers Reference

### Tree-Sitter Parsers

| Parser | Language ID | Extensions | Key node types |
|--------|------------|-----------|---------------|
| `CSharpParser` | `csharp` | `.cs` | `class_declaration`, `method_declaration`, `interface_declaration`, `record_declaration`, `property_declaration`, `namespace_declaration` |
| `TypeScriptJavaScriptParser` | `typescript` / `javascript` | `.ts`, `.tsx`, `.js`, `.jsx` | `class_declaration`, `method_definition`, `function_declaration`, `lexical_declaration`, `interface_declaration` |
| `PythonParser` | `python` | `.py` | `class_definition`, `function_definition`, `decorated_definition` |
| `GoParser` | `go` | `.go` | `function_declaration`, `method_declaration`, `type_declaration`, `const_spec`, `var_spec` |
| `RustParser` | `rust` | `.rs` | `struct_item`, `trait_item`, `function_item`, `impl_item`, `enum_item`, `type_item` |
| `JavaParser` | `java` | `.java` | `class_declaration`, `method_declaration`, `interface_declaration`, `enum_declaration` |

### Regex-Based Parsers

**Luau** (`.luau`, `.lua`) — Regex + line-by-line state machine. Tracks `function`/`end` nesting depth. Doc comments: `---` triple-dash. Dependencies: `require()` calls.

**TerraformParser** (`.tf`, `.tfvars`) — Regex + brace-depth tracking for HCL. Symbol types: resources, data sources, variables, outputs, modules, providers. Doc comments: `#` comments before blocks.
> Gotcha: dotted names like `aws_instance.web` conflict with `GetSymbolByNameAsync`'s `parent.child` splitting logic — use `GetSymbolsByFileAsync` for exact Terraform symbol lookups.

**BlazorRazorParser** (`.razor`) — Regex for `@page`, `@inject`, `@using`, `@inherits` directives; delegates C# code sections to `CSharpParser` instance internally.

### Structured Format Parsers

**DotNetProjectParser** (`.csproj`, `.fsproj`, `.vbproj`, `.props`) — `XDocument` traversal. Extracts package references, project references, build properties.

**JsonConfigParser** (`.json`) — `JsonDocument` traversal. Config keys as symbols with qualified names (e.g., `ConnectionStrings.Default`).

**YamlConfigParser** (`.yaml`, `.yml`) — `YamlDotNet` `YamlStream`. Same key-as-symbol approach as JSON.

## Adding a New Tree-Sitter Parser

### Step 1: Explore the grammar (mandatory before writing a single query)

Tree-sitter node type names vary per language and must be exact. To discover them:
1. Use Context7/Ref MCP to find the tree-sitter grammar for the language
2. Parse a small sample file and inspect `tree.RootNode` children to see actual node types
3. Cross-reference with the language's tree-sitter grammar repository

### Step 2: Write the S-expression query

Cover all symbol kinds you intend to extract. For each, capture `@decl`, `@name`, and optionally `@body`. Use the OR bracket syntax `[...]` when multiple node types map to the same kind.

### Step 3: Implement `GetKind(Node decl, Node? body)`

Map language node types to `SymbolKind` via a switch expression:

```csharp
private static SymbolKind GetKind(Node decl, Node? body) => decl.Type switch
{
    "class_definition" => SymbolKind.Class,
    "function_definition" when /* top-level */ => SymbolKind.Function,
    "function_definition" => SymbolKind.Method,
    _ => SymbolKind.Function
};
```

### Step 4: Handle visibility

Visibility rules differ per language:
- **Go:** capitalized name = `Public`, lowercase = `Private`
- **Python:** leading `_` = `Private`, `__` = `Private`, otherwise `Public`
- **Rust:** explicit `pub`/`pub(crate)` = `Public`, omitted = `Private`
- **Java/C#:** explicit keyword required

### Step 5: Doc comment extraction

Extract from the `lines` array, walking backwards from `lineStart - 1`:
- Look for consecutive comment lines (language-specific prefix)
- Stop at the first non-comment line

### Step 6: Add to DI + write tests + add sample project

See "Sample Project + Integration Test Pattern" section below.

## Regex Parser Patterns (for Luau/Terraform-style parsers)

When tree-sitter isn't suitable, use source-generated regex:

```csharp
[GeneratedRegex(@"^(?<vis>public|private|protected|internal)\s+(?<kind>class|interface|record)\s+(?<name>\w+)",
    RegexOptions.Multiline)]
private static partial Regex TypeDeclarationRegex();
```

**Byte offset for regex matches:** Convert the character-indexed `match.Index` to a byte offset using:
```csharp
var byteOffset = Encoding.UTF8.GetByteCount(text[..match.Index]);
```

**Nesting tracking:** For brace-based languages, track `{`/`}` depth while skipping:
- String literals (`"..."`, `@"..."`, `$"..."`)
- Character literals (`'{'`)
- Comments (`// ...`, `/* ... */`)

## Sample Project + Integration Test Pattern

Every new parser requires both. These are not optional — they're enforced by `implement-plan`.

### Sample Project — `samples/{language}-sample-project/`

- Realistic code that looks like a real project, not a minimal fixture
- Cover **all symbol kinds** the parser handles
- Include **edge cases**: nested blocks, strings with special chars, multi-line declarations, decorators
- Self-contained — no external dependencies needed to parse

### Integration Tests — `tests/CodeCompress.Integration.Tests/{Language}EndToEndTests.cs`

```csharp
internal sealed class PythonEndToEndTests
{
    [Test]
    public async Task IndexPythonSampleProject() { /* correct file/symbol count */ }

    [Test]
    public async Task OutlineContainsAllSymbolKinds() { /* all SymbolKind values appear */ }

    [Test]
    public async Task SpecificSymbolHasCorrectMetadata() { /* known symbol has right Kind, Visibility, DocComment */ }

    [Test]
    public async Task ByteOffsetsAreAccurate() { /* seek to offset, read bytes, verify content matches symbol text */ }

    [Test]
    public async Task SearchFindsSymbols() { /* FTS5 search returns expected results */ }

    [Test]
    public async Task DependenciesAreTracked() { /* import/require edges appear in dependency graph */ }
}
```

**Byte offset accuracy test** is particularly important — it catches off-by-one errors that would silently break `get_symbol`:
```csharp
var symbol = result.Symbols.First(s => s.Name == "MyClass");
var slice = Encoding.UTF8.GetString(bytes, symbol.ByteOffset, symbol.ByteLength);
await Assert.That(slice).Contains("class MyClass");
```

## Sub-Agent Context Requirements

When this skill is invoked as a sub-agent, the caller must provide:

1. **The `ILanguageParser` interface** — full definition
2. **A complete existing tree-sitter parser** (e.g., `GoParser.cs` or `CSharpParser.cs` full source)
3. **The `SymbolInfo` / `ParseResult` / `DependencyInfo` model definitions**
4. **The target language's grammar** — node type names for relevant constructs
5. **Sample source files** in the target language for the sample project
6. **Language-specific edge cases** to handle (visibility rules, doc comment style, dependency syntax)
7. **An example integration test** (e.g., `CSharpEndToEndTests.cs`)
