using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class CSharpParserTests
{
    private readonly CSharpParser _parser = new();

    private ParseResult Parse(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse("test.cs", bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsCSharp()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("csharp");
    }

    [Test]
    public async Task FileExtensionsContainsCs()
    {
        await Assert.That(_parser.FileExtensions).Contains(".cs");
    }

    // ── Empty / comments-only ───────────────────────────────────

    [Test]
    public async Task EmptyFileReturnsEmptyResult()
    {
        var result = Parse("");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task CommentOnlyFileReturnsEmptyResult()
    {
        var source = """
            // this is a comment
            /* block comment */
            /// xml comment
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Namespace ────────────────────────────────────────────────

    [Test]
    public async Task FileScopedNamespaceExtracted()
    {
        var source = """
            namespace Foo.Bar;

            public class Baz { }
            """;

        var result = Parse(source);

        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Module);
        await Assert.That(ns.Name).IsEqualTo("Foo.Bar");
        await Assert.That(ns.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(ns.Signature).IsEqualTo("namespace Foo.Bar;");
    }

    [Test]
    public async Task BlockScopedNamespaceExtracted()
    {
        var source = """
            namespace Foo.Bar
            {
                public class Baz { }
            }
            """;

        var result = Parse(source);

        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Module);
        await Assert.That(ns.Name).IsEqualTo("Foo.Bar");
        await Assert.That(ns.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(ns.LineStart).IsEqualTo(1);
        await Assert.That(ns.LineEnd).IsEqualTo(4);
    }

    [Test]
    public async Task MultipleNamespacesBlockScoped()
    {
        var source = """
            namespace First
            {
                public class A { }
            }

            namespace Second
            {
                public class B { }
            }
            """;

        var result = Parse(source);

        var namespaces = result.Symbols.Where(s => s.Kind == SymbolKind.Module).ToList();
        await Assert.That(namespaces).Count().IsEqualTo(2);
        await Assert.That(namespaces[0].Name).IsEqualTo("First");
        await Assert.That(namespaces[1].Name).IsEqualTo("Second");
    }

    // ── Class ────────────────────────────────────────────────────

    [Test]
    public async Task PublicClassWithBaseAndInterfaces()
    {
        var source = """
            namespace Test;

            public class Foo : Bar, IBaz
            {
                public int X { get; set; }
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Foo");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(cls.Signature).IsEqualTo("public class Foo : Bar, IBaz");
    }

    [Test]
    public async Task InternalClassVisibilityPublic()
    {
        var source = """
            internal class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task AbstractClassExtracted()
    {
        var source = """
            public abstract class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Signature).IsEqualTo("public abstract class Foo");
    }

    [Test]
    public async Task SealedClassExtracted()
    {
        var source = """
            public sealed class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Signature).IsEqualTo("public sealed class Foo");
    }

    [Test]
    public async Task StaticClassExtracted()
    {
        var source = """
            public static class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Signature).IsEqualTo("public static class Foo");
    }

    [Test]
    public async Task PartialClassExtracted()
    {
        var source = """
            public partial class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Signature).IsEqualTo("public partial class Foo");
    }

    // ── Interface ────────────────────────────────────────────────

    [Test]
    public async Task InterfaceWithIPrefix()
    {
        var source = """
            public interface IFoo
            {
                void DoStuff();
            }
            """;

        var result = Parse(source);

        var iface = result.Symbols.First(s => s.Kind == SymbolKind.Interface);
        await Assert.That(iface.Name).IsEqualTo("IFoo");
        await Assert.That(iface.Kind).IsEqualTo(SymbolKind.Interface);
        await Assert.That(iface.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task GenericInterfaceExtracted()
    {
        var source = """
            public interface IRepo<T>
            {
            }
            """;

        var result = Parse(source);

        var iface = result.Symbols.First(s => s.Kind == SymbolKind.Interface);
        await Assert.That(iface.Name).IsEqualTo("IRepo");
        await Assert.That(iface.Signature).IsEqualTo("public interface IRepo<T>");
    }

    // ── Record ───────────────────────────────────────────────────

    [Test]
    public async Task RecordWithPrimaryConstructor()
    {
        var source = """
            public record Foo(int X, string Y);
            """;

        var result = Parse(source);

        var rec = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(rec.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(rec.Signature).IsEqualTo("public record Foo(int X, string Y);");
    }

    [Test]
    public async Task RecordStructExtracted()
    {
        var source = """
            public record struct Point(int X, int Y);
            """;

        var result = Parse(source);

        var rec = result.Symbols.First(s => s.Name == "Point");
        await Assert.That(rec.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(rec.Signature).IsEqualTo("public record struct Point(int X, int Y);");
    }

    // ── Enum ─────────────────────────────────────────────────────

    [Test]
    public async Task EnumExtracted()
    {
        var source = """
            public enum Color
            {
                Red,
                Green,
                Blue
            }
            """;

        var result = Parse(source);

        var en = result.Symbols.First(s => s.Name == "Color");
        await Assert.That(en.Kind).IsEqualTo(SymbolKind.Type);
        await Assert.That(en.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task EnumWithUnderlyingType()
    {
        var source = """
            public enum Flags : byte
            {
                None = 0,
                A = 1
            }
            """;

        var result = Parse(source);

        var en = result.Symbols.First(s => s.Name == "Flags");
        await Assert.That(en.Signature).IsEqualTo("public enum Flags : byte");
    }

    // ── Struct ───────────────────────────────────────────────────

    [Test]
    public async Task StructExtracted()
    {
        var source = """
            public struct Vector3
            {
                public float X;
                public float Y;
                public float Z;
            }
            """;

        var result = Parse(source);

        var st = result.Symbols.First(s => s.Name == "Vector3");
        await Assert.That(st.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(st.Visibility).IsEqualTo(Visibility.Public);
    }

    // ── Generics ─────────────────────────────────────────────────

    [Test]
    public async Task GenericClassWithConstraints()
    {
        var source = """
            public class Repo<T> where T : IEntity
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Repo");
        await Assert.That(cls.Signature).IsEqualTo("public class Repo<T> where T : IEntity");
    }

    // ── Nested types ─────────────────────────────────────────────

    [Test]
    public async Task NestedClassParentSet()
    {
        var source = """
            public class Outer
            {
                public class Inner
                {
                }
            }
            """;

        var result = Parse(source);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.ParentSymbol).IsEqualTo("Outer");
    }

    [Test]
    public async Task NestedEnumParentSet()
    {
        var source = """
            public class Container
            {
                public enum Status
                {
                    Active,
                    Inactive
                }
            }
            """;

        var result = Parse(source);

        var en = result.Symbols.First(s => s.Name == "Status");
        await Assert.That(en.ParentSymbol).IsEqualTo("Container");
    }

    [Test]
    public async Task PrivateNestedClassVisibilityPrivate()
    {
        var source = """
            public class Outer
            {
                class Inner
                {
                }
            }
            """;

        var result = Parse(source);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── XML Doc Comments ─────────────────────────────────────────

    [Test]
    public async Task XmlDocCommentCaptured()
    {
        var source = """
            /// <summary>My class.</summary>
            public class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.DocComment).IsEqualTo("/// <summary>My class.</summary>");
    }

    [Test]
    public async Task MultiLineXmlDocCaptured()
    {
        var source = """
            /// <summary>
            /// My class does things.
            /// </summary>
            public class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.DocComment).IsEqualTo(
            """
            /// <summary>
            /// My class does things.
            /// </summary>
            """);
    }

    // ── Visibility derivation ────────────────────────────────────

    [Test]
    public async Task NoModifierTopLevelVisibilityPublic()
    {
        // C# default for top-level types is internal, mapped to Public
        var source = """
            class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ProtectedInternalVisibilityPublic()
    {
        var source = """
            public class Outer
            {
                protected internal class Inner
                {
                }
            }
            """;

        var result = Parse(source);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task PrivateProtectedVisibilityPrivate()
    {
        var source = """
            public class Outer
            {
                private protected class Inner
                {
                }
            }
            """;

        var result = Parse(source);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ProtectedVisibilityPrivate()
    {
        var source = """
            public class Outer
            {
                protected class Inner
                {
                }
            }
            """;

        var result = Parse(source);

        var inner = result.Symbols.First(s => s.Name == "Inner");
        await Assert.That(inner.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Brace tracking edge cases ────────────────────────────────

    [Test]
    public async Task BracesInStringLiteralsIgnored()
    {
        var source = """
            public class Foo
            {
                public string Bar = "{ }";
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.LineStart).IsEqualTo(1);
        await Assert.That(cls.LineEnd).IsEqualTo(4);
    }

    [Test]
    public async Task BracesInVerbatimStringIgnored()
    {
        var source = """
            public class Foo
            {
                public string Bar = @"test { } more";
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.LineEnd).IsEqualTo(4);
    }

    [Test]
    public async Task BracesInSingleLineCommentIgnored()
    {
        var source = """
            public class Foo
            {
                // this has { braces }
                public int X;
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.LineEnd).IsEqualTo(5);
    }

    [Test]
    public async Task BracesInBlockCommentIgnored()
    {
        var source = """
            public class Foo
            {
                /* { } */
                public int X;
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.LineEnd).IsEqualTo(5);
    }

    [Test]
    public async Task BracesInCharLiteralIgnored()
    {
        var source = """
            public class Foo
            {
                public char Open = '{';
                public char Close = '}';
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(cls.LineEnd).IsEqualTo(5);
    }

    // ── Line numbers and byte offsets ────────────────────────────

    [Test]
    public async Task LineNumbersAreOneBased()
    {
        var source = """
            namespace Test;

            public class Foo
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.LineStart).IsEqualTo(3);
        await Assert.That(cls.LineEnd).IsEqualTo(5);
    }

    [Test]
    public async Task ByteOffsetIsCorrect()
    {
        var source = "namespace Test;\n\npublic class Foo\n{\n}\n";

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        var expectedOffset = Encoding.UTF8.GetByteCount("namespace Test;\n\n");
        await Assert.That(cls.ByteOffset).IsEqualTo(expectedOffset);
    }

    // ── Record with body ─────────────────────────────────────────

    [Test]
    public async Task RecordWithBodyExtracted()
    {
        var source = """
            public record Foo(int X)
            {
                public int Y { get; init; }
            }
            """;

        var result = Parse(source);

        var rec = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(rec.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(rec.LineEnd).IsEqualTo(4);
    }

    // ── File-scoped type (C# 14 file modifier) ──────────────────

    [Test]
    public async Task FileModifierClassExtracted()
    {
        var source = """
            file class Secret
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Name == "Secret");
        await Assert.That(cls.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(cls.Visibility).IsEqualTo(Visibility.Private);
    }
}
