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
        var outline = new ProjectOutline("test-repo-id", [dirGroup]);

        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>()).Returns(outline);

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
        var outline = new ProjectOutline("test-repo-id", [servicesDir, utilsDir]);

        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>()).Returns(outline);

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
        var outline = new ProjectOutline("test-repo-id", [classesGroup, methodsGroup]);

        _store.GetProjectOutlineAsync("test-repo-id", false, "kind", Arg.Any<int>()).Returns(outline);

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
        var outline = new ProjectOutline("test-repo-id", [dirGroup]);

        _store.GetProjectOutlineAsync("test-repo-id", false, "directory", Arg.Any<int>()).Returns(outline);

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
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineIncludePrivateFalseOmitsPrivateSymbols()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", includePrivate: false).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineIncludePrivateTrueIncludesAllSymbols()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", true, "file", Arg.Any<int>()).Returns(outline);

        await _tools.ProjectOutline("/valid/path", includePrivate: true).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", true, "file", Arg.Any<int>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlineMaxDepthLimitsTraversal()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", 1).Returns(outline);

        await _tools.ProjectOutline("/valid/path", maxDepth: 1).ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", 1).ConfigureAwait(false);
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
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), Arg.Any<string?>()).Returns(outline);

        var result = await _tools.ProjectOutline(distinctivePath).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(distinctivePath);
    }

    [Test]
    public async Task ProjectOutlinePathFilterPassesToStore()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), "src/services").Returns(outline);

        await _tools.ProjectOutline("/valid/path", pathFilter: "src/services").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), "src/services").ConfigureAwait(false);
    }

    [Test]
    public async Task ProjectOutlinePathFilterNullPassesNullToStore()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>(), null).Returns(outline);

        await _tools.ProjectOutline("/valid/path").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "file", Arg.Any<int>(), null).ConfigureAwait(false);
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
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>()).Returns(outline);

        var result = await _tools.ProjectOutline("/valid/path", pathFilter: maliciousFilter).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(maliciousFilter);
    }

    [Test]
    public async Task ProjectOutlinePathFilterCombinesWithGroupBy()
    {
        var outline = new ProjectOutline("test-repo-id", []);
        _store.GetProjectOutlineAsync("test-repo-id", false, "kind", Arg.Any<int>(), "src/services").Returns(outline);

        await _tools.ProjectOutline("/valid/path", groupBy: "kind", pathFilter: "src/services").ConfigureAwait(false);

        await _store.Received(1).GetProjectOutlineAsync(
            "test-repo-id", false, "kind", Arg.Any<int>(), "src/services").ConfigureAwait(false);
    }

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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("module").GetString()).IsEqualTo("src/services/CombatService.luau");

        var symbolsArray = root.GetProperty("symbols");
        await Assert.That(symbolsArray.GetArrayLength()).IsEqualTo(2);

        var firstSymbol = symbolsArray[0];
        await Assert.That(firstSymbol.GetProperty("name").GetString()).IsEqualTo("CombatService");
        await Assert.That(firstSymbol.GetProperty("kind").GetString()).IsEqualTo("Class");
        await Assert.That(firstSymbol.GetProperty("signature").GetString()).IsEqualTo("local CombatService = {} :: CombatService");
        await Assert.That(firstSymbol.GetProperty("line").GetInt32()).IsEqualTo(1);
        await Assert.That(firstSymbol.GetProperty("doc_comment").GetString()).IsEqualTo("Combat service module");

        var secondSymbol = symbolsArray[1];
        await Assert.That(secondSymbol.GetProperty("name").GetString()).IsEqualTo("ProcessAttack");
        await Assert.That(secondSymbol.GetProperty("kind").GetString()).IsEqualTo("Method");

        var depsArray = root.GetProperty("dependencies");
        await Assert.That(depsArray.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task GetModuleApiNonExistentModuleReturnsError()
    {
        _store.GetModuleApiAsync("test-repo-id", "src/nonexistent.luau")
            .Throws(new FileNotFoundException("Module not found"));

        var result = await _tools.GetModuleApi("/valid/path", "src/nonexistent.luau").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Module not found");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("MODULE_NOT_FOUND");
    }

    [Test]
    public async Task GetModuleApiTraversalModulePathReturnsError()
    {
        _pathValidator.ValidateRelativePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetModuleApi("/valid/path", "../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task GetModuleApiInvalidProjectPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetModuleApi("/../invalid", "src/module.luau").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var depsArray = root.GetProperty("dependencies");
        await Assert.That(depsArray.GetArrayLength()).IsEqualTo(2);

        await Assert.That(depsArray[0].GetProperty("requires_path").GetString()).IsEqualTo("src/utils/MathUtils");
        await Assert.That(depsArray[1].GetProperty("requires_path").GetString()).IsEqualTo("src/utils/DamageCalc");
        await Assert.That(depsArray[1].GetProperty("alias").GetString()).IsEqualTo("Damage");
    }

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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("ProcessAttack");
            await Assert.That(root.GetProperty("kind").GetString()).IsEqualTo("Method");
            await Assert.That(root.GetProperty("parent").GetString()).IsEqualTo("CombatService");
            await Assert.That(root.GetProperty("file").GetString()).IsEqualTo(fileName);
            await Assert.That(root.GetProperty("line_start").GetInt32()).IsEqualTo(3);
            await Assert.That(root.GetProperty("line_end").GetInt32()).IsEqualTo(8);
            await Assert.That(root.GetProperty("signature").GetString())
                .IsEqualTo("function CombatService:ProcessAttack()");
            await Assert.That(root.GetProperty("source_code").GetString())
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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var sourceCode = root.GetProperty("source_code").GetString()!;

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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var sourceCode = root.GetProperty("source_code").GetString()!;
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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var sourceCode = root.GetProperty("source_code").GetString()!;
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Symbol not found");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("SYMBOL_NOT_FOUND");
    }

    [Test]
    public async Task GetSymbolInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetSymbol("/../../../etc/passwd", "SomeSymbol").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            await Assert.That(root.GetProperty("source_code").GetString()).IsEqualTo(expectedSource);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var results = root.GetProperty("results");
            await Assert.That(results.GetArrayLength()).IsEqualTo(3);

            var errors = root.GetProperty("errors");
            await Assert.That(errors.GetArrayLength()).IsEqualTo(0);
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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var results = root.GetProperty("results");
            await Assert.That(results.GetArrayLength()).IsEqualTo(2);

            var errors = root.GetProperty("errors");
            await Assert.That(errors.GetArrayLength()).IsEqualTo(1);
            await Assert.That(errors[0].GetProperty("symbol").GetString()).IsEqualTo("Missing");
            await Assert.That(errors[0].GetProperty("error").GetString()).IsEqualTo("Symbol not found");
            await Assert.That(errors[0].GetProperty("code").GetString()).IsEqualTo("SYMBOL_NOT_FOUND");
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var results = root.GetProperty("results");
        await Assert.That(results.GetArrayLength()).IsEqualTo(0);

        var errors = root.GetProperty("errors");
        await Assert.That(errors.GetArrayLength()).IsEqualTo(2);
        await Assert.That(errors[0].GetProperty("symbol").GetString()).IsEqualTo("Missing1");
        await Assert.That(errors[1].GetProperty("symbol").GetString()).IsEqualTo("Missing2");
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

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var results = root.GetProperty("results");
            await Assert.That(results.GetArrayLength()).IsEqualTo(2);

            await Assert.That(results[0].GetProperty("name").GetString()).IsEqualTo("A");
            await Assert.That(results[0].GetProperty("source_code").GetString())
                .IsEqualTo("function A()\nend");
            await Assert.That(results[1].GetProperty("name").GetString()).IsEqualTo("B");
            await Assert.That(results[1].GetProperty("source_code").GetString())
                .IsEqualTo("function B()\nend");
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString())
            .IsEqualTo("Too many symbols requested. Maximum is 50");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("SYMBOL_LIMIT_EXCEEDED");
    }

    [Test]
    public async Task GetSymbolsEmptyArrayReturnsError()
    {
        var result = await _tools.GetSymbols("/valid/path", []).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString())
            .IsEqualTo("No symbol names provided");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("EMPTY_SYMBOL_NAMES");
    }

    [Test]
    public async Task GetSymbolsInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.GetSymbols("/../../../etc/passwd", ["SomeSymbol"]).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("total_matches").GetInt32()).IsEqualTo(2);
        var results = root.GetProperty("results");
        await Assert.That(results.GetArrayLength()).IsEqualTo(2);
        await Assert.That(results[0].GetProperty("name").GetString()).IsEqualTo("ProcessAttack");
        await Assert.That(results[0].GetProperty("rank").GetInt32()).IsEqualTo(1);
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("total_matches").GetInt32()).IsEqualTo(1);

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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString())
            .IsEqualTo("Invalid symbol kind. Must be one of: function, method, type, class, interface, export, constant, module");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_KIND");

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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Search query cannot be empty");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task SearchSymbolsInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.SearchSymbols("/../../../etc/passwd", "damage").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
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

    [Test]
    public async Task SearchTextSimpleQueryReturnsFileMatches()
    {
        var searchResults = new List<TextSearchResult>
        {
            new("src/services/CombatService.luau", "...local damage = baseDamage * multiplier...", 1.0),
        };
        _store.SearchTextAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>()).Returns(searchResults);

        var result = await _tools.SearchText("/valid/path", "multiplier").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("total_matches").GetInt32()).IsEqualTo(1);
        var results = root.GetProperty("results");
        await Assert.That(results[0].GetProperty("file_path").GetString()).IsEqualTo("src/services/CombatService.luau");
        await Assert.That(results[0].GetProperty("snippet").GetString()).Contains("multiplier");
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

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Search query cannot be empty");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("EMPTY_QUERY");
    }

    [Test]
    public async Task SearchTextInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.SearchText("/../../../etc/passwd", "damage").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
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
        new(id, fileId, name, kind, signature, parent, byteOffset, byteLength, lineStart, lineStart + 5, visibility, docComment);

    private static string CreateTempFile(string content)
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, content, new UTF8Encoding(false));
        return tempPath;
    }
}
