using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Tools;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class QueryToolsTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private ISymbolStore _store = null!;
    private QueryTools _tools = null!;

    [Before(Test)]
    public void SetUp()
    {
        _pathValidator = Substitute.For<IPathValidator>();
        _scopeFactory = Substitute.For<IProjectScopeFactory>();
        _scope = Substitute.For<IProjectScope>();
        _store = Substitute.For<ISymbolStore>();
        _scope.Store.Returns(_store);
        _scope.RepoId.Returns("test-repo-id");
        _scopeFactory.CreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_scope);
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));
        _pathValidator.ValidateRelativePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));

        _tools = new QueryTools(_pathValidator, _scopeFactory);
    }

    // ── ProjectOutline (unchanged: still returns Markdown, not structured content) ──

    [Test]
    public async Task ProjectOutlineValidPathReturnsStructuredOutline()
    {
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
            CreateSymbol(2, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack(attacker, target): DamageResult", parent: "CombatService"),
        };
        var fileGroup = new OutlineGroup("CombatService.luau", symbols, []);
        var dirGroup = new OutlineGroup("src/services/", [], [fileGroup]);
        var outline = new ProjectOutline("test-repo-id", [dirGroup], 2, false);

        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await Assert.That(result).Contains("#");
        await Assert.That(result).Contains("CombatService");
        await Assert.That(result).Contains("ProcessAttack");
    }

    [Test]
    public async Task ProjectOutlineGroupByFileGroupsCorrectly()
    {
        var serviceSymbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
            CreateSymbol(2, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack(attacker, target): DamageResult", parent: "CombatService"),
        };
        var utilSymbols = new List<Symbol>
        {
            CreateSymbol(3, 2, "MathUtils", "Class", "local MathUtils = {} :: MathUtils"),
        };
        var serviceFile = new OutlineGroup("CombatService.luau", serviceSymbols, []);
        var utilFile = new OutlineGroup("MathUtils.luau", utilSymbols, []);
        var servicesDir = new OutlineGroup("src/services/", [], [serviceFile]);
        var utilsDir = new OutlineGroup("src/utils/", [], [utilFile]);
        var outline = new ProjectOutline("test-repo-id", [servicesDir, utilsDir], 3, false);

        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", groupBy: "file").ConfigureAwait(false);

        await Assert.That(result).Contains("CombatService.luau");
        await Assert.That(result).Contains("MathUtils.luau");
        await Assert.That(result).Contains("CombatService:ProcessAttack");
    }

    [Test]
    public async Task ProjectOutlineGroupByKindGroupsCorrectly()
    {
        var classSymbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
        };
        var methodSymbols = new List<Symbol>
        {
            CreateSymbol(2, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack(attacker, target): DamageResult", parent: "CombatService"),
        };
        var classesGroup = new OutlineGroup("Classes", classSymbols, []);
        var methodsGroup = new OutlineGroup("Methods", methodSymbols, []);
        var outline = new ProjectOutline("test-repo-id", [classesGroup, methodsGroup], 2, false);

        _store.GetProjectOutlineAsync("test-repo-id", false, "kind", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", groupBy: "kind").ConfigureAwait(false);

        await Assert.That(result).Contains("Classes");
        await Assert.That(result).Contains("Methods");
        await Assert.That(result).Contains("CombatService");
        await Assert.That(result).Contains("ProcessAttack");
    }

    [Test]
    public async Task ProjectOutlineGroupByDirectorySummarizesDirectories()
    {
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
        };
        var dirGroup = new OutlineGroup("src/services/", symbols, []);
        var outline = new ProjectOutline("test-repo-id", [dirGroup], 1, false);

        _store.GetProjectOutlineAsync("test-repo-id", false, "directory", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", groupBy: "directory").ConfigureAwait(false);

        await Assert.That(result).Contains("src/services/");
    }

    [Test]
    public async Task ProjectOutlineInvalidGroupByReturnsError()
    {
        var result = await _tools.ProjectOutline("/valid/path", groupBy: "invalid").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString())
            .IsEqualTo("Invalid group_by value. Must be one of: file, kind, directory");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_GROUP_BY");

        await _store.DidNotReceive().GetProjectOutlineAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineIncludePrivateFalseOmitsPrivateSymbols()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", includePrivate: false).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineIncludePrivateTrueIncludesAllSymbols()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", true, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", includePrivate: true).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", true, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineMaxDepthLimitsTraversal()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", 1, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", maxDepth: 1).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", 1, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.ProjectOutline("/../../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task ProjectOutlineDoesNotEchoRawPath()
    {
        var distinctivePath = "/very/unique/distinctive/test/path/12345";
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline(distinctivePath).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(distinctivePath);
    }

    [Test]
    public async Task ProjectOutlinePathFilterPassesToStore()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), "src/services", Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", pathFilter: "src/services").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), "src/services", Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlinePathFilterNullPassesNullToStore()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), null, Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), null, Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlinePathFilterWithTraversalReturnsError()
    {
        var result = await _tools.ProjectOutline("/valid/path", pathFilter: "../etc").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Invalid path filter");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task ProjectOutlinePathFilterAbsolutePathReturnsError()
    {
        var result = await _tools.ProjectOutline("/valid/path", pathFilter: "/etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task ProjectOutlinePathFilterEmptyStringReturnsError()
    {
        var result = await _tools.ProjectOutline("/valid/path", pathFilter: "  ").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task ProjectOutlinePathFilterDoesNotEchoRawFilter()
    {
        var maliciousFilter = "src/<script>alert(1)</script>";
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", pathFilter: maliciousFilter).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(maliciousFilter);
    }

    [Test]
    public async Task ProjectOutlinePathFilterCombinesWithGroupBy()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "kind", Arg.Any<int>(), "src/services", Arg.Any<int>(), Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", groupBy: "kind", pathFilter: "src/services").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "kind", Arg.Any<int>(), "src/services", Arg.Any<int>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineDefaultMaxSymbolsIs500()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).Returns(outline);

        await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineCustomMaxSymbolsPassesToStore()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 100).Returns(outline);

        await _tools.ProjectOutline("/valid/path", maxSymbols: 100).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 100).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineOffsetPassesToStore()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 50, 500).Returns(outline);

        await _tools.ProjectOutline("/valid/path", offset: 50).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 50, 500).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineMaxSymbolsClampedToUpperBound()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 5000).Returns(outline);

        await _tools.ProjectOutline("/valid/path", maxSymbols: 99999).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 5000).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineMaxSymbolsClampedToLowerBound()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 1).Returns(outline);

        await _tools.ProjectOutline("/valid/path", maxSymbols: -5).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 1).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineNegativeOffsetClampedToZero()
    {
        var outline = new ProjectOutline("test-repo-id", [], 0, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).Returns(outline);

        await _tools.ProjectOutline("/valid/path", offset: -10).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineTruncatedShowsTruncationIndicator()
    {
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
        };
        var group = new OutlineGroup("src/services/CombatService.luau", symbols, []);
        var outline = new ProjectOutline("test-repo-id", [group], 100, true);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await Assert.That(result).Contains("showing 1 of 100 symbols");
        await Assert.That(result).Contains("**Truncated:**");
        await Assert.That(result).Contains("offset: 1");
    }

    [Test]
    public async Task ProjectOutlineNotTruncatedOmitsTruncationIndicator()
    {
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
        };
        var group = new OutlineGroup("src/services/CombatService.luau", symbols, []);
        var outline = new ProjectOutline("test-repo-id", [group], 1, false);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 0, 500).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await Assert.That(result).DoesNotContain("Truncated");
        await Assert.That(result).DoesNotContain("showing");
    }

    [Test]
    public async Task ProjectOutlineTruncatedWithOffsetShowsCorrectContinuation()
    {
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "Helper", "Function", "function Helper()"),
        };
        var group = new OutlineGroup("src/utils.luau", symbols, []);
        var outline = new ProjectOutline("test-repo-id", [group], 200, true);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>(), 50, 100).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", maxSymbols: 100, offset: 50).ConfigureAwait(false);

        await Assert.That(result).Contains("offset 50");
        await Assert.That(result).Contains("offset: 51");
        await Assert.That(result).Contains("149 symbols remaining");
    }

    // ── GetModuleApi ─────────────────────────────────────────────────

    [Test]
    public async Task GetModuleApiValidModuleReturnsFullApi()
    {
        var file = new FileRecord(1, "test-repo-id", "src/services/CombatService.luau", "abc123", 2048, 80, 1000, 2000);
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService", lineStart: 1, docComment: "Combat service module"),
            CreateSymbol(2, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack(attacker, target): DamageResult", parent: "CombatService", lineStart: 10),
        };
        var dependencies = new List<Dependency>
        {
            new(1, 1, "src/utils/MathUtils", null, null),
        };
        var moduleApi = new ModuleApi(file, symbols, dependencies);

        _store.GetModuleApiAsync("test-repo-id", "src/services/CombatService.luau").Returns(moduleApi);

        var result = await _tools.GetModuleApi("/valid/path", "src/services/CombatService.luau").ConfigureAwait(false);

        await Assert.That(result.Module).IsEqualTo("src/services/CombatService.luau");
        await Assert.That(result.Symbols).Count().IsEqualTo(2);

        var firstSymbol = result.Symbols![0];
        await Assert.That(firstSymbol.Name).IsEqualTo("CombatService");
        await Assert.That(firstSymbol.Kind).IsEqualTo("Class");
        await Assert.That(firstSymbol.Signature).IsEqualTo("local CombatService = {} :: CombatService");
        await Assert.That(firstSymbol.Line).IsEqualTo(1);
        await Assert.That(firstSymbol.DocComment).IsEqualTo("Combat service module");

        var secondSymbol = result.Symbols![1];
        await Assert.That(secondSymbol.Name).IsEqualTo("ProcessAttack");
        await Assert.That(secondSymbol.Kind).IsEqualTo("Method");

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GetModuleApiNonExistentModuleReturnsError()
    {
        _store.GetModuleApiAsync("test-repo-id", "src/nonexistent.luau")
            .Throws(new FileNotFoundException("Module not found"));

        var result = await _tools.GetModuleApi("/valid/path", "src/nonexistent.luau").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Module not found");
        await Assert.That(result.Code).IsEqualTo("MODULE_NOT_FOUND");
    }

    [Test]
    public async Task GetModuleApiTraversalModulePathReturnsError()
    {
        _pathValidator.ValidateRelativePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetModuleApi("/valid/path", "../../etc/passwd").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task GetModuleApiInvalidProjectPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetModuleApi("/../invalid", "src/module.luau").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task GetModuleApiIncludesDependencies()
    {
        var file = new FileRecord(1, "test-repo-id", "src/services/CombatService.luau", "abc123", 2048, 80, 1000, 2000);
        var symbols = new List<Symbol>
        {
            CreateSymbol(1, 1, "CombatService", "Class", "local CombatService = {} :: CombatService"),
        };
        var dependencies = new List<Dependency>
        {
            new(1, 1, "src/utils/MathUtils", null, null),
            new(2, 1, "src/utils/DamageCalc", 3, "Damage"),
        };
        var moduleApi = new ModuleApi(file, symbols, dependencies);

        _store.GetModuleApiAsync("test-repo-id", "src/services/CombatService.luau").Returns(moduleApi);

        var result = await _tools.GetModuleApi("/valid/path", "src/services/CombatService.luau").ConfigureAwait(false);

        await Assert.That(result.Dependencies).Count().IsEqualTo(2);
        await Assert.That(result.Dependencies![0].RequiresPath).IsEqualTo("src/utils/MathUtils");
        await Assert.That(result.Dependencies![1].RequiresPath).IsEqualTo("src/utils/DamageCalc");
        await Assert.That(result.Dependencies![1].Alias).IsEqualTo("Damage");
    }

    // ── GetSymbol ────────────────────────────────────────────────────

    [Test]
    public async Task GetSymbolExistingSymbolReturnsSourceCode()
    {
        var content = "line1\nline2\nfunction ProcessAttack()\n  body\nend\nline6\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // "line1\nline2\n" = 12 bytes, "function ProcessAttack()\n  body\nend" = 35 bytes
            var symbol = CreateSymbol(1, 1, "ProcessAttack", "Method",
                "function CombatService:ProcessAttack()", parent: "CombatService",
                lineStart: 3, byteOffset: 12, byteLength: 35);

            _store.GetSymbolByNameAsync("test-repo-id", "CombatService:ProcessAttack")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 100, 6, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "CombatService:ProcessAttack").ConfigureAwait(false);

            await Assert.That(result.Name).IsEqualTo("ProcessAttack");
            await Assert.That(result.Kind).IsEqualTo("Method");
            await Assert.That(result.Parent).IsEqualTo("CombatService");
            await Assert.That(result.File).IsEqualTo(fileName);
            await Assert.That(result.LineStart).IsEqualTo(3);
            await Assert.That(result.LineEnd).IsEqualTo(8);
            await Assert.That(result.Signature)
                .IsEqualTo("function CombatService:ProcessAttack()");
            await Assert.That(result.SourceCode)
                .IsEqualTo("function ProcessAttack()\n  body\nend");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolWithContextIncludesSurroundingLines()
    {
        var lines = new StringBuilder();
        for (var i = 1; i <= 15; i++)
        {
            lines.Append(CultureInfo.InvariantCulture, $"line{i}\n");
        }

        var content = lines.ToString();
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // Lines 1-5 = "line1\nline2\nline3\nline4\nline5\n" = 30 bytes
            // "line6\nline7\nline8\n" starts at byte 30, length = 18 bytes
            var symbol = CreateSymbol(1, 1, "MyFunc", "Function",
                "function MyFunc()", lineStart: 6, byteOffset: 30, byteLength: 18);

            _store.GetSymbolByNameAsync("test-repo-id", "MyFunc")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 15, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "MyFunc", includeContext: true).ConfigureAwait(false);

            var sourceCode = result.SourceCode!;

            // With 5 lines context, should include lines 1-13 (5 before line 6, lines 6-8, 5 after line 8)
            await Assert.That(sourceCode).Contains("line1");
            await Assert.That(sourceCode).Contains("line6");
            await Assert.That(sourceCode).Contains("line7");
            await Assert.That(sourceCode).Contains("line8");
            await Assert.That(sourceCode).Contains("line13");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolWithContextAtFileStartHandlesGracefully()
    {
        var content = "function Start()\n  body\nend\nline4\nline5\nline6\nline7\nline8\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // Symbol at byte 0, "function Start()\n  body\nend" = 27 bytes
            var symbol = CreateSymbol(1, 1, "Start", "Function",
                "function Start()", lineStart: 1, byteOffset: 0, byteLength: 27);

            _store.GetSymbolByNameAsync("test-repo-id", "Start")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 8, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "Start", includeContext: true).ConfigureAwait(false);

            var sourceCode = result.SourceCode!;
            await Assert.That(sourceCode).Contains("function Start()");
            await Assert.That(sourceCode).Contains("body");
            await Assert.That(sourceCode).Contains("end");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolWithContextAtFileEndHandlesGracefully()
    {
        var content = "line1\nline2\nline3\nline4\nline5\nfunction End()\n  body\nend\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // "line1\n" through "line5\n" = 5*6 = 30 bytes
            // "function End()\n  body\nend" starts at byte 30, length = 25 bytes
            var symbol = CreateSymbol(1, 1, "End", "Function",
                "function End()", lineStart: 6, byteOffset: 30, byteLength: 25);

            _store.GetSymbolByNameAsync("test-repo-id", "End")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 8, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "End", includeContext: true).ConfigureAwait(false);

            var sourceCode = result.SourceCode!;
            await Assert.That(sourceCode).Contains("function End()");
            await Assert.That(sourceCode).Contains("body");
            await Assert.That(sourceCode).Contains("end");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolNonExistentReturnsError()
    {
        _store.GetSymbolByNameAsync("test-repo-id", "NonExistent")
            .Returns((Symbol?)null);

        var result = await _tools.GetSymbol("/valid/path", "NonExistent").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Symbol not found");
        await Assert.That(result.Code).IsEqualTo("SYMBOL_NOT_FOUND");
        await Assert.That(result.Guidance)
            .Contains("search_symbols").And.Contains("index_project");
    }

    [Test]
    public async Task GetSymbolInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetSymbol("/../../../etc/passwd", "SomeSymbol").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task GetSymbolByteOffsetMatchesFileContent()
    {
        var content = "-- header comment\nlocal x = 10\nfunction Exact()\n  return x\nend\n-- footer\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // "-- header comment\nlocal x = 10\n" = 18 + 13 = 31 bytes
            // "function Exact()\n  return x\nend" = 31 bytes
            var expectedSource = "function Exact()\n  return x\nend";
            var byteOffset = Encoding.UTF8.GetByteCount("-- header comment\nlocal x = 10\n");
            var byteLength = Encoding.UTF8.GetByteCount(expectedSource);

            var symbol = CreateSymbol(1, 1, "Exact", "Function",
                "function Exact()", lineStart: 3, byteOffset: byteOffset, byteLength: byteLength);

            _store.GetSymbolByNameAsync("test-repo-id", "Exact")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 6, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "Exact").ConfigureAwait(false);

            await Assert.That(result.SourceCode).IsEqualTo(expectedSource);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── ExpandSymbol ─────────────────────────────────────────────

    [Test]
    public async Task ExpandSymbolNestedMethodReturnsOnlyMethodBody()
    {
        var content = "public class PlayerService\n{\n    public int GetHealth()\n    {\n        return 100;\n    }\n}\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var methodSource = "    public int GetHealth()\n    {\n        return 100;\n    }";
            var byteOffset = Encoding.UTF8.GetByteCount("public class PlayerService\n{\n");
            var byteLength = Encoding.UTF8.GetByteCount(methodSource);

            var symbol = CreateSymbol(2, 1, "GetHealth", "Method",
                "public int GetHealth()", parent: "PlayerService",
                lineStart: 3, docComment: "Gets player health",
                byteOffset: byteOffset, byteLength: byteLength);

            _store.GetSymbolByNameAsync("test-repo-id", "PlayerService:GetHealth")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 100, 7, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.ExpandSymbol(dir, "PlayerService:GetHealth").ConfigureAwait(false);

            await Assert.That(result.Name).IsEqualTo("GetHealth");
            await Assert.That(result.Kind).IsEqualTo("Method");
            await Assert.That(result.Parent).IsEqualTo("PlayerService");
            await Assert.That(result.Signature).IsEqualTo("public int GetHealth()");
            await Assert.That(result.DocComment).IsEqualTo("Gets player health");
            await Assert.That(result.SourceCode).IsEqualTo(methodSource);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExpandSymbolTopLevelReturnsFullSource()
    {
        var content = "function Initialize()\n  setup()\nend\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var expectedSource = "function Initialize()\n  setup()\nend";
            var byteLength = Encoding.UTF8.GetByteCount(expectedSource);

            var symbol = CreateSymbol(1, 1, "Initialize", "Function",
                "function Initialize()", lineStart: 1,
                byteOffset: 0, byteLength: byteLength);

            _store.GetSymbolByNameAsync("test-repo-id", "Initialize")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 50, 3, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.ExpandSymbol(dir, "Initialize").ConfigureAwait(false);

            await Assert.That(result.Name).IsEqualTo("Initialize");
            await Assert.That(result.SourceCode).IsEqualTo(expectedSource);
            await Assert.That(result.Parent).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExpandSymbolNotFoundReturnsError()
    {
        _store.GetSymbolByNameAsync("test-repo-id", "NonExistent:Method")
            .Returns((Symbol?)null);

        var result = await _tools.ExpandSymbol("/valid/path", "NonExistent:Method").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Symbol not found");
        await Assert.That(result.Code).IsEqualTo("SYMBOL_NOT_FOUND");
        await Assert.That(result.Guidance)
            .Contains("search_symbols").And.Contains("index_project");
    }

    [Test]
    public async Task ExpandSymbolPrefixMatchReturnsCandidatesWithQualifiedNames()
    {
        // Exact match fails
        _store.GetSymbolByNameAsync("test-repo-id", "ProjectEndpoints:MapMaestroProject")
            .Returns((Symbol?)null);

        // Prefix match returns multiple candidates
        var candidates = new List<Symbol>
        {
            CreateSymbol(2, 1, "MapMaestroProjectCrudEndpoints", "Method", "public static void MapMaestroProjectCrudEndpoints()", parent: "ProjectEndpoints"),
            CreateSymbol(3, 1, "MapMaestroProjectMemberEndpoints", "Method", "public static void MapMaestroProjectMemberEndpoints()", parent: "ProjectEndpoints"),
        };
        _store.GetSymbolsByParentAndChildPrefixAsync("test-repo-id", "ProjectEndpoints", "MapMaestroProject", Arg.Any<int>())
            .Returns(candidates);

        var result = await _tools.ExpandSymbol("/valid/path", "ProjectEndpoints:MapMaestroProject").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("SYMBOL_NOT_FOUND");
        await Assert.That(result.Candidates).Count().IsEqualTo(2);
        await Assert.That(result.Candidates![0]).IsEqualTo("ProjectEndpoints:MapMaestroProjectCrudEndpoints");
        await Assert.That(result.Candidates![1]).IsEqualTo("ProjectEndpoints:MapMaestroProjectMemberEndpoints");
    }

    [Test]
    public async Task ExpandSymbolPrefixMatchSingleResultReturnsSymbol()
    {
        var content = "public static class ProjectEndpoints\n{\n    public static void MapMaestroProjectMemberEndpoints() { }\n}\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var methodSource = "    public static void MapMaestroProjectMemberEndpoints() { }";
            var byteOffset = Encoding.UTF8.GetByteCount("public static class ProjectEndpoints\n{\n");
            var byteLength = Encoding.UTF8.GetByteCount(methodSource);

            // Exact match fails
            _store.GetSymbolByNameAsync("test-repo-id", "ProjectEndpoints:MapMaestroProjectMember")
                .Returns((Symbol?)null);

            // Prefix match returns single result — should auto-resolve
            var symbol = CreateSymbol(2, 1, "MapMaestroProjectMemberEndpoints", "Method",
                "public static void MapMaestroProjectMemberEndpoints()", parent: "ProjectEndpoints",
                byteOffset: byteOffset, byteLength: byteLength);
            _store.GetSymbolsByParentAndChildPrefixAsync("test-repo-id", "ProjectEndpoints", "MapMaestroProjectMember", Arg.Any<int>())
                .Returns(new List<Symbol> { symbol });
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 500, 20, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.ExpandSymbol(dir, "ProjectEndpoints:MapMaestroProjectMember").ConfigureAwait(false);

            await Assert.That(result.Name).IsEqualTo("MapMaestroProjectMemberEndpoints");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExpandSymbolInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.ExpandSymbol("/../../../etc/passwd", "SomeSymbol").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task ExpandSymbolWithContextIncludesThreeLines()
    {
        // Use explicit \n to avoid Environment.NewLine differences across platforms
        var content = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            // Target line5 and line6
            var prefix = "line1\nline2\nline3\nline4\n";
            var target = "line5\nline6";
            var byteOffset = Encoding.UTF8.GetByteCount(prefix);
            var byteLength = Encoding.UTF8.GetByteCount(target);

            var symbol = CreateSymbol(1, 1, "Target", "Function",
                "function Target()", lineStart: 5,
                byteOffset: byteOffset, byteLength: byteLength);

            _store.GetSymbolByNameAsync("test-repo-id", "Target")
                .Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 100, 10, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.ExpandSymbol(dir, "Target", includeContext: true).ConfigureAwait(false);

            var sourceCode = result.SourceCode!;

            // Context algorithm counts the boundary newline as one of the 3,
            // so we get 2 visible context lines before and after
            await Assert.That(sourceCode).Contains("line3");
            await Assert.That(sourceCode).Contains("line4");
            await Assert.That(sourceCode).Contains("line5");
            await Assert.That(sourceCode).Contains("line6");
            await Assert.That(sourceCode).Contains("line7");
            await Assert.That(sourceCode).Contains("line8");
            // line1 and line2 should NOT be in context
            await Assert.That(sourceCode).DoesNotContain("line1\n");
            await Assert.That(sourceCode).DoesNotContain("line2\n");
            // line9 should NOT be in context
            await Assert.That(sourceCode).DoesNotContain("line9");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── GetSymbols (batch) ───────────────────────────────────────────

    [Test]
    public async Task GetSymbolsAllFoundReturnsAllResults()
    {
        var content = "function A()\nend\nfunction B()\nend\nfunction C()\nend\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbols = new List<Symbol>
            {
                CreateSymbol(1, 1, "A", "Function", "function A()", lineStart: 1,
                    byteOffset: 0, byteLength: 16),
                CreateSymbol(2, 1, "B", "Function", "function B()", lineStart: 3,
                    byteOffset: 17, byteLength: 16),
                CreateSymbol(3, 1, "C", "Function", "function C()", lineStart: 5,
                    byteOffset: 34, byteLength: 16),
            };

            _store.GetSymbolsByNamesAsync("test-repo-id", Arg.Any<IReadOnlyList<string>>())
                .Returns(symbols);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 6, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbols(dir, ["A", "B", "C"]).ConfigureAwait(false);

            await Assert.That(result.Results).Count().IsEqualTo(3);
            await Assert.That(result.Errors).Count().IsEqualTo(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolsSomeMissingReturnsPartialResults()
    {
        var content = "function A()\nend\nfunction B()\nend\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbols = new List<Symbol>
            {
                CreateSymbol(1, 1, "A", "Function", "function A()", lineStart: 1,
                    byteOffset: 0, byteLength: 16),
                CreateSymbol(2, 1, "B", "Function", "function B()", lineStart: 3,
                    byteOffset: 17, byteLength: 16),
            };

            _store.GetSymbolsByNamesAsync("test-repo-id", Arg.Any<IReadOnlyList<string>>())
                .Returns(symbols);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 4, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbols(dir, ["A", "B", "Missing"]).ConfigureAwait(false);

            await Assert.That(result.Results).Count().IsEqualTo(2);
            await Assert.That(result.Errors).Count().IsEqualTo(1);
            await Assert.That(result.Errors![0].Symbol).IsEqualTo("Missing");
            await Assert.That(result.Errors![0].Error).IsEqualTo("Symbol not found");
            await Assert.That(result.Errors![0].Code).IsEqualTo("SYMBOL_NOT_FOUND");
            await Assert.That(result.Errors![0].Guidance)
                .Contains("search_symbols").And.Contains("index_project");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolsNoneFoundReturnsAllErrors()
    {
        _store.GetSymbolsByNamesAsync("test-repo-id", Arg.Any<IReadOnlyList<string>>())
            .Returns(new List<Symbol>());

        var result = await _tools.GetSymbols("/valid/path", ["Missing1", "Missing2"]).ConfigureAwait(false);

        await Assert.That(result.Results).Count().IsEqualTo(0);
        await Assert.That(result.Errors).Count().IsEqualTo(2);
        await Assert.That(result.Errors![0].Symbol).IsEqualTo("Missing1");
        await Assert.That(result.Errors![1].Symbol).IsEqualTo("Missing2");
    }

    [Test]
    public async Task GetSymbolsSameFileGroupsReads()
    {
        var content = "function A()\nend\nfunction B()\nend\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbols = new List<Symbol>
            {
                CreateSymbol(1, 1, "A", "Function", "function A()", lineStart: 1,
                    byteOffset: 0, byteLength: 16),
                CreateSymbol(2, 1, "B", "Function", "function B()", lineStart: 3,
                    byteOffset: 17, byteLength: 16),
            };

            _store.GetSymbolsByNamesAsync("test-repo-id", Arg.Any<IReadOnlyList<string>>())
                .Returns(symbols);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord>
                {
                    new(1, "test-repo-id", fileName, "hash1", 200, 4, 1000, 2000),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbols(dir, ["A", "B"]).ConfigureAwait(false);

            await Assert.That(result.Results).Count().IsEqualTo(2);

            var byName = result.Results!.ToDictionary(r => r.Name!);
            await Assert.That(byName["A"].SourceCode).IsEqualTo("function A()\nend");
            await Assert.That(byName["B"].SourceCode).IsEqualTo("function B()\nend");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolsExceedsLimitReturnsError()
    {
        var names = Enumerable.Range(1, 51).Select(i => $"Symbol{i}").ToArray();

        var result = await _tools.GetSymbols("/valid/path", names).ConfigureAwait(false);

        await Assert.That(result.Error)
            .IsEqualTo("Too many symbols requested. Maximum is 50");
        await Assert.That(result.Code).IsEqualTo("SYMBOL_LIMIT_EXCEEDED");
    }

    [Test]
    public async Task GetSymbolsEmptyArrayReturnsError()
    {
        var result = await _tools.GetSymbols("/valid/path", []).ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("No symbol names provided");
        await Assert.That(result.Code).IsEqualTo("EMPTY_SYMBOL_NAMES");
    }

    [Test]
    public async Task GetSymbolsInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetSymbols("/../../../etc/passwd", ["SomeSymbol"]).ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    // ── SearchSymbols ────────────────────────────────────────────────

    [Test]
    public async Task SearchSymbolsSimpleQueryReturnsRankedResults()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack()", parent: "CombatService"), "src/services/CombatService.luau", 1.0),
            new(CreateSymbol(2, 1, "CalculateDamage", "Function", "function CalculateDamage()"), "src/utils/DamageCalc.luau", 0.8),
        };
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>()).Returns(searchResults);

        var result = await _tools.SearchSymbols("/valid/path", "damage").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(2);
        await Assert.That(result.Results).Count().IsEqualTo(2);
        await Assert.That(result.Results![0].Name).IsEqualTo("ProcessAttack");
        await Assert.That(result.Results![0].Rank).IsEqualTo(1);
    }

    [Test]
    public async Task SearchSymbolsWithKindFilterFiltersResults()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "ProcessAttack", "Method", "function CombatService:ProcessAttack()", parent: "CombatService"), "src/services/CombatService.luau", 1.0),
        };
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), "Method", Arg.Any<int>()).Returns(searchResults);

        var result = await _tools.SearchSymbols("/valid/path", "attack", kind: "method").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), "Method", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsKindNormalizedToPascalCase()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "WorkItem", kind: "class").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsKindUppercaseNormalized()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "WorkItem", kind: "CLASS").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsKindMixedCaseNormalized()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "WorkItem", kind: "cLaSs").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), "Class", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsInvalidKindReturnsError()
    {
        var result = await _tools.SearchSymbols("/valid/path", "damage", kind: "invalid").ConfigureAwait(false);

        await Assert.That(result.Error)
            .IsEqualTo("Invalid symbol kind. Must be one of: function, method, type, class, record, interface, export, constant, module");
        await Assert.That(result.Code).IsEqualTo("INVALID_KIND");

        await _store.DidNotReceive().SearchSymbolsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsWithLimitRespectsLimit()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 5)
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "damage", limit: 5).ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 5).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsLimitClampedAbove100()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 100)
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "damage", limit: 500).ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 100).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsEmptyQueryReturnsError()
    {
        var result = await _tools.SearchSymbols("/valid/path", "").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Search query cannot be empty");
        await Assert.That(result.Code).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task SearchSymbolsInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.SearchSymbols("/../../../etc/passwd", "damage").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task SearchSymbolsMaliciousQuerySanitized()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "name:foo ^bar").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", "foo bar", Arg.Any<string?>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    // ── SearchText ───────────────────────────────────────────────────

    [Test]
    public async Task SearchTextSimpleQueryReturnsFileMatches()
    {
        var searchResults = new List<TextSearchResult>
        {
            new("src/services/CombatService.luau", "...local damage = baseDamage * multiplier...", 1.0),
        };
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>()).Returns(searchResults);

        var result = await _tools.SearchText("/valid/path", "multiplier").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
        await Assert.That(result.Results![0].FilePath).IsEqualTo("src/services/CombatService.luau");
        await Assert.That(result.Results![0].Snippet).Contains("multiplier");
    }

    [Test]
    public async Task SearchTextWithGlobFilterFiltersFiles()
    {
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), "*.luau", Arg.Any<int>())
            .Returns(new List<TextSearchResult>());

        await _tools.SearchText("/valid/path", "damage", glob: "*.luau").ConfigureAwait(false);

        await _store.Received(1).SearchTextAsync(
            "test-repo-id", Arg.Any<string>(), "*.luau", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchTextMaliciousGlobSanitized()
    {
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>())
            .Returns(new List<TextSearchResult>());

        await _tools.SearchText("/valid/path", "damage", glob: "*.luau; DROP TABLE").ConfigureAwait(false);

        await _store.Received(1).SearchTextAsync(
            "test-repo-id", Arg.Any<string>(), "*.luauDROPTABLE", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchTextWithLimitRespectsLimit()
    {
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 10)
            .Returns(new List<TextSearchResult>());

        await _tools.SearchText("/valid/path", "damage", limit: 10).ConfigureAwait(false);

        await _store.Received(1).SearchTextAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), 10).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchTextEmptyQueryReturnsError()
    {
        var result = await _tools.SearchText("/valid/path", "  ").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Search query cannot be empty");
        await Assert.That(result.Code).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task SearchTextInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.SearchText("/../../../etc/passwd", "damage").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task SearchSymbolsWildcardOnlyQueryReturnsError()
    {
        var result = await _tools.SearchSymbols("/valid/path", "*").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Search query is too broad — provide at least one non-wildcard term");
        await Assert.That(result.Code).IsEqualTo("QUERY_TOO_BROAD");
    }

    [Test]
    public async Task SearchSymbolsWildcardWithPathFilterBrowsesAllSymbols()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%")
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.SearchSymbols("/valid/path", "*", pathFilter: "src/Core").ConfigureAwait(false);

        await Assert.That(result.Error).IsNull();
        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Core", "%").ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsPrefixGlobPassesFts5PrefixQuery()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "AddMaestro*").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", "AddMaestro*", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsSuffixGlobPassesSqlLikePattern()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Handler")
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "*Handler").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Handler").ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsContainsGlobPassesSqlLikePattern()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Maestro%")
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "*Maestro*").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Maestro%").ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsComplexGlobPassesSqlLikePattern()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "I%Service")
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "I*Service").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "I%Service").ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsWithPathFilterPassesValidatedFilter()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Core", Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "Order", pathFilter: "src/Core/").ConfigureAwait(false);

        // First call is FTS5, second is auto contains-match fallback (both with same pathFilter)
        await _store.Received(2).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Core", Arg.Any<string?>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchSymbolsInvalidPathFilterReturnsError()
    {
        var result = await _tools.SearchSymbols("/valid/path", "Order", pathFilter: "../../etc/").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Invalid path filter");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task SearchTextWithPathFilterPassesValidatedFilter()
    {
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Config")
            .Returns(new List<TextSearchResult>());

        await _tools.SearchText("/valid/path", "connectionString", pathFilter: "src/Config/").ConfigureAwait(false);

        await _store.Received(1).SearchTextAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Config").ConfigureAwait(false);
    }

    [Test]
    public async Task SearchTextInvalidPathFilterReturnsError()
    {
        var result = await _tools.SearchText("/valid/path", "damage", pathFilter: "../../etc/").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Invalid path filter");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task SearchSymbolsPlainQueryStillWorksFts5()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "OrderService", "Class", "public class OrderService"), "src/OrderService.cs", 1.0),
        };
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(searchResults);

        var result = await _tools.SearchSymbols("/valid/path", "OrderService").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
    }

    [Test]
    public async Task SearchSymbolsPathFilterScopesResultsToDirectory()
    {
        var srcSymbol = new SymbolSearchResult(
            CreateSymbol(1, 1, "ParserBase", "Class", "public class ParserBase"),
            "src/Core/Parsers/ParserBase.cs",
            1.0);
        var searchResults = new List<SymbolSearchResult> { srcSymbol };

        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src", Arg.Any<string?>())
            .Returns(searchResults);

        var result = await _tools.SearchSymbols("/valid/path", "Parser", pathFilter: "src/").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
        var firstResult = result.Results![0];
        await Assert.That(firstResult.File).IsEqualTo("src/Core/Parsers/ParserBase.cs");
        await Assert.That(firstResult.Name).IsEqualTo("ParserBase");

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src", Arg.Any<string?>()).ConfigureAwait(false);
        await _store.DidNotReceive().SearchSymbolsAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(p => p == null), Arg.Any<string?>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SearchTextPathFilterScopesResultsToDirectory()
    {
        var textResults = new List<TextSearchResult>
        {
            new("src/Config/Settings.cs", "var conn = \"Server=localhost\";", 1.0),
        };

        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Config")
            .Returns(textResults);

        var result = await _tools.SearchText("/valid/path", "connectionString", pathFilter: "src/Config/").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
        var firstResult = result.Results![0];
        await Assert.That(firstResult.FilePath).IsEqualTo("src/Config/Settings.cs");

        await _store.Received(1).SearchTextAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), "src/Config").ConfigureAwait(false);
    }

    // ── TopicOutline Tests (unchanged: still returns Markdown) ────────

    [Test]
    public async Task TopicOutlineValidTopicReturnsStructuredOutline()
    {
        var outline = new Core.Models.ProjectOutline(
            "test-repo-id",
            [
                new Core.Models.OutlineGroup(
                    "src/services/Auth.cs",
                    [CreateSymbol(1, 1, "AuthService", "Class", "public class AuthService")],
                    []),
            ],
            1,
            false);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(outline);

        var result = await _tools.TopicOutline("/valid/path", "authentication").ConfigureAwait(false);

        await Assert.That(result).Contains("AuthService");
        await Assert.That(result).Contains("src/services/Auth.cs");
    }

    [Test]
    public async Task TopicOutlineEmptyQueryReturnsError()
    {
        var result = await _tools.TopicOutline("/valid/path", "").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task TopicOutlineWhitespaceQueryReturnsError()
    {
        var result = await _tools.TopicOutline("/valid/path", "   ").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task TopicOutlineInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Throws(new ArgumentException("bad path"));

        var result = await _tools.TopicOutline("/bad/path", "auth").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task TopicOutlineClampsLimitTo200Max()
    {
        var outline = new Core.Models.ProjectOutline("test-repo-id", [], 0, false);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(outline);

        await _tools.TopicOutline("/valid/path", "test", maxResults: 999).ConfigureAwait(false);

        await _store.Received(1).SearchTopicOutlineAsync(
            "test-repo-id", Arg.Any<string>(), 200, Arg.Any<string?>()).ConfigureAwait(false);
    }

    [Test]
    public async Task TopicOutlineClampsLimitTo1Min()
    {
        var outline = new Core.Models.ProjectOutline("test-repo-id", [], 0, false);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(outline);

        await _tools.TopicOutline("/valid/path", "test", maxResults: -5).ConfigureAwait(false);

        await _store.Received(1).SearchTopicOutlineAsync(
            "test-repo-id", Arg.Any<string>(), 1, Arg.Any<string?>()).ConfigureAwait(false);
    }

    [Test]
    public async Task TopicOutlinePassesPathFilterToStore()
    {
        var outline = new Core.Models.ProjectOutline("test-repo-id", [], 0, false);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), "src/services")
            .Returns(outline);

        await _tools.TopicOutline("/valid/path", "auth", pathFilter: "src/services/").ConfigureAwait(false);

        await _store.Received(1).SearchTopicOutlineAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<int>(), "src/services").ConfigureAwait(false);
    }

    [Test]
    public async Task TopicOutlineInvalidPathFilterReturnsError()
    {
        var result = await _tools.TopicOutline("/valid/path", "auth", pathFilter: "../../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task TopicOutlineShowsTruncationMessage()
    {
        var outline = new Core.Models.ProjectOutline(
            "test-repo-id",
            [
                new Core.Models.OutlineGroup(
                    "src/Auth.cs",
                    [CreateSymbol(1, 1, "Login", "Method", "public void Login()")],
                    []),
            ],
            100,
            true);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(outline);

        var result = await _tools.TopicOutline("/valid/path", "auth").ConfigureAwait(false);

        await Assert.That(result).Contains("Truncated");
        await Assert.That(result).Contains("100");
    }

    [Test]
    public async Task TopicOutlineFts5ErrorRetriesWithLiteralPhrase()
    {
        var outline = new Core.Models.ProjectOutline("test-repo-id", [], 0, false);

        _store.SearchTopicOutlineAsync("test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(
                callInfo => throw new Microsoft.Data.Sqlite.SqliteException("fts5 error", 1),
                callInfo => outline);

        await _tools.TopicOutline("/valid/path", "auth:bad").ConfigureAwait(false);

        await _store.Received(2).SearchTopicOutlineAsync(
            "test-repo-id", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>()).ConfigureAwait(false);
    }

    private static Symbol CreateSymbol(
        long id,
        long fileId,
        string name,
        string kind,
        string signature,
        string visibility = "Public",
        string? parent = null,
        int lineStart = 1,
        string? docComment = null,
        int byteOffset = 0,
        int byteLength = 100) =>
        new(id, fileId, name, kind, signature, parent, byteOffset, byteLength, lineStart, lineStart + 5, visibility, docComment, null, null);

    private static string CreateTempFile(string content)
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, content, new UTF8Encoding(false));
        return tempPath;
    }

    // ── GetHotPath Tests ─────────────────────────────────────────────

    [Test]
    public async Task GetHotPathSingleIdentifierMatchesWithContext()
    {
        var lines = new[] { "line1", "line2", "line3", "line4", "has userId here", "line6", "line7", "line8", "line9", "line10" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "ProcessPayment", "Method", "void ProcessPayment()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 10, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "ProcessPayment").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 10, 1000, 2000) });

            var result = await _tools.GetHotPath(dir, "ProcessPayment", ["userId"], contextLines: 2).ConfigureAwait(false);

            await Assert.That(result.Symbol).IsEqualTo("ProcessPayment");
            await Assert.That(result.TotalLines).IsEqualTo(10);
            await Assert.That(result.ReturnedLines).IsEqualTo(5); // lines 3-7

            await Assert.That(result.Matches).Count().IsEqualTo(1);
            var match = result.Matches![0];
            await Assert.That(match.Identifier).IsEqualTo("userId");
            await Assert.That(match.Line).IsEqualTo(5);
            await Assert.That(match.Context).Count().IsEqualTo(5);
            await Assert.That(match.Context![0].LineNumber).IsEqualTo(3);
            await Assert.That(match.Context![4].LineNumber).IsEqualTo(7);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathMultipleIdentifiersReturnAllMatches()
    {
        var lines = new[] { "line1", "has userId here", "line3", "line4", "has status Pending here", "line6", "line7" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 7, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 7, 1000, 2000) });

            // userId at line 2 → window [1,3]; status at line 5 → window [4,6]; no overlap
            var result = await _tools.GetHotPath(dir, "Process", ["userId", "status"], contextLines: 1).ConfigureAwait(false);

            await Assert.That(result.ReturnedLines).IsEqualTo(6);
            await Assert.That(result.Matches).Count().IsEqualTo(2);

            var match0 = result.Matches![0];
            await Assert.That(match0.Identifier).IsEqualTo("userId");
            await Assert.That(match0.Line).IsEqualTo(2);
            await Assert.That(match0.Context).Count().IsEqualTo(3);

            var match1 = result.Matches![1];
            await Assert.That(match1.Identifier).IsEqualTo("status");
            await Assert.That(match1.Line).IsEqualTo(5);
            await Assert.That(match1.Context).Count().IsEqualTo(3);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathOverlappingContextMergedNoDuplicateLines()
    {
        var lines = new[] { "line1", "line2", "line3", "line4", "has userId here", "line6", "has status Pending here", "line8", "line9", "line10" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 10, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 10, 1000, 2000) });

            // userId at line 5 → window [2,8]; status at line 7 → window [4,10]; merged [2,10] = 9 lines
            var result = await _tools.GetHotPath(dir, "Process", ["userId", "status"], contextLines: 3).ConfigureAwait(false);

            await Assert.That(result.ReturnedLines).IsEqualTo(9);
            await Assert.That(result.Matches).Count().IsEqualTo(2);

            // First match (userId at line 5) gets the full merged context [2-10]
            var firstMatch = result.Matches![0];
            await Assert.That(firstMatch.Identifier).IsEqualTo("userId");
            await Assert.That(firstMatch.Context).Count().IsEqualTo(9);

            // Second match (status at line 7) gets empty context — already covered by merged window
            var secondMatch = result.Matches![1];
            await Assert.That(secondMatch.Identifier).IsEqualTo("status");
            await Assert.That(secondMatch.Context).Count().IsEqualTo(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathWholeWordMatchingOnly()
    {
        var lines = new[] { "has userIdHash here", "has userId here" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 2, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 2, 1000, 2000) });

            var result = await _tools.GetHotPath(dir, "Process", ["userId"], contextLines: 0).ConfigureAwait(false);

            await Assert.That(result.Matches).Count().IsEqualTo(1); // line 1 (userIdHash) NOT matched
            await Assert.That(result.Matches![0].Line).IsEqualTo(2);
            await Assert.That(result.ReturnedLines).IsEqualTo(1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathNoMatchesReturnsEmptyMatches()
    {
        var content = "line1\nline2\nline3\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 3, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 3, 1000, 2000) });

            var result = await _tools.GetHotPath(dir, "Process", ["userId"], contextLines: 3).ConfigureAwait(false);

            await Assert.That(result.Matches).Count().IsEqualTo(0);
            await Assert.That(result.ReturnedLines).IsEqualTo(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathContextLinesDefaultsToThree()
    {
        var lines = new[] { "line1", "line2", "line3", "line4", "has userId here", "line6", "line7", "line8", "line9", "line10" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 10, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 10, 1000, 2000) });

            // Default contextLines=3 → window [max(1,5-3), min(10,5+3)] = [2,8] = 7 lines
            var result = await _tools.GetHotPath(dir, "Process", ["userId"]).ConfigureAwait(false);

            await Assert.That(result.ReturnedLines).IsEqualTo(7);
            var context = result.Matches![0].Context;
            await Assert.That(context).Count().IsEqualTo(7);
            await Assert.That(context![0].LineNumber).IsEqualTo(2);
            await Assert.That(context[6].LineNumber).IsEqualTo(8);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathContextLinesClamped()
    {
        var content = "line1\nhas userId here\nline3\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 3, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 3, 1000, 2000) });

            // contextLines=15 → clamped to 10 → window [max(1,2-10), min(3,2+10)] = [1,3] = 3 lines
            var result = await _tools.GetHotPath(dir, "Process", ["userId"], contextLines: 15).ConfigureAwait(false);

            await Assert.That(result.ReturnedLines).IsEqualTo(3);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathSymbolNotFoundReturnsError()
    {
        _store.GetSymbolByNameAsync("test-repo-id", "NonExistentMethod").Returns((Symbol?)null);
        _store.GetSymbolCandidatesByNameAsync("test-repo-id", "NonExistentMethod", Arg.Any<int>())
            .Returns(new List<Symbol>());

        var result = await _tools.GetHotPath("/valid/path", "NonExistentMethod", ["userId"]).ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("SYMBOL_NOT_FOUND");
    }

    [Test]
    public async Task GetHotPathInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal"));

        var result = await _tools.GetHotPath("../../etc/passwd", "Method", ["userId"]).ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task GetHotPathRegexInjectionPrevented()
    {
        // "user.id" with unescaped regex (. = any char) would also match "user_id"
        // Regex.Escape ensures only the literal "user.id" is matched
        var lines = new[] { "user_id = 5", "user.id = 5" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "Process", "Method", "void Process()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 2, "Public", null, null, null);

            _store.GetSymbolByNameAsync("test-repo-id", "Process").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 2, 1000, 2000) });

            var result = await _tools.GetHotPath(dir, "Process", ["user.id"], contextLines: 0).ConfigureAwait(false);

            // Without Regex.Escape, "user.id" as regex (. = any char) would match "user_id" too
            // With Regex.Escape, only the literal "user.id" on line 2 matches
            await Assert.That(result.Matches).Count().IsEqualTo(1);
            await Assert.That(result.Matches![0].Line).IsEqualTo(2);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetHotPathUsesBodyLineRangeWhenAvailable()
    {
        // Symbol spans lines 1-6; "userId" appears on line 1 (signature) and line 4 (body)
        // BodyLineStart=2, BodyLineEnd=5 → only body lines scanned; line 1 excluded
        var lines = new[] { "void userId()", "{", "line3", "has userId here", "line5", "}" };
        var content = string.Join("\n", lines) + "\n";
        var tempFile = CreateTempFile(content);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);
            var symbol = new Symbol(1, 1, "UserId", "Method", "void userId()",
                null, 0, Encoding.UTF8.GetByteCount(content), 1, 6, "Public", null, 2, 5);

            _store.GetSymbolByNameAsync("test-repo-id", "UserId").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 6, 1000, 2000) });

            var result = await _tools.GetHotPath(dir, "UserId", ["userId"], contextLines: 0).ConfigureAwait(false);

            await Assert.That(result.TotalLines).IsEqualTo(4); // body is lines 2-5
            await Assert.That(result.Matches).Count().IsEqualTo(1);
            await Assert.That(result.Matches![0].Line).IsEqualTo(4); // body match only
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Mixed strategy error tests ───────────────────────────────────

    [Test]
    public async Task SearchSymbolsMixedPatternReturnsLlmFriendlyError()
    {
        var result = await _tools.SearchSymbols("/valid/path", "Claude* OR *Service").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("MIXED_PATTERN");
        await Assert.That(result.Suggestion).Contains("MUST split");

        // Should include ready-to-use query suggestions
        await Assert.That(result.Suggestions).Count().IsGreaterThanOrEqualTo(2);
        await Assert.That(result.Suggestions![0]).IsEqualTo("Claude*");
        await Assert.That(result.Suggestions![1]).IsEqualTo("*Service");
    }

    [Test]
    public async Task SearchSymbolsCompoundPrefixRoutesFts5()
    {
        _store.SearchSymbolsAsync("test-repo-id", "Claude* OR Agent*", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        await _tools.SearchSymbols("/valid/path", "Claude* OR Agent*").ConfigureAwait(false);

        await _store.Received(1).SearchSymbolsAsync(
            "test-repo-id", "Claude* OR Agent*", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>()).ConfigureAwait(false);
    }

    // ── Contains-match fallback tests ──────────────────────────────────

    [Test]
    public async Task SearchSymbolsPlainTermFallsBackToContainsOnZeroResults()
    {
        // First FTS5 call returns empty, fallback with *Validator* returns results
        var fallbackResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
            new(CreateSymbol(2, 1, "IPathValidator", "Interface", "public interface IPathValidator"), "src/Validation/IPathValidator.cs", 0.9),
        };

        _store.SearchSymbolsAsync("test-repo-id", "Validator", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Validator%")
            .Returns(fallbackResults);

        var result = await _tools.SearchSymbols("/valid/path", "Validator").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(2);
        await Assert.That(result.FallbackUsed).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsExactMatchDoesNotTriggerFallback()
    {
        var directResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
        };

        _store.SearchSymbolsAsync("test-repo-id", "PathValidator", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(directResults);

        var result = await _tools.SearchSymbols("/valid/path", "PathValidator").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
        await Assert.That(result.FallbackUsed).IsNull();
    }

    [Test]
    public async Task SearchSymbolsFallbackPreservesKindAndPathFilter()
    {
        _store.SearchSymbolsAsync("test-repo-id", "Validator", "Class", 20, "src", Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), "Class", 20, "src", "%Validator%")
            .Returns(new List<SymbolSearchResult>
            {
                new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
            });

        var result = await _tools.SearchSymbols("/valid/path", "Validator", kind: "class", pathFilter: "src/").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
        await Assert.That(result.FallbackUsed).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsNoResultsAfterFallbackReturnsEmpty()
    {
        _store.SearchSymbolsAsync("test-repo-id", "NonExistent", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%NonExistent%")
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.SearchSymbols("/valid/path", "NonExistent").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(0);
        await Assert.That(result.FallbackUsed).IsNull();
    }

    [Test]
    public async Task SearchSymbolsWildcardQueryDoesNotTriggerFallback()
    {
        // *Handler already has wildcards — should NOT trigger fallback
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), "%Handler")
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.SearchSymbols("/valid/path", "*Handler").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(0);
        await Assert.That(result.FallbackUsed).IsNull();
    }

    [Test]
    public async Task SearchSymbolsFts5OperatorQueryDoesNotTriggerFallback()
    {
        _store.SearchSymbolsAsync("test-repo-id", "damage OR health", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.SearchSymbols("/valid/path", "damage OR health").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(0);
        await Assert.That(result.FallbackUsed).IsNull();
    }

    // ── Size guard tests ────────────────────────────────────────────────

    [Test]
    public async Task GetSymbolLargeSymbolWithChildrenReturnsGuidedSummary()
    {
        // Create a large source file (>16KB)
        var largeContent = new string('x', 20_000);
        var tempFile = CreateTempFile(largeContent);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbol = CreateSymbol(1, 1, "BigClass", "Class",
                "public class BigClass", lineStart: 1, byteOffset: 0, byteLength: 20_000);

            _store.GetSymbolByNameAsync("test-repo-id", "BigClass").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 20_000, 100, 1000, 2000) });
            _store.GetChildSymbolsAsync("test-repo-id", "BigClass")
                .Returns(new List<Symbol>
                {
                    CreateSymbol(2, 1, "MethodA", "Method", "void MethodA()", parent: "BigClass", lineStart: 5),
                    CreateSymbol(3, 1, "MethodB", "Method", "void MethodB()", parent: "BigClass", lineStart: 15),
                });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "BigClass").ConfigureAwait(false);

            await Assert.That(result.Truncated).IsTrue();
            await Assert.That(result.Name).IsEqualTo("BigClass");
            await Assert.That(result.Guidance).Contains("expand_symbol");
            await Assert.That(result.Children).Count().IsEqualTo(2);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolLargeSymbolWithForceReturnsFullSource()
    {
        var largeContent = new string('y', 20_000);
        var tempFile = CreateTempFile(largeContent);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbol = CreateSymbol(1, 1, "BigClass", "Class",
                "public class BigClass", lineStart: 1, byteOffset: 0, byteLength: 20_000);

            _store.GetSymbolByNameAsync("test-repo-id", "BigClass").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 20_000, 100, 1000, 2000) });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "BigClass", force: true).ConfigureAwait(false);

            await Assert.That(result.Truncated).IsNull();
            await Assert.That(result.SourceCode).IsEqualTo(largeContent);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolLargeSymbolWithNoChildrenReturnsFullSource()
    {
        var largeContent = new string('z', 20_000);
        var tempFile = CreateTempFile(largeContent);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbol = CreateSymbol(1, 1, "BigFunction", "Function",
                "function BigFunction()", lineStart: 1, byteOffset: 0, byteLength: 20_000);

            _store.GetSymbolByNameAsync("test-repo-id", "BigFunction").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 20_000, 100, 1000, 2000) });
            _store.GetChildSymbolsAsync("test-repo-id", "BigFunction")
                .Returns(new List<Symbol>());

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "BigFunction").ConfigureAwait(false);

            await Assert.That(result.Truncated).IsNull();
            await Assert.That(result.SourceCode).IsEqualTo(largeContent);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetSymbolSmallSymbolReturnsFullSource()
    {
        var smallContent = "public class SmallClass { }";
        var tempFile = CreateTempFile(smallContent);
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileName(tempFile);

            var symbol = CreateSymbol(1, 1, "SmallClass", "Class",
                "public class SmallClass", lineStart: 1, byteOffset: 0,
                byteLength: Encoding.UTF8.GetByteCount(smallContent));

            _store.GetSymbolByNameAsync("test-repo-id", "SmallClass").Returns(symbol);
            _store.GetFilesByRepoAsync("test-repo-id")
                .Returns(new List<FileRecord> { new(1, "test-repo-id", fileName, "hash1", 100, 1, 1000, 2000) });

            _pathValidator.ValidatePath(dir, dir).Returns(dir);

            var result = await _tools.GetSymbol(dir, "SmallClass").ConfigureAwait(false);

            await Assert.That(result.Truncated).IsNull();
            await Assert.That(result.SourceCode).IsEqualTo(smallContent);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
