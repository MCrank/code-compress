using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class LuauParserTests
{
    private readonly LuauParser _parser = new();

    private ParseResult Parse(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse("test.luau", bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsLuau()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("luau");
    }

    [Test]
    public async Task FileExtensionsContainsLuauAndLua()
    {
        await Assert.That(_parser.FileExtensions).Contains(".luau");
        await Assert.That(_parser.FileExtensions).Contains(".lua");
    }

    // ── Empty / comments-only ───────────────────────────────────

    [Test]
    public async Task ParseEmptyFileReturnsEmptyResult()
    {
        var result = Parse("");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ParseOnlyCommentsReturnsEmptyResult()
    {
        var source = """
            -- this is a comment
            -- another comment
            --[[ block comment ]]
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Single function ─────────────────────────────────────────

    [Test]
    public async Task ParseSingleFunctionReturnsCorrectSymbolInfo()
    {
        var source = """
            function greet(name: string): string
                return "Hello, " .. name
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("greet");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(sym.ParentSymbol).IsNull();
        await Assert.That(sym.Signature).IsEqualTo("function greet(name: string): string");
    }

    // ── Method on class ─────────────────────────────────────────

    [Test]
    public async Task ParseMethodOnClassReturnsCorrectParentAndVisibility()
    {
        var source = """
            local MyClass = {} :: MyClass

            function MyClass:doStuff(x: number): boolean
                return x > 0
            end

            return MyClass
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("doStuff");
        await Assert.That(method.ParentSymbol).IsEqualTo("MyClass");
        await Assert.That(method.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(method.Signature).IsEqualTo("function MyClass:doStuff(x: number): boolean");
    }

    // ── Local function ──────────────────────────────────────────

    [Test]
    public async Task ParseLocalFunctionReturnsPrivateVisibility()
    {
        var source = """
            local function helper(a: number, b: number): number
                return a + b
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("helper");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Module class declaration ────────────────────────────────

    [Test]
    public async Task ParseModuleClassDeclarationReturnsClassSymbol()
    {
        var source = """
            local PlayerService = {} :: PlayerService
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("PlayerService");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(sym.Signature).IsEqualTo("local PlayerService = {} :: PlayerService");
    }

    // ── Module return ───────────────────────────────────────────

    [Test]
    public async Task ParseModuleReturnReturnsExportSymbol()
    {
        var source = """
            local Foo = {} :: Foo

            return Foo
            """;

        var result = Parse(source);

        var export = result.Symbols.First(s => s.Kind == SymbolKind.Export);
        await Assert.That(export.Name).IsEqualTo("Foo");
        await Assert.That(export.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(export.Signature).IsEqualTo("return Foo");
    }

    // ── Multiple symbols in order ───────────────────────────────

    [Test]
    public async Task ParseMultipleSymbolsReturnsAllInOrder()
    {
        var source = """
            local MyModule = {} :: MyModule

            local function privateHelper()
            end

            function MyModule:publicMethod()
            end

            function topLevel()
            end

            return MyModule
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(5);

        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("MyModule");

        await Assert.That(result.Symbols[1].Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(result.Symbols[1].Name).IsEqualTo("privateHelper");
        await Assert.That(result.Symbols[1].Visibility).IsEqualTo(Visibility.Private);

        await Assert.That(result.Symbols[2].Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(result.Symbols[2].Name).IsEqualTo("publicMethod");

        await Assert.That(result.Symbols[3].Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(result.Symbols[3].Name).IsEqualTo("topLevel");
        await Assert.That(result.Symbols[3].Visibility).IsEqualTo(Visibility.Public);

        await Assert.That(result.Symbols[4].Kind).IsEqualTo(SymbolKind.Export);
        await Assert.That(result.Symbols[4].Name).IsEqualTo("MyModule");
    }

    // ── Line numbers ────────────────────────────────────────────

    [Test]
    public async Task ParseLineNumbersAreOneBased()
    {
        var source = "function foo()\n    local x = 1\nend\n";

        var result = Parse(source);

        var sym = result.Symbols[0];
        await Assert.That(sym.LineStart).IsEqualTo(1);
        await Assert.That(sym.LineEnd).IsEqualTo(3);
    }

    // ── Byte offsets ────────────────────────────────────────────

    [Test]
    public async Task ParseByteOffsetsAreAccurate()
    {
        var source = "function foo()\n    local x = 1\nend\n";
        var bytes = Encoding.UTF8.GetBytes(source);

        var result = _parser.Parse("test.luau", bytes);

        var sym = result.Symbols[0];
        await Assert.That(sym.ByteOffset).IsEqualTo(0);
        // ByteLength covers from "function" to the end of "end"
        var expectedLength = source.IndexOf("end", StringComparison.Ordinal) + 3 - 0;
        await Assert.That(sym.ByteLength).IsEqualTo(expectedLength);
    }

    // ── Nested blocks ───────────────────────────────────────────

    [Test]
    public async Task ParseNestedBlocksDoNotConfuseEndMatching()
    {
        var source = """
            function complex()
                if true then
                    for i = 1, 10 do
                        print(i)
                    end
                end
                while true do
                    break
                end
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("complex");
        await Assert.That(sym.LineStart).IsEqualTo(1);
        await Assert.That(sym.LineEnd).IsEqualTo(10);
    }

    // ── Signature capture ───────────────────────────────────────

    [Test]
    public async Task ParseFunctionWithReturnTypeCapturesFullSignature()
    {
        var source = """
            function calculate(): string
                return "result"
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].Signature).IsEqualTo("function calculate(): string");
    }

    [Test]
    public async Task ParseMethodWithParametersCapturesFullSignature()
    {
        var source = """
            function Cls:bar(x: number, y: string): boolean
                return true
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].Signature).IsEqualTo("function Cls:bar(x: number, y: string): boolean");
    }

    // ── Nested function inside function ─────────────────────────

    [Test]
    public async Task ParseRepeatUntilBlockDoesNotConfuseEndMatching()
    {
        var source = """
            function withRepeat()
                repeat
                    local x = 1
                until x > 5
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].LineEnd).IsEqualTo(5);
    }

    [Test]
    public async Task ParseDoBlockDoesNotConfuseEndMatching()
    {
        var source = """
            function withDo()
                do
                    local x = 1
                end
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].LineEnd).IsEqualTo(5);
    }

    // ── Dot function (module.func) ──────────────────────────────

    [Test]
    public async Task ParseDotFunctionExtractsAsMethod()
    {
        var source = """
            function MyModule.staticFunc(a: number)
                return a
            end
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("staticFunc");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(sym.ParentSymbol).IsEqualTo("MyModule");
    }
}
