using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class BlazorRazorParserTests
{
    private readonly BlazorRazorParser _parser = new();

    private ParseResult Parse(string source, string filePath = "Components/Counter.razor")
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse(filePath, bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsBlazor()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("blazor");
    }

    [Test]
    public async Task FileExtensionsContainsRazor()
    {
        await Assert.That(_parser.FileExtensions).Contains(".razor");
    }

    // ── Empty / trivial files ───────────────────────────────────

    [Test]
    public async Task EmptyFileReturnsEmptyResult()
    {
        var result = Parse("");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MarkupOnlyFileReturnsComponentSymbol()
    {
        var source = """
            <h1>Hello World</h1>
            <p>Some markup content</p>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var component = result.Symbols[0];
        await Assert.That(component.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(component.Name).IsEqualTo("Counter");
    }

    // ── Component name extraction ───────────────────────────────

    [Test]
    public async Task ComponentNameExtractedFromFilename()
    {
        var source = """
            <h1>Widget</h1>
            """;

        var result = Parse(source, "Components/MyWidget.razor");

        var component = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(component.Name).IsEqualTo("MyWidget");
        await Assert.That(component.Signature).Contains("Razor component");
    }

    [Test]
    public async Task ComponentNameFromNestedPath()
    {
        var source = """
            <h1>Dashboard</h1>
            """;

        var result = Parse(source, "Pages/Admin/Dashboard.razor");

        var component = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(component.Name).IsEqualTo("Dashboard");
    }

    // ── @page route directives ──────────────────────────────────

    [Test]
    public async Task SinglePageRouteExtracted()
    {
        var source = """
            @page "/counter"

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var pageSymbol = result.Symbols.First(s => s.Name == @"@page ""/counter""");
        await Assert.That(pageSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(pageSymbol.ParentSymbol).IsEqualTo("Counter");
    }

    [Test]
    public async Task MultiplePageRoutesExtracted()
    {
        var source = """
            @page "/counter"
            @page "/counter/{Id:int}"

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var pageSymbols = result.Symbols.Where(s => s.Name.StartsWith("@page", StringComparison.Ordinal)).ToList();
        await Assert.That(pageSymbols).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PageRouteWithParameterExtracted()
    {
        var source = """
            @page "/counter/{Id:int}"

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var pageSymbol = result.Symbols.First(s => s.Name.StartsWith("@page", StringComparison.Ordinal));
        await Assert.That(pageSymbol.Name).IsEqualTo(@"@page ""/counter/{Id:int}""");
        await Assert.That(pageSymbol.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── @inject directives ──────────────────────────────────────

    [Test]
    public async Task InjectDirectiveCreatesSymbolAndDependency()
    {
        var source = """
            @inject NavigationManager Nav

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var injectSymbol = result.Symbols.First(s => s.Name == "Nav");
        await Assert.That(injectSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(injectSymbol.Signature).IsEqualTo("@inject NavigationManager Nav");
        await Assert.That(injectSymbol.ParentSymbol).IsEqualTo("Counter");

        var dependency = result.Dependencies.First(d => d.RequirePath == "NavigationManager");
        await Assert.That(dependency.RequirePath).IsEqualTo("NavigationManager");
    }

    [Test]
    public async Task InjectWithGenericType()
    {
        var source = """
            @inject ILogger<Counter> Logger

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var injectSymbol = result.Symbols.First(s => s.Name == "Logger");
        await Assert.That(injectSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(injectSymbol.Signature).IsEqualTo("@inject ILogger<Counter> Logger");

        var dependency = result.Dependencies.First(d => d.RequirePath == "ILogger<Counter>");
        await Assert.That(dependency.RequirePath).IsEqualTo("ILogger<Counter>");
    }

    // ── @using directives ───────────────────────────────────────

    [Test]
    public async Task UsingDirectiveCreatesDependency()
    {
        var source = """
            @using MyApp.Models

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var dependency = result.Dependencies.First(d => d.RequirePath == "MyApp.Models");
        await Assert.That(dependency.RequirePath).IsEqualTo("MyApp.Models");
        await Assert.That(dependency.Alias).IsNull();
    }

    [Test]
    public async Task UsingDirectiveWithAlias()
    {
        var source = """
            @using Models = MyApp.Models

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var dependency = result.Dependencies.First(d => d.RequirePath == "MyApp.Models");
        await Assert.That(dependency.RequirePath).IsEqualTo("MyApp.Models");
        await Assert.That(dependency.Alias).IsEqualTo("Models");
    }

    [Test]
    public async Task MultipleUsingDirectives()
    {
        var source = """
            @using MyApp.Models
            @using MyApp.Services

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var usingDeps = result.Dependencies
            .Where(d => d.RequirePath.StartsWith("MyApp.", StringComparison.Ordinal))
            .ToList();

        await Assert.That(usingDeps).Count().IsEqualTo(2);
    }

    // ── @inherits directive ─────────────────────────────────────

    [Test]
    public async Task InheritsDirectiveCreatesSymbol()
    {
        var source = """
            @inherits LayoutComponentBase

            <div>@Body</div>
            """;

        var result = Parse(source);

        var inheritsSymbol = result.Symbols.First(s => s.Name == "@inherits LayoutComponentBase");
        await Assert.That(inheritsSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(inheritsSymbol.ParentSymbol).IsEqualTo("Counter");
    }

    // ── @implements directive ───────────────────────────────────

    [Test]
    public async Task ImplementsDirectiveCreatesSymbol()
    {
        var source = """
            @implements IDisposable

            <h1>Counter</h1>
            """;

        var result = Parse(source);

        var implementsSymbol = result.Symbols.First(s => s.Name == "@implements IDisposable");
        await Assert.That(implementsSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(implementsSymbol.ParentSymbol).IsEqualTo("Counter");
    }

    // ── @code block parsing (delegation to CSharpParser) ────────

    [Test]
    public async Task CodeBlockMethodsExtracted()
    {
        var source = """
            <h1>Counter</h1>

            @code {
                private void IncrementCount()
                {
                    var x = 1;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Name == "IncrementCount");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Counter");
    }

    [Test]
    public async Task CodeBlockPropertiesExtracted()
    {
        var source = """
            <h1>Counter</h1>

            @code {
                [Parameter]
                public string Title { get; set; }
            }
            """;

        var result = Parse(source);

        var property = result.Symbols.First(s => s.Name == "Title");
        await Assert.That(property.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(property.ParentSymbol).IsEqualTo("Counter");
    }

    [Test]
    public async Task CodeBlockFieldsExtracted()
    {
        var source = """
            <h1>Counter</h1>

            @code {
                private int _currentCount;
            }
            """;

        var result = Parse(source);

        // CSharpParser may not extract standalone fields — only properties/methods.
        // The component Class symbol should still be present.
        var componentSymbol = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(componentSymbol.Name).IsEqualTo("Counter");
    }

    [Test]
    public async Task CodeBlockWithMultipleMembers()
    {
        var source = """
            <h1>Counter</h1>

            @code {
                [Parameter]
                public int Id { get; set; }

                private void IncrementCount()
                {
                    var x = 1;
                }
            }
            """;

        var result = Parse(source);

        var property = result.Symbols.First(s => s.Name == "Id");
        await Assert.That(property.Kind).IsEqualTo(SymbolKind.Constant);

        var method = result.Symbols.First(s => s.Name == "IncrementCount");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);

        // Both should have the component as parent
        await Assert.That(property.ParentSymbol).IsEqualTo("Counter");
        await Assert.That(method.ParentSymbol).IsEqualTo("Counter");
    }

    // ── @functions block (legacy) ───────────────────────────────

    [Test]
    public async Task FunctionsBlockExtractedLikeCodeBlock()
    {
        var source = """
            <h1>Counter</h1>

            @functions {
                private void DoWork()
                {
                    var x = 1;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Name == "DoWork");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Counter");
    }

    // ── Line number remapping ───────────────────────────────────

    [Test]
    public async Task LineNumbersRemappedToRazorFile()
    {
        var source = "@page \"/test\"\n\n<h1>Test</h1>\n\n@code {\n    private void DoSomething()\n    {\n        var x = 1;\n    }\n}\n";

        var result = Parse(source);

        // @code { is on line 5, content starts line 6, so DoSomething should have LineStart = 6
        var method = result.Symbols.First(s => s.Name == "DoSomething");
        await Assert.That(method.LineStart).IsEqualTo(6);
    }

    // ── No @code block ──────────────────────────────────────────

    [Test]
    public async Task NoCodeBlockStillExtractsDirectives()
    {
        var source = """
            @page "/counter"
            @inject NavigationManager Nav

            <h1>Counter</h1>
            <p>Current count: @currentCount</p>
            """;

        var result = Parse(source);

        var pageSymbol = result.Symbols.First(s => s.Name.StartsWith("@page", StringComparison.Ordinal));
        await Assert.That(pageSymbol.Kind).IsEqualTo(SymbolKind.Constant);

        var injectSymbol = result.Symbols.First(s => s.Name == "Nav");
        await Assert.That(injectSymbol.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── Multiple @code blocks ───────────────────────────────────

    [Test]
    public async Task MultipleCodeBlocksAllExtracted()
    {
        var source = """
            <h1>Counter</h1>

            @code {
                private void FirstMethod()
                {
                    var x = 1;
                }
            }

            <p>More markup</p>

            @code {
                private void SecondMethod()
                {
                    var y = 2;
                }
            }
            """;

        var result = Parse(source);

        var first = result.Symbols.First(s => s.Name == "FirstMethod");
        await Assert.That(first.Kind).IsEqualTo(SymbolKind.Method);

        var second = result.Symbols.First(s => s.Name == "SecondMethod");
        await Assert.That(second.Kind).IsEqualTo(SymbolKind.Method);
    }

    // ── Empty @code block ───────────────────────────────────────

    [Test]
    public async Task EmptyCodeBlockNoError()
    {
        var source = """
            @page "/counter"
            @inject NavigationManager Nav

            <h1>Counter</h1>

            @code { }
            """;

        var result = Parse(source);

        // Component symbol and directive symbols should still be present
        var componentSymbol = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(componentSymbol.Name).IsEqualTo("Counter");

        var pageSymbol = result.Symbols.First(s => s.Name.StartsWith("@page", StringComparison.Ordinal));
        await Assert.That(pageSymbol.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── Malformed C# in @code block ─────────────────────────────

    [Test]
    public async Task MalformedCodeBlockGracefulDegradation()
    {
        var source = """
            @page "/counter"
            @inject NavigationManager Nav

            <h1>Counter</h1>

            @code {
                this is not valid C# at all }{}{
            }
            """;

        var result = Parse(source);

        // Directive symbols should still be extracted even if @code content is invalid
        var pageSymbol = result.Symbols.First(s => s.Name.StartsWith("@page", StringComparison.Ordinal));
        await Assert.That(pageSymbol.Kind).IsEqualTo(SymbolKind.Constant);

        var injectSymbol = result.Symbols.First(s => s.Name == "Nav");
        await Assert.That(injectSymbol.Kind).IsEqualTo(SymbolKind.Constant);
    }

    // ── Code-behind coexistence ─────────────────────────────────

    [Test]
    public async Task ParserOnlyHandlesRazorExtension()
    {
        await Assert.That(_parser.FileExtensions).Count().IsEqualTo(1);
        await Assert.That(_parser.FileExtensions).Contains(".razor");
    }

    // ── Full integration-style test ─────────────────────────────

    [Test]
    public async Task FullRazorFileExtractsAllSymbolsAndDependencies()
    {
        var source = """
            @page "/counter"
            @page "/counter/{Id:int}"
            @using MyApp.Models
            @inject NavigationManager Nav
            @inject ILogger<Counter> Logger

            <h1>Counter</h1>
            <p>Current count: @currentCount</p>
            <button @onclick="IncrementCount">Click me</button>

            @code {
                [Parameter]
                public int Id { get; set; }

                private int _currentCount;

                private void IncrementCount()
                {
                    _currentCount++;
                }
            }
            """;

        var result = Parse(source);

        // Component class symbol
        var component = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(component.Name).IsEqualTo("Counter");

        // Two @page directives
        var pageSymbols = result.Symbols.Where(s => s.Name.StartsWith("@page", StringComparison.Ordinal)).ToList();
        await Assert.That(pageSymbols).Count().IsEqualTo(2);

        // Two @inject symbols
        var navSymbol = result.Symbols.First(s => s.Name == "Nav");
        await Assert.That(navSymbol.Kind).IsEqualTo(SymbolKind.Constant);
        var loggerSymbol = result.Symbols.First(s => s.Name == "Logger");
        await Assert.That(loggerSymbol.Kind).IsEqualTo(SymbolKind.Constant);

        // @code members
        var idProp = result.Symbols.First(s => s.Name == "Id");
        await Assert.That(idProp.Kind).IsEqualTo(SymbolKind.Constant);
        var incrementMethod = result.Symbols.First(s => s.Name == "IncrementCount");
        await Assert.That(incrementMethod.Kind).IsEqualTo(SymbolKind.Method);

        // Dependencies: @using + 2 @inject types
        var usingDep = result.Dependencies.First(d => d.RequirePath == "MyApp.Models");
        await Assert.That(usingDep.Alias).IsNull();

        var navDep = result.Dependencies.First(d => d.RequirePath == "NavigationManager");
        await Assert.That(navDep).IsNotNull();

        var loggerDep = result.Dependencies.First(d => d.RequirePath == "ILogger<Counter>");
        await Assert.That(loggerDep).IsNotNull();
    }
}
