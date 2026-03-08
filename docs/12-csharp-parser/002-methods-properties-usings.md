# 002 — C# Parser: Methods, Properties, Using Statements, Nested Types

## Summary

Extends the C# parser with extraction of methods (including constructors, operators, and extension methods), properties (auto, expression-bodied, init-only), and using statements (as dependency information). Handles complex real-world patterns such as async methods, generic methods with constraints, attributes on members, nullable return types, and tuple returns.

## Dependencies

- **Feature 12-001** — `CSharpParser` with core type patterns, brace-depth tracking, modifier detection, and visibility derivation.

## Scope

### 1. Method Extraction

| Pattern | Regex Target | SymbolKind | Notes |
|---|---|---|---|
| Regular method | `[modifiers] ReturnType Name(params)` | `Method` | Includes body via brace-depth tracking |
| Async method | `async Task<T> Name(params)` | `Method` | `async` in modifiers, full return type in signature |
| Generic method | `T Name<U>(params) where U : IFoo` | `Method` | Generic params and constraints in signature |
| Constructor | `ClassName(params)` | `Method` | Detected by name matching enclosing type |
| Static constructor | `static ClassName()` | `Method` | |
| Finalizer | `~ClassName()` | `Method` | |
| Operator | `static ReturnType operator +(Type a, Type b)` | `Method` | `operator` keyword in name |
| Indexer | `Type this[int index]` | `Method` | |
| Expression-bodied | `ReturnType Name(params) => expr;` | `Method` | Body is the expression |
| Extension method | `static ReturnType Name(this Type param, ...)` | `Method` | `this` keyword on first parameter noted |
| Void method | `void Name(params)` | `Method` | |

Method extraction rules:
- Methods are children of the enclosing type — `ParentSymbol` set via the brace-depth parent stack.
- Attributes (`[HttpGet]`, `[Obsolete]`, etc.) on lines immediately preceding the method are skipped — the signature starts at the method declaration line, not the attribute.
- Visibility derived from modifiers, defaulting to `Private` for members within a type.

### 2. Property Extraction

| Pattern | Regex Target | SymbolKind | Notes |
|---|---|---|---|
| Auto-property | `[modifiers] Type Name { get; set; }` | `Constant` | Accessor pattern detected |
| Init-only | `[modifiers] Type Name { get; init; }` | `Constant` | |
| Expression-bodied | `[modifiers] Type Name => expr;` | `Constant` | Arrow syntax |
| Full property | `[modifiers] Type Name { get { } set { } }` | `Constant` | Brace-depth tracking for body |
| Required property | `required Type Name { get; set; }` | `Constant` | `required` modifier captured |

Property detection heuristic: a line matching `[modifiers] Type Identifier { get` or `[modifiers] Type Identifier =>` that is not a method (no parentheses before `{` or `=>`).

### 3. Using Statements as Dependencies

| Pattern | DependencyInfo | Notes |
|---|---|---|
| `using Namespace;` | `ModulePath = "Namespace"` | Standard using |
| `using Alias = Type;` | `ModulePath = "Type"`, alias captured | Using alias |
| `global using Namespace;` | `ModulePath = "Namespace"`, global flag | Global using |
| `using static Namespace.Type;` | `ModulePath = "Namespace.Type"`, static flag | Static using |

Using statements are captured as `DependencyInfo` entries on the `ParseResult`, enabling the dependency graph to show namespace/type relationships between C# files.

### 4. Edge Cases and Complex Patterns

| Pattern | Handling |
|---|---|
| Attributes before methods | Attribute lines (`[...]`) are not part of the method signature; parser skips them |
| Generic methods with constraints | `where T : class, new()` captured in signature |
| Nullable return types | `string?`, `Task<int?>` — `?` included in return type |
| Tuple return types | `(int X, string Y)` — parenthesized tuple in return type |
| Extension methods | `this` keyword on first parameter — noted in signature |
| Nested type methods | Methods inside nested classes have correct parent chain |
| Partial methods | Each declaration captured independently |
| `new` modifier on methods | `new void DoSomething()` — modifier captured |
| Explicit interface impl | `void IFoo.Bar()` — interface name included in method name |

### 5. Tests (`tests/CodeCompress.Core.Tests/Parsers/CSharpParserTests.cs` — additional tests)

| Test | Description |
|---|---|
| PublicMethod_Extracted | `public void DoSomething()` — Name, Kind, Signature, Visibility |
| PrivateMethod_VisibilityPrivate | `private int Calculate()` — Visibility is Private |
| ProtectedMethod_Extracted | `protected virtual void OnEvent()` — modifiers in signature |
| AsyncMethod_TaskReturn | `public async Task<int> GetAsync()` — async and return type captured |
| GenericMethod_WithConstraints | `public T Find<T>(int id) where T : class` — generics in signature |
| Constructor_Extracted | `public MyClass(int x, string y)` — Kind is Method, name is class name |
| StaticConstructor_Extracted | `static MyClass()` — captured correctly |
| Finalizer_Extracted | `~MyClass()` — captured correctly |
| OperatorOverload_Extracted | `public static MyClass operator +(...)` — operator in name |
| Indexer_Extracted | `public int this[int i]` — `this[]` captured |
| ExpressionBodiedMethod_Extracted | `public int Sum() => X + Y;` — body is expression |
| ExtensionMethod_ThisParameter | `public static int Count(this IEnumerable<T> source)` — `this` noted |
| VoidMethod_Extracted | `public void Execute()` — void return type |
| AutoProperty_Extracted | `public string Name { get; set; }` — Kind is Constant |
| InitOnlyProperty_Extracted | `public string Name { get; init; }` — init captured |
| ExpressionBodiedProperty_Extracted | `public int Total => Items.Count;` — arrow syntax |
| RequiredProperty_Extracted | `required string Name { get; set; }` — required modifier |
| UsingStatement_AsDependency | `using System.Collections.Generic;` — DependencyInfo created |
| GlobalUsing_AsDependency | `global using System.Linq;` — global flag set |
| UsingAlias_AsDependency | `using Dict = System.Collections.Generic.Dictionary<string, int>;` — alias captured |
| UsingStatic_AsDependency | `using static System.Math;` — static flag set |
| MethodWithAttributes_AttributeSkipped | `[HttpGet] public IActionResult Get()` — signature starts at method, not attribute |
| NullableReturnType_Captured | `public string? GetName()` — `?` in return type |
| TupleReturnType_Captured | `public (int X, string Y) GetPair()` — tuple in return type |
| NestedTypeMethod_CorrectParent | Method in nested class — ParentSymbol is `OuterClass.InnerClass` |
| ExplicitInterfaceImpl_Captured | `void IDisposable.Dispose()` — interface name in method name |
| ComplexRealWorldFile_AllSymbolsExtracted | A realistic C# file with mixed patterns — all symbols found |

## Acceptance Criteria

- [ ] Methods are extracted with correct Name, Kind (`Method`), Signature, Visibility, and parent tracking.
- [ ] Constructors, static constructors, finalizers, operators, and indexers are all captured as `Method`.
- [ ] Async methods preserve `async` and full return type (`Task<T>`) in signature.
- [ ] Generic methods with constraints are captured completely.
- [ ] Expression-bodied methods and properties are handled.
- [ ] Properties are extracted as `SymbolKind.Constant` with accessor pattern noted.
- [ ] Using statements are captured as `DependencyInfo` with correct module path and flags.
- [ ] Attributes on lines before methods are not included in the method signature.
- [ ] Nullable return types, tuple returns, and extension methods are handled.
- [ ] Nested type members have the correct parent chain.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Core.Tests`.
- [ ] 95%+ cumulative code coverage for `CSharpParser`.

## Files to Create/Modify

### Create

None — all additions are to existing files.

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Core/Parsers/CSharpParser.cs` | Add method, property, and using statement extraction patterns |
| `tests/CodeCompress.Core.Tests/Parsers/CSharpParserTests.cs` | Add tests for methods, properties, usings, and edge cases |

## Out of Scope

- Field extraction (`private int _count;`) — fields are not tracked as symbols for MVP.
- Event extraction (`public event EventHandler Changed;`) — not tracked for MVP.
- Delegate extraction (`public delegate void Handler(int x);`) — not tracked for MVP.
- Local functions inside methods — only top-level members of types are extracted.
- Lambda expressions — not tracked as symbols.
- LINQ query expressions — not tracked.

## Notes / Decisions

1. **Properties as Constant.** `SymbolKind` does not have a `Property` variant. Mapping properties to `Constant` is a pragmatic choice — both represent named values on a type. The signature distinguishes them (properties have `{ get; set; }` or `=>`).
2. **Attribute skipping.** Attributes are detected by `[` at the start of a line (after whitespace). Multiple attribute lines are skipped until the actual declaration line is found. This heuristic may misfire on array initializers at class level, but that is rare enough to be acceptable.
3. **Using statements as dependencies.** This is a coarser dependency signal than Luau's `require()` — C# usings reference namespaces, not files. However, it still provides useful information for the dependency graph (which namespaces a file depends on). File-to-file resolution would require Roslyn and is out of scope.
4. **Extension method detection.** The `this` keyword on the first parameter is a heuristic. The parser checks for `this` followed by a type name as the first parameter. This is sufficient for real-world code.
5. **Explicit interface implementations.** `void IFoo.Bar()` has no access modifier and is always public (implicitly). The parser detects the dotted name pattern and captures `IFoo.Bar` as the method name.
