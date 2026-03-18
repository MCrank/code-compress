# C# Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `CSharpParser`.

## Type Declarations
- [x] Class (public, abstract, static, sealed)
- [x] Interface (simple, generic with constraints)
- [x] Record (positional with primary constructor)
- [x] Record struct (readonly record struct)
- [x] Struct (with operator overloads)
- [x] Enum (top-level, nested, with [Flags])
- [x] Partial type (record split across two files)
- [x] File-scoped type (`file class`)
- [x] Class primary constructor (C# 12+)
- [x] Inheritance / interface implementation
- [x] Generic type with constraints (`where T : class`)
- [ ] Delegate (sample exists but parser does not index delegates)
- [ ] Nested class (only nested enum currently)

## Members
- [x] Methods (public, private, async, expression-bodied, virtual, override)
- [x] Constructors (regular, primary)
- [x] Properties (auto-prop, expression-bodied, init-only)
- [x] Primary constructor parameters (indexed as Constant children of record/class)
- [x] Indexer (`this[]`)
- [x] Finalizer (`~ClassName`)
- [x] Operator overloads (single-line expression-bodied)
- [ ] Events (sample exists but parser does not index events)

## Namespaces
- [x] File-scoped namespace (`namespace Foo;`)
- [x] Block-scoped namespace (`namespace Foo { }`)

## Visibility
- [x] Public
- [x] Internal (default)
- [x] Private
- [x] File (`file class` maps to Private)

## Other
- [x] XML doc comments (`///`)
- [x] Using statements (dependency tracking)
- [x] Nested types (enum inside class)

## Known Parser Gaps (sample provides constructs, but parser does not index them)
- Delegates — no dedicated matcher
- Events — no dedicated matcher
- Multi-line expression-bodied operators — only single-line operators are indexed
