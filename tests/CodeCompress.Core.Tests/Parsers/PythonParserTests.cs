using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class PythonParserTests
{
    private readonly PythonParser _parser = new();

    private ParseResult Parse(string code) =>
        _parser.Parse("test.py", Encoding.UTF8.GetBytes(code));

    [Test]
    public async Task LanguageIdIsPython()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("python");
    }

    [Test]
    [Arguments(".py")]
    [Arguments(".pyi")]
    public async Task FileExtensionsContainsExpected(string ext)
    {
        await Assert.That(_parser.FileExtensions).Contains(ext);
    }

    [Test]
    public async Task EmptyContentReturnsEmpty()
    {
        var result = _parser.Parse("test.py", ReadOnlySpan<byte>.Empty);
        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Imports ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesImport()
    {
        var result = Parse("import os");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("os");
    }

    [Test]
    public async Task ParsesFromImport()
    {
        var result = Parse("from datetime import datetime");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("datetime");
    }

    [Test]
    public async Task ParsesRelativeImport()
    {
        var result = Parse("from .entity import BaseEntity");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("entity");
    }

    // ── Classes ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesClass()
    {
        var code = "class User:\n    pass\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesClassWithInheritance()
    {
        var code = "class User(BaseEntity, Auditable):\n    pass\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.Signature).Contains("(BaseEntity, Auditable)");
    }

    [Test]
    public async Task ParsesPrivateClass()
    {
        var code = "class _Internal:\n    pass\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "_Internal");
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ParsesDecoratedClass()
    {
        var code = "@dataclass\nclass Config:\n    value: str\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "Config");
        await Assert.That(cls.Signature).Contains("@dataclass");
    }

    // ── Functions ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesFunction()
    {
        var code = "def hello(name: str) -> str:\n    return f'Hello {name}'\n";
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "hello");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesAsyncFunction()
    {
        var code = "async def fetch_data(url: str) -> dict:\n    pass\n";
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "fetch_data");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.Signature).Contains("async def");
    }

    [Test]
    public async Task ParsesPrivateFunction()
    {
        var code = "def _sanitize(value: str) -> str:\n    pass\n";
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "_sanitize");
        await Assert.That(fn.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Methods ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesMethod()
    {
        var code = "class Service:\n    def run(self) -> None:\n        pass\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "run");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Service");
    }

    [Test]
    public async Task ParsesStaticMethod()
    {
        var code = "class Factory:\n    @staticmethod\n    def create(id: str) -> 'Factory':\n        pass\n";
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "create");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.Signature).Contains("@staticmethod");
    }

    [Test]
    public async Task ParsesInitMethod()
    {
        var code = "class User:\n    def __init__(self, name: str) -> None:\n        self.name = name\n";
        var result = Parse(code);
        var init = result.Symbols.First(s => s.Name == "__init__");
        await Assert.That(init.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(init.ParentSymbol).IsEqualTo("User");
    }

    // ── Constants ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesModuleConstant()
    {
        var code = "MAX_LENGTH = 255\n";
        var result = Parse(code);
        var c = result.Symbols.First(s => s.Name == "MAX_LENGTH");
        await Assert.That(c.Kind).IsEqualTo(SymbolKind.Constant);
    }

    [Test]
    public async Task ParsesTypedConstant()
    {
        var code = "MAX_LENGTH: int = 255\n";
        var result = Parse(code);
        var c = result.Symbols.First(s => s.Name == "MAX_LENGTH");
        await Assert.That(c.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── Line Ranges ───────────────────────────────────────────────────

    [Test]
    public async Task ClassSpansToNextTopLevel()
    {
        var code = "class A:\n    def method(self):\n        pass\n\ndef top():\n    pass\n";
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "A");
        await Assert.That(cls.LineStart).IsEqualTo(1);
        // Class ends when top-level function starts
        await Assert.That(cls.LineEnd).IsEqualTo(4);
    }

    // ── Resilience ────────────────────────────────────────────────────

    [Test]
    public async Task HandlesMultipleClasses()
    {
        var code = "class A:\n    pass\n\nclass B:\n    pass\n";
        var result = Parse(code);
        var names = result.Symbols.Where(s => s.Kind == SymbolKind.Class).Select(s => s.Name).ToList();
        await Assert.That(names).Contains("A");
        await Assert.That(names).Contains("B");
    }
}
