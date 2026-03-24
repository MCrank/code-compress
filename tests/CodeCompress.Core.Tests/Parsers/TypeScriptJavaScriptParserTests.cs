using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class TypeScriptJavaScriptParserTests
{
    private readonly TypeScriptJavaScriptParser _parser = new();

    private ParseResult Parse(string code, string ext = ".ts") =>
        _parser.Parse($"test{ext}", Encoding.UTF8.GetBytes(code));

    [Test]
    public async Task LanguageIdIsTypeScript()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("typescript");
    }

    [Test]
    [Arguments(".ts")]
    [Arguments(".tsx")]
    [Arguments(".js")]
    [Arguments(".jsx")]
    [Arguments(".mjs")]
    [Arguments(".cjs")]
    public async Task FileExtensionsContainsExpected(string ext)
    {
        await Assert.That(_parser.FileExtensions).Contains(ext);
    }

    [Test]
    public async Task EmptyContentReturnsEmpty()
    {
        var result = _parser.Parse("test.ts", ReadOnlySpan<byte>.Empty);
        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Imports ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesEsmImport()
    {
        var result = Parse("""import { User } from "./models/user";""");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("./models/user");
    }

    [Test]
    public async Task ParsesRequire()
    {
        var result = Parse("""const { helper } = require("./utils");""", ".js");
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("./utils");
    }

    // ── Classes ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesExportedClass()
    {
        var code = """
            export class UserService {
            }
            """;
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "UserService");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesAbstractClass()
    {
        var code = """
            export abstract class BaseEntity {
            }
            """;
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "BaseEntity");
        await Assert.That(cls.Signature).Contains("abstract");
    }

    [Test]
    public async Task ParsesClassExtendsImplements()
    {
        var code = """
            export class User extends BaseEntity implements Auditable {
            }
            """;
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "User");
        await Assert.That(cls.Signature).Contains("extends BaseEntity");
        await Assert.That(cls.Signature).Contains("implements Auditable");
    }

    [Test]
    public async Task NonExportedClassIsPrivate()
    {
        var code = """
            class Internal {
            }
            """;
        var result = Parse(code);
        await Assert.That(result.Symbols[0].Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Interfaces ────────────────────────────────────────────────────

    [Test]
    public async Task ParsesInterface()
    {
        var code = """
            export interface Identifiable {
                readonly id: string;
            }
            """;
        var result = Parse(code);
        var iface = result.Symbols.First(s => s.Name == "Identifiable");
        await Assert.That(iface.Kind).IsEqualTo(SymbolKind.Interface);
        await Assert.That(iface.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesGenericInterface()
    {
        var code = """
            export interface Repository<T, ID = string> {
                findById(id: ID): Promise<T | null>;
            }
            """;
        var result = Parse(code);
        var repo = result.Symbols.First(s => s.Name == "Repository");
        await Assert.That(repo.Kind).IsEqualTo(SymbolKind.Interface);
        await Assert.That(repo.Signature).Contains("<T, ID = string>");
    }

    // ── Enums ─────────────────────────────────────────────────────────

    [Test]
    public async Task ParsesEnum()
    {
        var code = """
            export enum UserRole {
                Guest = "guest",
                Member = "member",
            }
            """;
        var result = Parse(code);
        var enumSym = result.Symbols.First(s => s.Name == "UserRole");
        await Assert.That(enumSym.Kind).IsEqualTo(SymbolKind.Enum);
    }

    // ── Type Aliases ──────────────────────────────────────────────────

    [Test]
    public async Task ParsesTypeAlias()
    {
        var code = """export type UserResult = User | null;""";
        var result = Parse(code);
        var typeSym = result.Symbols.First(s => s.Name == "UserResult");
        await Assert.That(typeSym.Kind).IsEqualTo(SymbolKind.Type);
        await Assert.That(typeSym.Visibility).IsEqualTo(Visibility.Public);
    }

    // ── Functions ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesFunctionDeclaration()
    {
        var code = """
            export function createUser(id: string): User {
                return new User(id);
            }
            """;
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "createUser");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(fn.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesArrowFunction()
    {
        var code = """export const isValid = (s: string): boolean => s.length > 0;""";
        var result = Parse(code);
        var fn = result.Symbols.First(s => s.Name == "isValid");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
    }

    [Test]
    public async Task ParsesJsFunction()
    {
        var code = """
            function formatName(name) {
                return name;
            }
            """;
        var result = Parse(code, ".js");
        var fn = result.Symbols.First(s => s.Name == "formatName");
        await Assert.That(fn.Kind).IsEqualTo(SymbolKind.Function);
    }

    // ── Methods ───────────────────────────────────────────────────────

    [Test]
    public async Task ParsesClassMethod()
    {
        var code = """
            export class Service {
                async createUser(id: string): Promise<User> {
                    return new User(id);
                }
            }
            """;
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "createUser");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Service");
    }

    [Test]
    public async Task ParsesConstructor()
    {
        var code = """
            export class User {
                constructor(public name: string) {
                }
            }
            """;
        var result = Parse(code);
        var ctor = result.Symbols.First(s => s.Name == "constructor");
        await Assert.That(ctor.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(ctor.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesInterfaceMethod()
    {
        var code = """
            export interface Auditable {
                getAuditId(): string;
            }
            """;
        var result = Parse(code);
        var method = result.Symbols.First(s => s.Name == "getAuditId");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Auditable");
    }

    // ── Constants ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesExportedConst()
    {
        var code = """export const MAX_LENGTH = 255;""";
        var result = Parse(code);
        var c = result.Symbols.First(s => s.Name == "MAX_LENGTH");
        await Assert.That(c.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(c.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesNonExportedConst()
    {
        var code = """const INTERNAL_VERSION = "1.0.0";""";
        var result = Parse(code);
        var c = result.Symbols.First(s => s.Name == "INTERNAL_VERSION");
        await Assert.That(c.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── JSDoc ─────────────────────────────────────────────────────────

    [Test]
    public async Task CapturesJsDoc()
    {
        var code = """
            /** Base entity for all domain objects. */
            export class BaseEntity {
            }
            """;
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "BaseEntity");
        await Assert.That(cls.DocComment).IsNotNull();
        await Assert.That(cls.DocComment!).Contains("Base entity");
    }

    // ── Line Ranges ───────────────────────────────────────────────────

    [Test]
    public async Task ClassSpansCorrectLines()
    {
        var code = """
            export class Service {
                run(): void {
                }
            }
            """;
        var result = Parse(code);
        var cls = result.Symbols.First(s => s.Name == "Service");
        await Assert.That(cls.LineStart).IsEqualTo(1);
        await Assert.That(cls.LineEnd).IsEqualTo(4);
    }

    // ── JS CommonJS class ─────────────────────────────────────────────

    [Test]
    public async Task ParsesJsClass()
    {
        var code = """
            class PageResult {
                constructor(items, total) {
                    this.items = items;
                    this.total = total;
                }
            }
            """;
        var result = Parse(code, ".js");
        var cls = result.Symbols.First(s => s.Name == "PageResult");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
    }
}
