# Java Sample Project — Parser Coverage

## Type Declarations
- [x] Class (public, abstract, final, sealed)
- [x] Interface (with default methods)
- [x] Enum (with methods)
- [x] Record (Java 16+)
- [x] Annotation type (@interface)
- [x] Inner class (static, non-static)
- [x] Inner enum

## Members
- [x] Methods (public, private, protected, package-private)
- [x] Constructors
- [x] Static methods
- [x] Generic methods
- [x] Methods with throws clause
- [x] Static final constants

## Modifiers & Visibility
- [x] public
- [x] protected
- [x] private
- [x] package-private (no modifier)
- [x] abstract
- [x] final
- [x] static
- [x] synchronized (in methods)

## Generics
- [x] Generic class/interface declarations
- [x] Generic method declarations
- [x] Bounded type parameters (extends)

## Documentation
- [x] Javadoc comments (/** ... */)
- [x] Multi-line Javadoc with @param, @return, @throws

## Dependencies
- [x] import statements
- [x] import static statements
- [x] Wildcard imports (java.util.*)
- [x] Package declarations

## Annotations
- [x] @Override, @Deprecated on methods
- [x] @interface annotation type declarations
- [x] Annotations with parameters

## Edge Cases
- [x] Private constructor (utility class)
- [x] Implements multiple interfaces
- [x] Extends + implements on same class
- [x] Nested generics (List<Map<String, Object>>)

## Known Gaps
- [ ] Lambda expressions as standalone symbols
- [ ] Anonymous class instances
- [ ] Module declarations (module-info.java)
- [ ] Text blocks (Java 13+)
- [ ] Pattern matching variables
