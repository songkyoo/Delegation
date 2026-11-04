using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ExposeGenerationTests
{
    [TestCase("get => 0;", "get; private set;")]
    [TestCase("set { }", "private get; set;")]
    public void ChecksExistingPropertiesUsingOnlyExposedAccessors(string existingAccessors, string targetAccessors)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target { public int Value { {{targetAccessors}} } }

            public partial class Wrapper
            {
                public int Renamed { {{existingAccessors}} }

                [Expose(rename: new[] { "Value:Renamed" })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [Test]
    public void PreservesPrivateAccessors_WhenAccessibleFromNestedWrapper()
    {
        var result = RunGenerator(
            """
            using Macaron.MemberExposure;

            public partial class Target
            {
                public int Value { get; private set; }
                public partial class Wrapper
                {
                    [Expose]
                    private readonly Target _target = new();
                }
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(
            result.GeneratedSources.Single().SourceText.ToString(),
            Does.Contain("get => _target.Value;").And.Contain("set => _target.Value = value;")
        );
    }

    [TestCase("Target", false)]
    [TestCase("Wrapper", true)]
    public void ChecksProtectedAccessorThroughReceiverType(string receiverType, bool canSet)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target { public int Value { get; protected set; } }

            public partial class Wrapper : Target
            {
                [Expose(rename: new[] { "Value:DelegatedValue" }, IncludeBaseTypes = true)]
                private readonly {{receiverType}} _target = null!;
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source.Contains("set => _target.Value = value;", StringComparison.Ordinal), Is.EqualTo(canSet));
    }

    [TestCase("private", false)]
    [TestCase("protected", false)]
    [TestCase("private protected", false)]
    [TestCase("internal", true)]
    [TestCase("protected internal", true)]
    public void ExposesOnlyAccessiblePropertyAccessors(string accessibility, bool accessible)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Readable { get; {{accessibility}} set; }
                public int Writable { {{accessibility}} get; set; }
            }

            public partial class Wrapper
            {
                [Expose]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("get => _target.Readable;"));
            Assert.That(source, Does.Contain("set => _target.Writable = value;"));
            Assert.That(
                source.Contains("set => _target.Readable = value;", StringComparison.Ordinal),
                Is.EqualTo(accessible)
            );
            Assert.That(
                source.Contains("get => _target.Writable;", StringComparison.Ordinal),
                Is.EqualTo(accessible)
            );
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SkipsPropertiesWithoutCallableAccessors(bool useOptions)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { private get; init; }
                public int InitOnly { init { } }
                public int Readable { get; init; }
            }

            public partial class Wrapper
            {
                [Expose{{(useOptions ? "(filter: new[] { \"Value\", \"InitOnly\", \"Readable\" })" : "")}}]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(
            source,
            Does.Contain("get => _target.Readable;").And.Not.Contain("int Value").And.Not.Contain("int InitOnly")
        );
        Assert.That(
            result.Diagnostics.Count(static diagnostic => diagnostic.Id == "MAME0002"),
            Is.EqualTo(useOptions ? 2 : 0)
        );
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ChecksAccessibilityAcrossAssemblies_ForDeclaredAndInheritedMembers(bool useOptions, bool friendAssembly)
    {
        const string targetSource =
            """
            public class TargetBase
            {
                internal void HiddenMethod() { }

                internal int HiddenProperty { get; set; }

                internal event System.Action HiddenEvent { add { } remove { } }

                public int Readable { get; internal set; }

                public int Writable { internal get; set; }
            }

            public class Target : TargetBase
            {
                internal void HiddenDeclaredMethod() { }

                internal int HiddenDeclaredProperty { get; set; }

                internal event System.Action HiddenDeclaredEvent { add { } remove { } }

                public void Run() { }

                public event System.Action Changed { add { } remove { } }
            }
            """;
        var references = CreateReferences(typeof(MemberExposure.ExposeAttribute));
        var targetCompilation = CSharpCompilation.Create(
            "ExternalTargets",
            [
                CSharpSyntaxTree.ParseText((friendAssembly
                    ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Macaron.Delegation.Tests\")]\n"
                    : "") + targetSource)
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        Assert.That(targetCompilation.GetDiagnostics(), Is.Empty);

        var result = RunGeneratorWithReferences(
            $$"""
            using Macaron.MemberExposure;
            public partial class Wrapper
            {
                [Expose({{(useOptions ? "filter: new[] { \"Run\", \"Changed\", \"Readable\", \"Writable\" }, " : "")}}IncludeBaseTypes = true)]
                private readonly Target _target = new();
            }
            """,
            references.Add(targetCompilation.ToMetadataReference())
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source.Contains("Hidden", StringComparison.Ordinal), Is.EqualTo(friendAssembly && !useOptions));
            Assert.That(source, Does.Contain("void Run()").And.Contain("event global::System.Action Changed"));
            Assert.That(
                source,
                Does.Contain("get => _target.Readable;").And.Contain("set => _target.Writable = value;")
            );
            Assert.That(
                source.Contains("set => _target.Readable", StringComparison.Ordinal),
                Is.EqualTo(friendAssembly)
            );
            Assert.That(
                source.Contains("get => _target.Writable", StringComparison.Ordinal),
                Is.EqualTo(friendAssembly)
            );
        });
    }

    [Test]
    public void IgnoresNonExposeableMembers_When_UsingExposeAttribute()
    {
        AssertGeneratedCode(
            sourceCode:
            """
            using System.Collections.Generic;

            namespace Macaron.Delegation.Tests;

            using Macaron.InterfaceDelegation;

            using Macaron.MemberExposure;

            // 베이스 클래스 멤버도 포함
            public class Base
            {
                public int BaseValue { get; }

                public void BaseMethod() { }

                // 오버라이드 된 멤버는 무시됨
                public virtual void BaseVirtualMethod() { }
            }

            public abstract class Foo : Base
            {
                // 정적 멤버는 무시됨
                public static Foo Of() => null!;

                public static int StaticValue => 42;

                // 인덱서는 무시됨
                public int this[int index]
                {
                    get => 0;
                    set { }
                }

                public List<object?> Value => null!;

                public int GetAnswer<T>() where T : class => 42;

                public int GetAnswerNullable<T>() where T : class? => 42;

                public List<T?> GetAnswerNullable2<T>() => null!;

                // internal은 internal로 리프팅
                internal int GetAnswer() => 42;

                public override void BaseVirtualMethod() { }

                // public인 아닌 멤버는 무시됨
                protected int HiddenAnswer() => 42;

                // abstract 멤버도 리프팅 대상
                public abstract void AbstractMethod();
            }

            public partial class Bar
            {
                [Expose(IncludeBaseTypes = true)]
                private readonly Foo _impl = null!;
            }
            """,
            expected:
            """
            // <auto-generated />
            #nullable enable

            namespace Macaron.Delegation.Tests
            {
                partial class Bar
                {
                    #region global::Macaron.Delegation.Tests.Foo
                    public global::System.Collections.Generic.List<object?> Value
                    {
                        get => _impl.Value;
                    }

                    public int GetAnswer<T>()
                        where T : class
                        => _impl.GetAnswer<T>();

                    public int GetAnswerNullable<T>()
                        where T : class?
                        => _impl.GetAnswerNullable<T>();

                    public global::System.Collections.Generic.List<T?> GetAnswerNullable2<T>()
                        => _impl.GetAnswerNullable2<T>();

                    internal int GetAnswer()
                        => _impl.GetAnswer();

                    public void BaseVirtualMethod()
                        => _impl.BaseVirtualMethod();

                    public void AbstractMethod()
                        => _impl.AbstractMethod();

                    public int BaseValue
                    {
                        get => _impl.BaseValue;
                    }

                    public void BaseMethod()
                        => _impl.BaseMethod();
                    #endregion
                }
            }

            """
        );
    }

    [Test]
    public void ExposeedMethodPreservesDefaultParametersFromTargetMethod_When_TargetMethodHasDefaultParameters()
    {
        AssertGeneratedCode(
            sourceCode:
            """
            using System;

            namespace Macaron.Delegation.Tests;

            using Macaron.InterfaceDelegation;

            using Macaron.MemberExposure;

            public struct Foo
            {
                // 정적 멤버는 무시됨
                public static Foo Of() => new Foo();

                public static int StaticValue => 42;

                public void Bar(int x, int y = 42, DateTime z = new DateTime()) { }
            }

            public partial class Exposeed
            {
                [Expose]
                private Foo _impl { get; } = new();
            }
            """,
            expected:
            """
            // <auto-generated />
            #nullable enable

            namespace Macaron.Delegation.Tests
            {
                partial class Exposeed
                {
                    #region global::Macaron.Delegation.Tests.Foo
                    public void Bar(int x, int y = 42, global::System.DateTime z = default)
                        => _impl.Bar(x, y, z);
                    #endregion
                }
            }

            """
        );
    }

    [Test]
    public void ExposeAttributeOptions_WorksAsExpected()
    {
        AssertGeneratedCodes(
            sourceCode:
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public class ExposeTargetBase
            {
                public int BaseValue { get; }
            }

            public class ExposeTarget : ExposeTargetBase
            {
                public int Value => 42;

                public void GetA() { }

                public void GetA(int x) { }

                public void GetB() { }

                public void GetC() { }
            }

            public partial class Exposeed
            {
                // 멤버 중복 여부는 변경된 이름을 기준으로 검사
                public void Renamed() { }

                [Expose(
                    filter: new[] { "BaseValue", "Value", "GetA", "GetB" },
                    remove: new[] { "GetB" },
                    rename: new[] { "BaseValue:ParentValue", "Value:Answer", "GetA:Renamed" },
                    IncludeBaseTypes = true
                )]
                private readonly ExposeTarget _impl = new();

                [Expose(
                    filter: new[] { "GetB" },
                    IncludeBaseTypes = false
                )]
                private readonly ExposeTarget _impl2 = new();
            }
            """,
            expected:
            [
                """
                // <auto-generated />
                #nullable enable

                namespace Macaron.Delegation.Tests
                {
                    partial class Exposeed
                    {
                        #region global::Macaron.Delegation.Tests.ExposeTarget
                        public int Answer
                        {
                            get => _impl.Value;
                        }

                        public void Renamed(int x)
                            => _impl.GetA(x);

                        public int ParentValue
                        {
                            get => _impl.BaseValue;
                        }
                        #endregion
                    }
                }

                """,
                """
                // <auto-generated />
                #nullable enable

                namespace Macaron.Delegation.Tests
                {
                    partial class Exposeed
                    {
                        #region global::Macaron.Delegation.Tests.ExposeTarget
                        public void GetB()
                            => _impl2.GetB();
                        #endregion
                    }
                }

                """,
            ]
        );
    }

    [Test]
    public void EmptyExposeOptions_PreserveDefaultGenerationWithoutDiagnostics()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public sealed class ExposeTarget
            {
                public void Run() { }
            }

            public partial class DefaultExpose
            {
                [Expose]
                private readonly ExposeTarget _impl = new();
            }

            public partial class ExplicitNullExpose
            {
                [Expose(null, null, null)]
                private readonly ExposeTarget _impl = new();
            }

            public partial class EmptyFilterExpose
            {
                [Expose(filter: new string[] { })]
                private readonly ExposeTarget _impl = new();
            }

            public partial class EmptyRemoveExpose
            {
                [Expose(remove: new string[] { })]
                private readonly ExposeTarget _impl = new();
            }

            public partial class EmptyRenameExpose
            {
                [Expose(rename: new string[] { })]
                private readonly ExposeTarget _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.None.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAME0002"));
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(5));
            Assert.That(
                result.GeneratedSources.Select(static source => source.SourceText.ToString()),
                Has.All.Contains("public void Run()")
            );
        });
    }

    [Test]
    public void WhitespaceExposeFilter_RemainsAnActiveFilter()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public sealed class ExposeTarget
            {
                public void Run() { }
            }

            public partial class Wrapper
            {
                [Expose(filter: new[] { " " })]
                private readonly ExposeTarget _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.None.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAME0002"));
            Assert.That(result.GeneratedSources, Is.Empty);
        });
    }

    [Test]
    public void ImplementAndExposeOnSameTarget_ProduceDistinctSources()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IFoo
            {
                void Run();
            }

            public sealed class Foo : IFoo
            {
                public int Value => 42;

                public void Run() { }
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo), Mode = ImplementationMode.Implicit)]
                [Expose(filter: new[] { "Value" })]
                private readonly Foo _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(2));
            Assert.That(
                result.GeneratedSources.Select(static source => source.HintName),
                Is.Unique
            );
            Assert.That(
                result.GeneratedSources.Count(static source => source.SourceText.ToString().Contains("public void Run()", StringComparison.Ordinal)),
                Is.EqualTo(1)
            );
            Assert.That(
                result.GeneratedSources.Count(static source => source.SourceText.ToString().Contains("public int Value", StringComparison.Ordinal)),
                Is.EqualTo(1)
            );
        });
    }
}
