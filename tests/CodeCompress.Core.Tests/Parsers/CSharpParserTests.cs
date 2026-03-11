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

    // ── Methods ──────────────────────────────────────────────────

    [Test]
    public async Task PublicMethodExtracted()
    {
        var source = """
            public class MyClass
            {
                public void DoSomething()
                {
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("DoSomething");
        await Assert.That(method.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(method.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(method.ParentSymbol).IsEqualTo("MyClass");
        await Assert.That(method.Signature).IsEqualTo("public void DoSomething()");
    }

    [Test]
    public async Task PrivateMethodVisibilityPrivate()
    {
        var source = """
            public class MyClass
            {
                private int Calculate()
                {
                    return 42;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task ProtectedMethodExtracted()
    {
        var source = """
            public class MyClass
            {
                protected virtual void OnEvent()
                {
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Signature).IsEqualTo("protected virtual void OnEvent()");
    }

    [Test]
    public async Task AsyncMethodTaskReturn()
    {
        var source = """
            public class MyClass
            {
                public async Task<int> GetAsync()
                {
                    return 42;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Signature).IsEqualTo("public async Task<int> GetAsync()");
    }

    [Test]
    public async Task GenericMethodWithConstraints()
    {
        var source = """
            public class MyClass
            {
                public T Find<T>(int id) where T : class
                {
                    return default;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Find");
        await Assert.That(method.Signature).IsEqualTo("public T Find<T>(int id) where T : class");
    }

    [Test]
    public async Task ConstructorExtracted()
    {
        var source = """
            public class MyClass
            {
                public MyClass(int x, string y)
                {
                }
            }
            """;

        var result = Parse(source);

        var ctor = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(ctor.Name).IsEqualTo("MyClass");
        await Assert.That(ctor.ParentSymbol).IsEqualTo("MyClass");
    }

    [Test]
    public async Task StaticConstructorExtracted()
    {
        var source = """
            public class MyClass
            {
                static MyClass()
                {
                }
            }
            """;

        var result = Parse(source);

        var ctor = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(ctor.Name).IsEqualTo("MyClass");
        await Assert.That(ctor.Signature).IsEqualTo("static MyClass()");
    }

    [Test]
    public async Task FinalizerExtracted()
    {
        var source = """
            public class MyClass
            {
                ~MyClass()
                {
                }
            }
            """;

        var result = Parse(source);

        var finalizer = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(finalizer.Name).IsEqualTo("~MyClass");
    }

    [Test]
    public async Task OperatorOverloadExtracted()
    {
        var source = """
            public class MyClass
            {
                public static MyClass operator +(MyClass a, MyClass b)
                {
                    return a;
                }
            }
            """;

        var result = Parse(source);

        var op = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(op.Name).IsEqualTo("operator +");
        await Assert.That(op.Signature).IsEqualTo("public static MyClass operator +(MyClass a, MyClass b)");
    }

    [Test]
    public async Task IndexerExtracted()
    {
        var source = """
            public class MyClass
            {
                public int this[int i]
                {
                    get { return 0; }
                }
            }
            """;

        var result = Parse(source);

        var indexer = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(indexer.Name).IsEqualTo("this[]");
        await Assert.That(indexer.Signature).IsEqualTo("public int this[int i]");
    }

    [Test]
    public async Task ExpressionBodiedMethodExtracted()
    {
        var source = """
            public class MyClass
            {
                public int Sum() => X + Y;
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Sum");
        await Assert.That(method.Signature).IsEqualTo("public int Sum()");
    }

    [Test]
    public async Task ExtensionMethodThisParameter()
    {
        var source = """
            public static class Extensions
            {
                public static int Count(this IEnumerable<int> source)
                {
                    return 0;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Count");
        await Assert.That(method.Signature).IsEqualTo("public static int Count(this IEnumerable<int> source)");
    }

    [Test]
    public async Task VoidMethodExtracted()
    {
        var source = """
            public class MyClass
            {
                public void Execute()
                {
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Execute");
    }

    // ── Properties ───────────────────────────────────────────────

    [Test]
    public async Task AutoPropertyExtracted()
    {
        var source = """
            public class MyClass
            {
                public string Name { get; set; }
            }
            """;

        var result = Parse(source);

        var prop = result.Symbols.First(s => s.Kind == SymbolKind.Constant);
        await Assert.That(prop.Name).IsEqualTo("Name");
        await Assert.That(prop.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(prop.Signature).IsEqualTo("public string Name { get; set; }");
        await Assert.That(prop.ParentSymbol).IsEqualTo("MyClass");
    }

    [Test]
    public async Task InitOnlyPropertyExtracted()
    {
        var source = """
            public class MyClass
            {
                public string Name { get; init; }
            }
            """;

        var result = Parse(source);

        var prop = result.Symbols.First(s => s.Kind == SymbolKind.Constant);
        await Assert.That(prop.Signature).IsEqualTo("public string Name { get; init; }");
    }

    [Test]
    public async Task ExpressionBodiedPropertyExtracted()
    {
        var source = """
            public class MyClass
            {
                public int Total => Items.Count;
            }
            """;

        var result = Parse(source);

        var prop = result.Symbols.First(s => s.Kind == SymbolKind.Constant);
        await Assert.That(prop.Name).IsEqualTo("Total");
        await Assert.That(prop.Signature).IsEqualTo("public int Total => Items.Count;");
    }

    [Test]
    public async Task RequiredPropertyExtracted()
    {
        var source = """
            public class MyClass
            {
                public required string Name { get; set; }
            }
            """;

        var result = Parse(source);

        var prop = result.Symbols.First(s => s.Kind == SymbolKind.Constant);
        await Assert.That(prop.Signature).IsEqualTo("public required string Name { get; set; }");
    }

    // ── Using statements ─────────────────────────────────────────

    [Test]
    public async Task UsingStatementAsDependency()
    {
        var source = """
            using System.Collections.Generic;

            public class Foo { }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("System.Collections.Generic");
        await Assert.That(result.Dependencies[0].Alias).IsNull();
    }

    [Test]
    public async Task GlobalUsingAsDependency()
    {
        var source = """
            global using System.Linq;

            public class Foo { }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("System.Linq");
    }

    [Test]
    public async Task UsingAliasAsDependency()
    {
        var source = """
            using Dict = System.Collections.Generic.Dictionary<string, int>;

            public class Foo { }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("System.Collections.Generic.Dictionary<string, int>");
        await Assert.That(result.Dependencies[0].Alias).IsEqualTo("Dict");
    }

    [Test]
    public async Task UsingStaticAsDependency()
    {
        var source = """
            using static System.Math;

            public class Foo { }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("System.Math");
    }

    // ── Edge cases ───────────────────────────────────────────────

    [Test]
    public async Task MethodWithAttributesAttributeSkipped()
    {
        var source = """
            public class MyController
            {
                [HttpGet]
                public IActionResult Get()
                {
                    return Ok();
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Signature).IsEqualTo("public IActionResult Get()");
    }

    [Test]
    public async Task NullableReturnTypeCaptured()
    {
        var source = """
            public class MyClass
            {
                public string? GetName()
                {
                    return null;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Signature).IsEqualTo("public string? GetName()");
    }

    [Test]
    public async Task TupleReturnTypeCaptured()
    {
        var source = """
            public class MyClass
            {
                public (int X, string Y) GetPair()
                {
                    return (1, "a");
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Signature).IsEqualTo("public (int X, string Y) GetPair()");
    }

    [Test]
    public async Task ExplicitInterfaceImplCaptured()
    {
        var source = """
            public class MyClass : IDisposable
            {
                void IDisposable.Dispose()
                {
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("IDisposable.Dispose");
    }

    [Test]
    public async Task DefaultVisibilityForMethodIsPrivate()
    {
        var source = """
            public class MyClass
            {
                void InternalMethod()
                {
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Visibility).IsEqualTo(Visibility.Private);
    }

    // ── Block-scoped namespace with brace on same line ─────────

    [Test]
    public async Task BlockScopedNamespaceWithBraceOnSameLine()
    {
        var source = """
            namespace Foo.Bar {
              public class Baz { }
            }
            """;

        var result = Parse(source);

        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Module);
        await Assert.That(ns.Name).IsEqualTo("Foo.Bar");
        await Assert.That(ns.Signature).IsEqualTo("namespace Foo.Bar");

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Baz");
    }

    [Test]
    public async Task BlockScopedNamespaceWithBraceNoSpace()
    {
        var source = """
            namespace Foo.Bar{
            }
            """;

        var result = Parse(source);

        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Module);
        await Assert.That(ns.Name).IsEqualTo("Foo.Bar");
    }

    // ── Nested generic type parameters ──────────────────────────

    [Test]
    public async Task NestedGenericTypeParametersExtracted()
    {
        var source = """
            public class Repo<Dictionary<string, int>>
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Repo");
        await Assert.That(cls.Signature).IsEqualTo("public class Repo<Dictionary<string, int>>");
    }

    [Test]
    public async Task DeeplyNestedGenericsExtracted()
    {
        var source = """
            public class Cache<Dict<string, List<int>>>
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Cache");
        await Assert.That(cls.Signature).IsEqualTo("public class Cache<Dict<string, List<int>>>");
    }

    [Test]
    public async Task GenericWithNestedGenericsAndBaseType()
    {
        var source = """
            public class Foo<Dict<string, int>> : IBar
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Foo");
        await Assert.That(cls.Signature).IsEqualTo("public class Foo<Dict<string, int>> : IBar");
    }

    // ── Extension method tests ──────────────────────────────────

    [Test]
    public async Task ExtensionMethodWithGenericReturnType()
    {
        var source = """
            public static class Extensions
            {
                public static IServiceCollection AddFoo(this IServiceCollection services)
                {
                    return services;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("AddFoo");
        await Assert.That(method.ParentSymbol).IsEqualTo("Extensions");
        await Assert.That(method.Signature).IsEqualTo("public static IServiceCollection AddFoo(this IServiceCollection services)");
    }

    [Test]
    public async Task MultipleExtensionMethodsInStaticClass()
    {
        var source = """
            public static class Extensions
            {
                public static void Foo(this string s) { }
                public static void Bar(this string s) { }
                public static void Baz(this string s) { }
            }
            """;

        var result = Parse(source);

        var methods = result.Symbols.Where(s => s.Kind == SymbolKind.Method).ToList();
        await Assert.That(methods).Count().IsEqualTo(3);
        await Assert.That(methods[0].ParentSymbol).IsEqualTo("Extensions");
        await Assert.That(methods[1].ParentSymbol).IsEqualTo("Extensions");
        await Assert.That(methods[2].ParentSymbol).IsEqualTo("Extensions");
    }

    [Test]
    public async Task ExtensionMethodWithGenericConstraints()
    {
        var source = """
            public static class Extensions
            {
                public static T Find<T>(this IQueryable<T> query) where T : class
                {
                    return default;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Find");
        await Assert.That(method.Signature).IsEqualTo("public static T Find<T>(this IQueryable<T> query) where T : class");
    }

    [Test]
    public async Task ExtensionMethodWithAttributes()
    {
        var source = """
            public static class Extensions
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int Count(this IEnumerable<int> source)
                {
                    return 0;
                }
            }
            """;

        var result = Parse(source);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Count");
    }

    // ── Additional parser robustness tests ──────────────────────

    [Test]
    public async Task AbstractClassWithGenericBase()
    {
        var source = """
            public abstract class BaseEntity<TId> where TId : struct
            {
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("BaseEntity");
        await Assert.That(cls.Signature).IsEqualTo("public abstract class BaseEntity<TId> where TId : struct");
    }

    [Test]
    public async Task PartialClassMultipleFiles()
    {
        var source1 = """
            public partial class Foo
            {
                public void A() { }
            }
            """;

        var source2 = """
            public partial class Foo
            {
                public void B() { }
            }
            """;

        var result1 = Parse(source1);
        var result2 = Parse(source2);

        var cls1 = result1.Symbols.First(s => s.Kind == SymbolKind.Class);
        var cls2 = result2.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls1.Name).IsEqualTo("Foo");
        await Assert.That(cls2.Name).IsEqualTo("Foo");
    }

    [Test]
    public async Task StaticClassWithMethods()
    {
        var source = """
            public static class Extensions
            {
                public static void Foo() { }
            }
            """;

        var result = Parse(source);

        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class);
        await Assert.That(cls.Name).IsEqualTo("Extensions");

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Foo");
        await Assert.That(method.ParentSymbol).IsEqualTo("Extensions");
    }

    [Test]
    public async Task SealedAbstractStaticModifierCombinations()
    {
        var source = """
            public sealed class A { }
            public abstract class B { }
            public static class C { }
            internal sealed class D { }
            """;

        var result = Parse(source);

        var classes = result.Symbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        await Assert.That(classes).Count().IsEqualTo(4);
        await Assert.That(classes[0].Name).IsEqualTo("A");
        await Assert.That(classes[1].Name).IsEqualTo("B");
        await Assert.That(classes[2].Name).IsEqualTo("C");
        await Assert.That(classes[3].Name).IsEqualTo("D");
    }

    [Test]
    public async Task RecordWithBodyAndMethods()
    {
        var source = """
            public record Foo(int X)
            {
                public int Double() => X * 2;
            }
            """;

        var result = Parse(source);

        var rec = result.Symbols.First(s => s.Name == "Foo");
        await Assert.That(rec.Kind).IsEqualTo(SymbolKind.Class);

        var method = result.Symbols.First(s => s.Kind == SymbolKind.Method);
        await Assert.That(method.Name).IsEqualTo("Double");
        await Assert.That(method.ParentSymbol).IsEqualTo("Foo");
    }

    // ── Complex real-world file ─────────────────────────────────

    [Test]
    public async Task ComplexRealWorldFileAllSymbolsExtracted()
    {
        var source = """
            using System;
            using System.Collections.Generic;

            namespace MyApp.Services;

            /// <summary>
            /// Service for managing users.
            /// </summary>
            public class UserService : IUserService
            {
                private readonly ILogger _logger;

                public UserService(ILogger logger)
                {
                    _logger = logger;
                }

                public string Name { get; set; }

                public int Count => _users.Count;

                public async Task<User?> GetByIdAsync(int id)
                {
                    return null;
                }

                private void Validate(User user)
                {
                    if (user == null) throw new ArgumentNullException(nameof(user));
                }

                public enum Status
                {
                    Active,
                    Inactive
                }
            }
            """;

        var result = Parse(source);

        // Should find: namespace, class, constructor, 2 properties, 2 methods, nested enum
        var ns = result.Symbols.Where(s => s.Kind == SymbolKind.Module).ToList();
        await Assert.That(ns).Count().IsEqualTo(1);

        var classes = result.Symbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        await Assert.That(classes).Count().IsEqualTo(1);

        var methods = result.Symbols.Where(s => s.Kind == SymbolKind.Method).ToList();
        await Assert.That(methods.Count).IsGreaterThanOrEqualTo(3);

        var properties = result.Symbols.Where(s => s.Kind == SymbolKind.Constant).ToList();
        await Assert.That(properties.Count).IsGreaterThanOrEqualTo(2);

        var enums = result.Symbols.Where(s => s.Kind == SymbolKind.Type).ToList();
        await Assert.That(enums).Count().IsEqualTo(1);

        // Dependencies from using statements
        await Assert.That(result.Dependencies.Count).IsGreaterThanOrEqualTo(2);
    }
}
