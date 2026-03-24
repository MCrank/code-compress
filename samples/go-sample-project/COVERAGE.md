# Go Sample Project — Parser Coverage

## Type Declarations
- [x] Struct (exported and unexported)
- [x] Interface (with method signatures)
- [x] Generic interface (type parameters)
- [x] Type alias (type Name = Other)
- [x] Named type (type Role int)

## Functions & Methods
- [x] Top-level functions (exported and unexported)
- [x] Generic functions (type parameters)
- [x] Methods with pointer receiver (func (r *Type) Name())
- [x] Methods with value receiver (func (r Type) Name())
- [x] Multiple return values
- [x] Named return values
- [x] Error return pattern

## Constants & Variables
- [x] Single const declaration
- [x] Const block with iota
- [x] Exported const
- [x] Unexported const
- [x] Var declarations (exported)
- [x] Var declarations (unexported)

## Visibility
- [x] Exported (uppercase first letter) → Public
- [x] Unexported (lowercase first letter) → Private

## Documentation
- [x] Single-line // comments above declarations
- [x] Multi-line // comment blocks

## Dependencies
- [x] Single import
- [x] Grouped import block
- [x] Standard library imports
- [x] Third-party module imports

## Generics (Go 1.18+)
- [x] Generic function declarations
- [x] Generic interface declarations
- [x] Type constraints (comparable, any)

## Known Gaps
- [ ] Build tags / build constraints
- [ ] CGo constructs
- [ ] Generated code detection
- [ ] go.mod parsing
