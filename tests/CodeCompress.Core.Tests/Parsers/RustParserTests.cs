using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class RustParserTests
{
    private readonly RustParser _parser = new();

    private ParseResult Parse(string code) =>
        _parser.Parse("test.rs", Encoding.UTF8.GetBytes(code));

    [Test]
    public async Task LanguageIdIsRust()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("rust");
    }

    [Test]
    public async Task FileExtensionsContainsRs()
    {
        await Assert.That(_parser.FileExtensions).Contains(".rs");
    }

    [Test]
    public async Task EmptyContentReturnsEmpty()
    {
        var result = _parser.Parse("test.rs", ReadOnlySpan<byte>.Empty);
        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Use Imports ───────────────────────────────────────────────────

    [Test]
    public async Task ParsesUseStatement()
    {
        var result = Parse("use std::collections::HashMap;");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("std::collections::HashMap");
    }

    [Test]
    public async Task ParsesUseWithGlob()
    {
        var result = Parse("use std::io::*;");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
    }

    // ── Structs ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPubStruct()
    {
        var code = """
            pub struct User {
                id: String,
            }
            """;
        var result = Parse(code);
        var s = result.Symbols.First(sym => sym.Name == "User");
        await Assert.That(s.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(s.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesStructWithDerive()
    {
        var code = """
            #[derive(Debug, Clone)]
            pub struct Config {
                value: String,
            }
            """;
        var result = Parse(code);
        var s = result.Symbols.First(sym => sym.Name == "Config");
        await Assert.That(s.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(s.Signature).Contains("#[derive(Debug, Clone)]");
    }

    // ── Enums ─────────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPubEnum()
    {
        var code = """
            pub enum Role {
                Guest,
                Admin,
            }
            """;
        var result = Parse(code);
        var e = result.Symbols.First(sym => sym.Name == "Role");
        await Assert.That(e.Kind).IsEqualTo(SymbolKind.Enum);
    }

    // ── Traits ────────────────────────────────────────────────────────

    [Test]
    public async Task ParsesTrait()
    {
        var code = """
            pub trait Identifiable {
                fn id(&self) -> &str;
            }
            """;
        var result = Parse(code);
        var t = result.Symbols.First(sym => sym.Name == "Identifiable");
        await Assert.That(t.Kind).IsEqualTo(SymbolKind.Interface);
    }

    [Test]
    public async Task ParsesTraitWithBound()
    {
        var code = """
            pub trait Auditable: Identifiable {
                fn audit_id(&self) -> String;
            }
            """;
        var result = Parse(code);
        var t = result.Symbols.First(sym => sym.Name == "Auditable");
        await Assert.That(t.Signature).Contains("Identifiable");
    }

    // ── Functions ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPubFunction()
    {
        var code = """
            pub fn is_valid(s: &str) -> bool {
                !s.is_empty()
            }
            """;
        var result = Parse(code);
        var f = result.Symbols.First(sym => sym.Name == "is_valid");
        await Assert.That(f.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(f.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesPrivateFunction()
    {
        var code = """
            fn helper() -> String {
                String::new()
            }
            """;
        var result = Parse(code);
        var f = result.Symbols.First(sym => sym.Name == "helper");
        await Assert.That(f.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ParsesGenericFunction()
    {
        var code = """
            pub fn audit<T: Auditable>(entity: &T) {
                println!("{}", entity.audit_id());
            }
            """;
        var result = Parse(code);
        var f = result.Symbols.First(sym => sym.Name == "audit");
        await Assert.That(f.Kind).IsEqualTo(SymbolKind.Function);
    }

    // ── Impl Methods ──────────────────────────────────────────────────

    [Test]
    public async Task ParsesImplMethod()
    {
        var code = """
            impl User {
                pub fn new(id: &str) -> Self {
                    User { id: id.to_string() }
                }
            }
            """;
        var result = Parse(code);
        var m = result.Symbols.First(sym => sym.Name == "new");
        await Assert.That(m.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(m.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesTraitImplMethod()
    {
        var code = """
            impl Identifiable for User {
                fn id(&self) -> &str {
                    &self.id
                }
            }
            """;
        var result = Parse(code);
        var m = result.Symbols.First(sym => sym.Name == "id");
        await Assert.That(m.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(m.ParentSymbol).IsEqualTo("User");
    }

    // ── Constants ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPubConst()
    {
        var result = Parse("pub const MAX_LEN: usize = 255;");
        var c = result.Symbols.First(sym => sym.Name == "MAX_LEN");
        await Assert.That(c.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(c.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesPrivateConst()
    {
        var result = Parse("const DEFAULT: &str = \"test\";");
        var c = result.Symbols.First(sym => sym.Name == "DEFAULT");
        await Assert.That(c.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ParsesStatic()
    {
        var result = Parse("pub(crate) static EMAIL_SEP: &str = \"@\";");
        var s = result.Symbols.First(sym => sym.Name == "EMAIL_SEP");
        await Assert.That(s.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(s.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Type Aliases ──────────────────────────────────────────────────

    [Test]
    public async Task ParsesTypeAlias()
    {
        var result = Parse("pub type AppResult<T> = Result<T, String>;");
        var t = result.Symbols.First(sym => sym.Name == "AppResult");
        await Assert.That(t.Kind).IsEqualTo(SymbolKind.Type);
    }

    // ── Modules ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesModDecl()
    {
        var result = Parse("pub mod models;");
        var m = result.Symbols.First(sym => sym.Name == "models");
        await Assert.That(m.Kind).IsEqualTo(SymbolKind.Module);
    }

    // ── Macros ────────────────────────────────────────────────────────

    [Test]
    public async Task ParsesMacroRules()
    {
        var code = """
            macro_rules! app_error {
                ($($arg:tt)*) => {
                    Err(format!($($arg)*))
                };
            }
            """;
        var result = Parse(code);
        var m = result.Symbols.First(sym => sym.Name == "app_error");
        await Assert.That(m.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(m.Signature).Contains("macro_rules!");
    }

    // ── Doc Comments ──────────────────────────────────────────────────

    [Test]
    public async Task CapturesDocComment()
    {
        var code = """
            /// A registered user.
            pub struct User {
                id: String,
            }
            """;
        var result = Parse(code);
        var s = result.Symbols.First(sym => sym.Name == "User");
        await Assert.That(s.DocComment).IsNotNull();
        await Assert.That(s.DocComment!).Contains("registered user");
    }

    // ── Visibility ────────────────────────────────────────────────────

    [Test]
    public async Task PubCrateIsPrivate()
    {
        var result = Parse("pub(crate) static SEP: &str = \"@\";");
        var s = result.Symbols.First(sym => sym.Name == "SEP");
        await Assert.That(s.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Line Ranges ───────────────────────────────────────────────────

    [Test]
    public async Task StructSpansCorrectLines()
    {
        var code = """
            pub struct User {
                id: String,
                name: String,
            }
            """;
        var result = Parse(code);
        var s = result.Symbols.First(sym => sym.Name == "User");
        await Assert.That(s.LineStart).IsEqualTo(1);
        await Assert.That(s.LineEnd).IsEqualTo(4);
    }
}
