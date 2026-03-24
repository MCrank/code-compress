using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class JavaParserTests
{
    private readonly JavaParser _parser = new();

    private ParseResult Parse(string code) =>
        _parser.Parse("Test.java", Encoding.UTF8.GetBytes(code));

    [Test]
    public async Task LanguageIdIsJava()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("java");
    }

    [Test]
    public async Task FileExtensionsContainsJava()
    {
        await Assert.That(_parser.FileExtensions).Contains(".java");
    }

    [Test]
    public async Task EmptyContentReturnsEmptyResult()
    {
        var result = _parser.Parse("Test.java", ReadOnlySpan<byte>.Empty);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Package and Import ────────────────────────────────────────────

    [Test]
    public async Task ParsesPackageDeclaration()
    {
        var result = Parse("package com.example.models;");

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("com.example.models");
    }

    [Test]
    public async Task ParsesImportStatement()
    {
        var result = Parse("import java.util.List;");

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("java.util.List");
    }

    [Test]
    public async Task ParsesStaticImport()
    {
        var result = Parse("import static java.util.Collections.emptyList;");

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("java.util.Collections.emptyList");
    }

    [Test]
    public async Task ParsesWildcardImport()
    {
        var result = Parse("import java.util.*;");

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("java.util.*");
    }

    // ── Class Declarations ────────────────────────────────────────────

    [Test]
    public async Task ParsesPublicClass()
    {
        var code = """
            public class UserService {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("UserService");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(result.Symbols[0].Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesAbstractClass()
    {
        var code = """
            public abstract class BaseEntity {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("BaseEntity");
        await Assert.That(result.Symbols[0].Signature).Contains("abstract");
    }

    [Test]
    public async Task ParsesClassExtendsImplements()
    {
        var code = """
            public final class User extends BaseEntity implements Auditable {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).Contains("extends BaseEntity");
        await Assert.That(result.Symbols[0].Signature).Contains("implements Auditable");
    }

    [Test]
    public async Task ParsesGenericClass()
    {
        var code = """
            public class Repository<T, ID> {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).Contains("<T, ID>");
    }

    // ── Interface Declarations ────────────────────────────────────────

    [Test]
    public async Task ParsesInterface()
    {
        var code = """
            public interface Auditable {
                String getAuditIdentifier();
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols.Count).IsGreaterThanOrEqualTo(1);
        var iface = result.Symbols.First(s => s.Name == "Auditable");
        await Assert.That(iface.Kind).IsEqualTo(SymbolKind.Interface);
        await Assert.That(iface.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesInterfaceDefaultMethod()
    {
        var code = """
            public interface Repository<T> {
                default long count() {
                    return 0;
                }
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.FirstOrDefault(s => s.Name == "count");
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Repository");
    }

    // ── Enum Declarations ─────────────────────────────────────────────

    [Test]
    public async Task ParsesEnum()
    {
        var code = """
            public enum UserRole {
                ADMIN,
                MEMBER,
                GUEST
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("UserRole");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Enum);
    }

    // ── Record Declarations ───────────────────────────────────────────

    [Test]
    public async Task ParsesRecord()
    {
        var code = """
            public record Notification(String id, String title, String message) {
            }
            """;
        var result = Parse(code);

        var record = result.Symbols.First(s => s.Name == "Notification");
        await Assert.That(record.Kind).IsEqualTo(SymbolKind.Record);
        await Assert.That(record.Signature).Contains("(String id, String title, String message)");
    }

    // ── Annotation Types ──────────────────────────────────────────────

    [Test]
    public async Task ParsesAnnotationType()
    {
        var code = """
            public @interface EventHandler {
                String value() default "";
            }
            """;
        var result = Parse(code);

        var annotation = result.Symbols.First(s => s.Name == "EventHandler");
        await Assert.That(annotation.Kind).IsEqualTo(SymbolKind.Type);
    }

    // ── Method Declarations ───────────────────────────────────────────

    [Test]
    public async Task ParsesPublicMethod()
    {
        var code = """
            public class Service {
                public void process() {
                }
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "process");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Service");
        await Assert.That(method.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParsesConstructor()
    {
        var code = """
            public class User {
                public User(String name) {
                }
            }
            """;
        var result = Parse(code);

        var ctor = result.Symbols.First(s => s.Name == "User" && s.Kind == SymbolKind.Method);
        await Assert.That(ctor.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task ParsesMethodWithThrows()
    {
        var code = """
            public class Service {
                public void save() throws IOException, SQLException {
                }
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "save");
        await Assert.That(method.Signature).Contains("throws IOException, SQLException");
    }

    [Test]
    public async Task ParsesGenericMethod()
    {
        var code = """
            public class Service {
                public <T extends Auditable> void audit(T entity) {
                }
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "audit");
        await Assert.That(method.Signature).Contains("<T extends Auditable>");
    }

    [Test]
    public async Task ParsesAbstractMethod()
    {
        var code = """
            public abstract class Base {
                public abstract void process();
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "process");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.LineStart).IsEqualTo(method.LineEnd);
    }

    [Test]
    public async Task ParsesInterfaceMethod()
    {
        var code = """
            public interface Processor {
                void execute();
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "execute");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.ParentSymbol).IsEqualTo("Processor");
    }

    // ── Constants ─────────────────────────────────────────────────────

    [Test]
    public async Task ParsesStaticFinalConstant()
    {
        var code = """
            public class Config {
                public static final int MAX_SIZE = 100;
            }
            """;
        var result = Parse(code);

        var constant = result.Symbols.First(s => s.Name == "MAX_SIZE");
        await Assert.That(constant.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(constant.ParentSymbol).IsEqualTo("Config");
    }

    [Test]
    public async Task ParsesPrivateStaticFinalConstant()
    {
        var code = """
            public class Config {
                private static final String DEFAULT_NAME = "test";
            }
            """;
        var result = Parse(code);

        var constant = result.Symbols.First(s => s.Name == "DEFAULT_NAME");
        await Assert.That(constant.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(constant.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Inner Classes ─────────────────────────────────────────────────

    [Test]
    public async Task ParsesInnerClass()
    {
        var code = """
            public class Outer {
                public static class Inner {
                    public void innerMethod() {
                    }
                }
            }
            """;
        var result = Parse(code);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(inner.ParentSymbol).IsEqualTo("Outer");

        var innerMethod = result.Symbols.First(s => s.Name == "innerMethod");
        await Assert.That(innerMethod.ParentSymbol).IsEqualTo("Inner");
    }

    [Test]
    public async Task ParsesInnerEnum()
    {
        var code = """
            public class User {
                public enum UserRole {
                    ADMIN,
                    MEMBER
                }
            }
            """;
        var result = Parse(code);

        var innerEnum = result.Symbols.First(s => s.Name == "UserRole");
        await Assert.That(innerEnum.Kind).IsEqualTo(SymbolKind.Enum);
        await Assert.That(innerEnum.ParentSymbol).IsEqualTo("User");
    }

    // ── Javadoc ───────────────────────────────────────────────────────

    [Test]
    public async Task CapturesJavadocComment()
    {
        var code = """
            /** Base class for all entities. */
            public class BaseEntity {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols[0].DocComment).IsNotNull();
        await Assert.That(result.Symbols[0].DocComment!).Contains("Base class");
    }

    [Test]
    public async Task CapturesMultiLineJavadoc()
    {
        var code = """
            /**
             * Creates a new user.
             * @param name the display name
             * @return the created user
             */
            public class Factory {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols[0].DocComment).IsNotNull();
        await Assert.That(result.Symbols[0].DocComment!).Contains("Creates a new user");
    }

    // ── Annotations ───────────────────────────────────────────────────

    [Test]
    public async Task AnnotationsIncludedInMethodSignature()
    {
        var code = """
            public class Service {
                @Override
                public String toString() {
                    return "Service";
                }
            }
            """;
        var result = Parse(code);

        var method = result.Symbols.First(s => s.Name == "toString");
        await Assert.That(method.Signature).Contains("@Override");
    }

    // ── Visibility ────────────────────────────────────────────────────

    [Test]
    public async Task PackagePrivateClassIsPrivate()
    {
        var code = """
            class PackagePrivate {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols[0].Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task PublicClassIsPublic()
    {
        var code = """
            public class PublicClass {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols[0].Visibility).IsEqualTo(Visibility.Public);
    }

    // ── Resilience ────────────────────────────────────────────────────

    [Test]
    public async Task HandlesEmptyClass()
    {
        var code = """
            public class Empty {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("Empty");
    }

    [Test]
    public async Task HandlesMultipleClassesInFile()
    {
        var code = """
            public class Primary {
            }
            class Secondary {
            }
            """;
        var result = Parse(code);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("Primary");
        await Assert.That(names).Contains("Secondary");
    }

    // ── Line Ranges ───────────────────────────────────────────────────

    [Test]
    public async Task ClassSpansCorrectLines()
    {
        var code = """
            public class Service {
                public void run() {
                }
            }
            """;
        var result = Parse(code);

        var service = result.Symbols.First(s => s.Name == "Service");
        await Assert.That(service.LineStart).IsEqualTo(1);
        await Assert.That(service.LineEnd).IsEqualTo(4);
    }
}
