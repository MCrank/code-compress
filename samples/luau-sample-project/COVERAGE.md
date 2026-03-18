# Luau Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `LuauParser`.

## Symbol Types
- [x] Module class (`local X = {} :: Type`)
- [x] Methods (`function Class:Method()`)
- [x] Local functions (`local function name()`)
- [x] Top-level functions (`function name()`)
- [x] Module exports (`return ClassName`)

## Control Flow (nesting tracking)
- [x] `if/then/elseif/else/end` blocks
- [x] `for/do/end` loops
- [x] `while/do/end` loops
- [x] `repeat/until` loops
- [x] Standalone `do/end` blocks (scope isolation)

## Function Patterns
- [x] Methods with type annotations
- [x] Functions with return type annotations
- [x] Nested local functions (function inside function)
- [x] Multiple methods per module

## Other
- [x] Comment lines (not extracted as doc comments)
- [x] Service pattern (module table + methods + return)
