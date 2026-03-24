using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class GoParserTests
{
    private readonly GoParser _parser = new();

    private ParseResult Parse(string code) =>
        _parser.Parse("test.go", Encoding.UTF8.GetBytes(code));

    [Test]
    public async Task LanguageIdIsGo()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("go");
    }

    [Test]
    public async Task FileExtensionsContainsGo()
    {
        await Assert.That(_parser.FileExtensions).Contains(".go");
    }

    [Test]
    public async Task EmptyContentReturnsEmptyResult()
    {
        var result = _parser.Parse("test.go", ReadOnlySpan<byte>.Empty);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Package ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPackageDeclaration()
    {
        var result = Parse("package models");

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("models");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Module);
    }

    // ── Imports ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesSingleImport()
    {
        var result = Parse("""
            package main
            import "fmt"
            """);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("fmt");
    }

    [Test]
    public async Task ParsesGroupedImports()
    {
        var result = Parse("""
            package main
            import (
            	"fmt"
            	"strings"
            )
            """);

        await Assert.That(result.Dependencies).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ParsesAliasedImport()
    {
        var result = Parse("""
            package main
            import myalias "example.com/pkg"
            """);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].Alias).IsEqualTo("myalias");
    }

    // ── Structs ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesStruct()
    {
        var code = """
            package models
            type User struct {
            	Name string
            }
            """;
        var result = Parse(code);

        var user = result.Symbols.First(s => s.Name == "User");
        await Assert.That(user.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(user.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesUnexportedStruct()
    {
        var code = """
            package models
            type config struct {
            	value string
            }
            """;
        var result = Parse(code);

        var cfg = result.Symbols.First(s => s.Name == "config");
        await Assert.That(cfg.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Interfaces ────────────────────────────────────────────────────

    [Test]
    public async Task ParsesInterface()
    {
        var code = """
            package models
            type Auditable interface {
            	AuditID() string
            }
            """;
        var result = Parse(code);

        var iface = result.Symbols.First(s => s.Name == "Auditable");
        await Assert.That(iface.Kind).IsEqualTo(SymbolKind.Interface);
    }

    [Test]
    public async Task ParsesGenericInterface()
    {
        var code = """
            package service
            type Repository[T any, ID comparable] interface {
            	FindByID(id ID) (T, error)
            }
            """;
        var result = Parse(code);

        var repo = result.Symbols.First(s => s.Name == "Repository");
        await Assert.That(repo.Kind).IsEqualTo(SymbolKind.Interface);
        await Assert.That(repo.Signature).Contains("[T any, ID comparable]");
    }

    // ── Type Aliases ──────────────────────────────────────────────────

    [Test]
    public async Task ParsesNamedType()
    {
        var code = """
            package models
            type Role int
            """;
        var result = Parse(code);

        var role = result.Symbols.First(s => s.Name == "Role");
        await Assert.That(role.Kind).IsEqualTo(SymbolKind.Type);
    }

    // ── Functions ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesExportedFunction()
    {
        var code = """
            package models
            func NewUser(id string) *User {
            	return &User{}
            }
            """;
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == "NewUser");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesUnexportedFunction()
    {
        var code = """
            package util
            func isValid(s string) bool {
            	return s != ""
            }
            """;
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == "isValid");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ParsesGenericFunction()
    {
        var code = """
            package service
            func Audit[T Auditable](entity T) {
            	fmt.Println(entity.AuditID())
            }
            """;
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == "Audit");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
    }

    // ── Methods ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesPointerReceiverMethod()
    {
        var code = """
            package models
            func (u *User) SetName(name string) error {
            	return nil
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "SetName");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesValueReceiverMethod()
    {
        var code = """
            package models
            func (u User) String() string {
            	return u.Name
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "String");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("User");
    }

    // ── Constants ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesSingleConst()
    {
        var code = """
            package models
            const MaxNameLength = 255
            """;
        var result = Parse(code);

        var constant = result.Symbols.First(s => s.Name == "MaxNameLength");
        await Assert.That(constant.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(constant.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesVarDeclaration()
    {
        var code = """
            package models
            var ErrNotFound = errors.New("not found")
            """;
        var result = Parse(code);

        var v = result.Symbols.First(s => s.Name == "ErrNotFound");
        await Assert.That(v.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── Doc Comments ──────────────────────────────────────────────────

    [Test]
    public async Task CapturesDocComment()
    {
        var code = """
            package models
            // Entity is the base type for all domain objects.
            type Entity struct {
            }
            """;
        var result = Parse(code);

        var entity = result.Symbols.First(s => s.Name == "Entity");
        await Assert.That(entity.DocComment).IsNotNull();
        await Assert.That(entity.DocComment!).Contains("base type");
    }

    [Test]
    public async Task CapturesMultiLineDocComment()
    {
        var code = """
            package models
            // NewUser creates a new user.
            // It validates the name length.
            func NewUser(name string) *User {
            	return nil
            }
            """;
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == "NewUser");
        await Assert.That(fn.DocComment).IsNotNull();
        await Assert.That(fn.DocComment!).Contains("creates a new user");
    }

    // ── Visibility ────────────────────────────────────────────────────

    [Test]
    [Arguments("Exported", true)]
    [Arguments("unexported", false)]
    public async Task VisibilityDerivedFromCapitalization(string name, bool isPublic)
    {
        var code = "package test\nfunc " + name + "() {\n}\n";
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == name);
        var expected = isPublic ? Visibility.Public : Visibility.Private;
        await Assert.That(fn.Visibility).IsEqualTo(expected);
    }

    // ── Line Ranges ───────────────────────────────────────────────────

    [Test]
    public async Task StructSpansCorrectLines()
    {
        var code = """
            package models
            type User struct {
            	Name string
            }
            """;
        var result = Parse(code);

        var user = result.Symbols.First(s => s.Name == "User");
        await Assert.That(user.LineStart).IsEqualTo(2);
        await Assert.That(user.LineEnd).IsEqualTo(4);
    }

    [Test]
    public async Task FunctionSpansCorrectLines()
    {
        var code = """
            package main
            func doWork() {
            	fmt.Println("working")
            }
            """;
        var result = Parse(code);

        var fn = result.Symbols.First(s => s.Name == "doWork");
        await Assert.That(fn.LineStart).IsEqualTo(2);
        await Assert.That(fn.LineEnd).IsEqualTo(4);
    }

    // ── Resilience ────────────────────────────────────────────────────

    [Test]
    public async Task HandlesEmptyStruct()
    {
        var code = """
            package models
            type Empty struct {
            }
            """;
        var result = Parse(code);

        var empty = result.Symbols.First(s => s.Name == "Empty");
        await Assert.That(empty.Kind).IsEqualTo(SymbolKind.Class);
    }

    [Test]
    public async Task HandlesMultipleTypesInFile()
    {
        var code = """
            package models
            type First struct {
            }
            type Second struct {
            }
            """;
        var result = Parse(code);

        var names = result.Symbols.Where(s => s.Kind == SymbolKind.Class).Select(s => s.Name).ToList();
        await Assert.That(names).Contains("First");
        await Assert.That(names).Contains("Second");
    }
}
