using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class RubyParserTests
{
    private readonly RubyParser _parser = new();

    private ParseResult Parse(string code) =>
        _parser.Parse("test.rb", Encoding.UTF8.GetBytes(code));

    // ── Interface contract ────────────────────────────────────────────

    [Test]
    public async Task LanguageIdIsRuby()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("ruby");
    }

    [Test]
    [Arguments(".rb")]
    public async Task FileExtensionsContainsExpected(string ext)
    {
        await Assert.That(_parser.FileExtensions).Contains(ext);
    }

    [Test]
    public async Task EmptyContentReturnsEmpty()
    {
        var result = _parser.Parse("test.rb", ReadOnlySpan<byte>.Empty);
        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Classes ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesClass()
    {
        var code = "class User\nend\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(cls.ParentSymbol).IsNull();
    }

    [Test]
    public async Task ParsesClassWithInheritance()
    {
        var code = "class User < BaseEntity\nend\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Signature).Contains("BaseEntity");
    }

    [Test]
    public async Task ParsesClassLineNumbers()
    {
        var code = "class User\nend\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.LineStart).IsEqualTo(1);
        await Assert.That(cls.LineEnd).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ParsesMultipleClasses()
    {
        var code = "class User\nend\n\nclass Admin\nend\n";
        var result = Parse(code);
        var names = result.Symbols.Where(s => s.Kind == SymbolKind.Class).Select(s => s.Name).ToList();
        await Assert.That(names).Contains("User");
        await Assert.That(names).Contains("Admin");
    }

    // ── Modules ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesModule()
    {
        var code = "module Serializable\nend\n";
        var result = Parse(code);
        var mod = result.Symbols.First(s => s.Name == "Serializable");
        await Assert.That(mod.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(mod.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesModuleContainingClass()
    {
        var code = "module Services\n  class UserService\n  end\nend\n";
        var result = Parse(code);
        var svc = result.Symbols.First(s => s.Name == "UserService");
        await Assert.That(svc.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(svc.ParentSymbol).IsEqualTo("Services");
    }

    // ── Instance methods ─────────────────────────────────────────────

    [Test]
    public async Task ParsesInstanceMethod()
    {
        var code = "class User\n  def greet\n    'hello'\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "greet");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesInstanceMethodWithParams()
    {
        var code = "class User\n  def initialize(name, age)\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "initialize");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.Signature).Contains("initialize");
        await Assert.That(method.Signature).Contains("name");
    }

    [Test]
    public async Task ParsesMethodInsideModule()
    {
        var code = "module MathHelper\n  def add(a, b)\n    a + b\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "add");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("MathHelper");
    }

    [Test]
    public async Task ParsesTopLevelMethod()
    {
        var code = "def hello\n  puts 'hi'\nend\n";
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "hello");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.ParentSymbol).IsNull();
    }

    // ── Singleton (class) methods ─────────────────────────────────────

    [Test]
    public async Task ParsesSingletonMethod()
    {
        var code = "class User\n  def self.create(name)\n    new(name)\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "create");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesSingletonMethodSignatureIncludesSelf()
    {
        var code = "class User\n  def self.find_by_email(email)\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "find_by_email");
        await Assert.That(method.Signature).Contains("self");
    }

    // ── Constants ────────────────────────────────────────────────────

    [Test]
    public async Task ParsesModuleLevelConstant()
    {
        var code = "MAX_RETRIES = 3\n";
        var result = Parse(code);
        var constant = result.Symbols.First(s => s.Name == "MAX_RETRIES");
        await Assert.That(constant.Kind).IsEqualTo(SymbolKind.Constant);
    }

    [Test]
    public async Task ParsesClassLevelConstant()
    {
        var code = "class Config\n  DEFAULT_TIMEOUT = 30\nend\n";
        var result = Parse(code);
        var constant = result.Symbols.First(s => s.Name == "DEFAULT_TIMEOUT");
        await Assert.That(constant.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(constant.ParentSymbol).IsEqualTo("Config");
    }

    // ── Doc comments ─────────────────────────────────────────────────

    [Test]
    public async Task ParsesDocComment()
    {
        var code = "# A user model\nclass User\nend\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.DocComment).IsNotNull();
        await Assert.That(cls.DocComment).Contains("user model");
    }

    [Test]
    public async Task ParsesMethodDocComment()
    {
        var code = "class User\n  # Greets the user\n  def greet\n  end\nend\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "greet");
        await Assert.That(method.DocComment).IsNotNull();
        await Assert.That(method.DocComment).Contains("Greets");
    }

    // ── Byte offset accuracy ──────────────────────────────────────────

    [Test]
    public async Task ByteOffsetPointsToDeclarationStart()
    {
        var code = "class User\n  def greet\n  end\nend\n";
        var bytes = Encoding.UTF8.GetBytes(code);
        var result = _parser.Parse("test.rb", bytes);
        var cls = result.Symbols.First(s => s.Name == "User");
        var slice = Encoding.UTF8.GetString(bytes, cls.ByteOffset, 5);
        await Assert.That(slice).IsEqualTo("class");
    }

    [Test]
    public async Task ByteOffsetAndLengthAreNonNegative()
    {
        var code = "class Foo\n  def bar\n  end\nend\n";
        var result = Parse(code);
        foreach (var sym in result.Symbols)
        {
            await Assert.That(sym.ByteOffset).IsGreaterThanOrEqualTo(0);
            await Assert.That(sym.ByteLength).IsGreaterThan(0);
        }
    }

    // ── Resilience ────────────────────────────────────────────────────

    [Test]
    public async Task HandlesMalformedInputGracefully()
    {
        var code = "class Broken\n  def method_without_end\n";
        var result = Parse(code);
        // Should not throw — returns whatever was parseable
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task HandlesMixedClassesAndModules()
    {
        var code = "module App\n  class User\n    MAX = 10\n    def greet; end\n    def self.create; end\n  end\nend\n";
        var result = Parse(code);
        var kinds = result.Symbols.Select(s => s.Kind).Distinct().ToList();
        await Assert.That(kinds).Contains(SymbolKind.Class);
        await Assert.That(kinds).Contains(SymbolKind.Module);
        await Assert.That(kinds).Contains(SymbolKind.Method);
        await Assert.That(kinds).Contains(SymbolKind.Constant);
    }
}
