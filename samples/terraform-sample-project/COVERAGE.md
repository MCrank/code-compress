# Terraform Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `TerraformParser`.

## Block Types
- [x] `resource` (two-label: type + name)
- [x] `data` (data sources)
- [x] `variable` (with description, type, default, validation)
- [x] `output` (with description, sensitive)
- [x] `module` (local source, registry source with version)
- [x] `provider` (with nested configuration)
- [x] `terraform` (required_version, required_providers, backend)
- [x] `locals` (simple assignments, nested maps)

## Value Patterns
- [x] String values
- [x] Number values
- [x] Boolean values
- [x] List literals
- [x] Map/object literals
- [x] Complex nested maps (map of maps)
- [x] Heredoc syntax (`<<-EOF`)
- [x] String interpolation (`${...}`)

## Comment Styles
- [x] Hash line comments (`# ...`)
- [x] Double-slash line comments (`// ...`)
- [x] Block comments (`/* ... */`)

## .tfvars
- [x] Key-value assignments
- [x] List values
- [x] Map values

## Other
- [x] Dynamic blocks
- [x] Lifecycle blocks
- [x] Meta-arguments (count, for_each)
- [x] Description enrichment (doc comments)
- [x] Module source dependency tracking
