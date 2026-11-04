using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ExposeAssemblyAccessibilityTests
{
    private const string TargetSource =
        """
        public class TargetBase
        {
            internal void HiddenMethod() { }

            internal int HiddenProperty { get; set; }

            internal event System.Action HiddenEvent
            {
                add { }
                remove { }
            }

            public int Readable { get; internal set; }

            public int Writable { internal get; set; }
        }

        public class Target : TargetBase
        {
            internal void HiddenDeclaredMethod() { }

            internal int HiddenDeclaredProperty { get; set; }

            internal event System.Action HiddenDeclaredEvent
            {
                add { }
                remove { }
            }

            public void Run() { }

            public event System.Action Changed
            {
                add { }
                remove { }
            }
        }
        """;

    private const string PublicMemberFilter =
        """
        Expose(filter: new[] { "Run", "Changed", "Readable", "Writable" }, IncludeBaseTypes = true)
        """;

    private const string FriendAssemblyAttribute =
        """
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Macaron.Delegation.Tests")]

        """;

    [TestCase("Expose(IncludeBaseTypes = true)")]
    [TestCase(PublicMemberFilter)]
    public void ExternalAssembly_ExcludesInaccessibleMembersAndAccessors(string attribute)
    {
        var references = CreateExternalTargetReferences();
        var result = RunGeneratorWithReferences(
            $$"""
            using Macaron.MemberExposure;

            public partial class Wrapper
            {
                [{{attribute}}]
                private readonly Target _target = new();
            }
            """,
            references
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("Hidden"));
            Assert.That(source, Does.Contain("void Run()").And.Contain("event global::System.Action Changed"));
            Assert.That(source, Does.Contain("get => _target.Readable;").And.Not.Contain("set => _target.Readable"));
            Assert.That(
                source,
                Does.Contain("set => _target.Writable = value;").And.Not.Contain("get => _target.Writable")
            );
        });
    }

    [Test]
    public void FriendAssembly_ExposesInternalMembersAndAccessors()
    {
        var references = CreateExternalTargetReferences(assemblyAttributes: FriendAssemblyAttribute);
        var result = RunGeneratorWithReferences(
            """
            using Macaron.MemberExposure;

            public partial class Wrapper
            {
                [Expose(IncludeBaseTypes = true)]
                private readonly Target _target = new();
            }
            """,
            references
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("HiddenMethod()").And.Contain("HiddenProperty").And.Contain("HiddenEvent"));
            Assert.That(
                source,
                Does
                    .Contain("HiddenDeclaredMethod()")
                    .And.Contain("HiddenDeclaredProperty")
                    .And.Contain("HiddenDeclaredEvent")
            );
            Assert.That(source, Does.Contain("void Run()").And.Contain("event global::System.Action Changed"));
            Assert.That(
                source,
                Does.Contain("get => _target.Readable;").And.Contain("set => _target.Readable = value;")
            );
            Assert.That(
                source,
                Does.Contain("get => _target.Writable;").And.Contain("set => _target.Writable = value;")
            );
        });
    }

    [Test]
    public void FriendAssemblyFilter_ExcludesUnselectedMembersWhilePreservingCallableAccessors()
    {
        var references = CreateExternalTargetReferences(assemblyAttributes: FriendAssemblyAttribute);
        var result = RunGeneratorWithReferences(
            $$"""
            using Macaron.MemberExposure;

            public partial class Wrapper
            {
                [{{PublicMemberFilter}}]
                private readonly Target _target = new();
            }
            """,
            references
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("Hidden"));
            Assert.That(source, Does.Contain("void Run()").And.Contain("event global::System.Action Changed"));
            Assert.That(
                source,
                Does.Contain("get => _target.Readable;").And.Contain("set => _target.Readable = value;")
            );
            Assert.That(
                source,
                Does.Contain("get => _target.Writable;").And.Contain("set => _target.Writable = value;")
            );
        });
    }

    private static ImmutableArray<MetadataReference> CreateExternalTargetReferences(string assemblyAttributes = "")
    {
        var references = CreateReferences(typeof(MemberExposure.ExposeAttribute));
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExternalTargets",
            syntaxTrees: [CSharpSyntaxTree.ParseText(assemblyAttributes + TargetSource)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        Assert.That(compilation.GetDiagnostics(), Is.Empty);

        return references.Add(compilation.ToMetadataReference());
    }
}
