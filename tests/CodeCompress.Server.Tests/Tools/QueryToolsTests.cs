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
        _store.GetProjectOutlineAsync("test-repo-id", false, "file", Arg.Any<int>()).Returns(outline);

        var result = await _tools.ProjectOutline(distinctivePath).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(distinctivePath);
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

    private static Symbol CreateSymbol(
        long id,
        long fileId,
        string name,
        string kind,
        string signature,
        string visibility = "Public",
        string? parent = null,
        int lineStart = 1,
        string? docComment = null) =>
        new(id, fileId, name, kind, signature, parent, 0, 100, lineStart, lineStart + 5, visibility, docComment);
}
