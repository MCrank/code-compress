# JSON Config Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `JsonConfigParser`.

## Value Types
- [x] String values
- [x] Number values (integer, float)
- [x] Boolean values (true, false)
- [x] Null values
- [x] Object/section values (with key count in signature)
- [x] Array values (with item count in signature)
- [x] Empty object (`{}`)
- [x] Empty array (`[]`)

## Structural Features
- [x] Top-level keys (null parent)
- [x] Nested objects (colon-separated qualified names)
- [x] Deep nesting (4+ levels)
- [x] Parent-child relationships
- [x] Multiple files (appsettings, development overrides, i18n)

## UTF-8 / Multi-byte
- [x] Multi-byte property values (Japanese characters)
- [x] Multi-byte property KEYS (Japanese, accented Latin)
- [x] Accented characters (Spanish)
- [x] Emoji characters in values
- [x] Byte offset accuracy across multi-byte sequences (tree-sitter AST-derived)
- [x] Repeated property names in different sections (qualified name disambiguation)

## Realistic Patterns
- [x] .NET appsettings.json (Logging, ConnectionStrings, Authentication)
- [x] Feature flags (boolean config)
- [x] Environment-specific overrides
- [x] Internationalization config
