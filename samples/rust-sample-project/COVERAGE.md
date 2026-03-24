# Rust Sample Project — Parser Coverage

## Type Declarations
- [x] Struct (pub, non-pub)
- [x] Enum (with variants)
- [x] Trait (with method signatures)
- [x] Trait with bounds (: Identifiable)
- [x] Type alias (pub type)

## Functions & Methods
- [x] Top-level functions (pub, non-pub)
- [x] impl block methods (pub, non-pub)
- [x] impl Trait for Type methods
- [x] Generic functions
- [x] Methods with lifetime parameters
- [x] Static methods (no self)
- [x] &self and &mut self methods

## Constants & Statics
- [x] pub const
- [x] Non-pub const
- [x] pub(crate) static

## Modules
- [x] pub mod declarations
- [x] pub use re-exports

## Visibility
- [x] pub → Public
- [x] pub(crate) → Private
- [x] No modifier → Private

## Documentation
- [x] /// doc comments (single-line)
- [x] Multi-line /// doc comment blocks

## Dependencies
- [x] use statements
- [x] use crate:: paths
- [x] use std:: paths

## Macros
- [x] macro_rules! definitions

## Derive & Attributes
- [x] #[derive(...)] on structs/enums

## Known Gaps
- [ ] Procedural macro implementations
- [ ] Unsafe blocks as symbols
- [ ] Feature-gated code
- [ ] Cargo.toml parsing
