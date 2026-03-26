# YAML Config Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `YamlConfigParser`.

## Value Types
- [x] String values (quoted and unquoted)
- [x] Number values (integer, float)
- [x] Boolean values (true, false)
- [x] Null values (explicit `null`)
- [x] Mapping values (with key count in signature)
- [x] Sequence values (with item count in signature)
- [x] Empty mapping (`{}`)
- [x] Empty sequence (`[]`)

## Structural Features
- [x] Top-level keys (null parent)
- [x] Nested mappings (colon-separated qualified names)
- [x] Deep nesting (4+ levels via node_pools labels)
- [x] Parent-child relationships
- [x] Inline mappings (flow style `{key: value}`)
- [x] Multiple files (settings.yaml, docker-compose.yml, i18n.yaml)

## Array Handling
- [x] Scalar arrays (summarized with item count)
- [x] Object arrays (items indexed with zero-based index)
- [x] Nested objects within array items
- [x] Array items with varying structure

## UTF-8 / Multi-byte
- [x] Multi-byte property values (Japanese characters)
- [x] Accented characters (Spanish, German)
- [x] Emoji characters in values
- [x] Byte offset accuracy across multi-byte sequences

## YAML-Specific Features
- [x] Comments (block and inline)
- [x] Both `.yaml` and `.yml` file extensions

## Realistic Patterns
- [x] Kubernetes cluster settings
- [x] Docker Compose services
- [x] Internationalization config
- [x] Network CIDR configurations
- [x] Resource tags/labels
