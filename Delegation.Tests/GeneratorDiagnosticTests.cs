using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class GeneratorDiagnosticTests
{
    private const string NoDirectInterfaces = "public sealed class Runner { }";

    private const string MultipleDirectInterfaces =
        """
        public interface IFirst { }

        public interface ISecond { }

        public sealed class Runner : IFirst, ISecond { }
        """;

    private const string InheritedInterfaceOnly =
        """
        public interface IRunner
        {
            void Run();
        }

        public class Base : IRunner
        {
            public void Run() { }
        }

        public sealed class Runner : Base { }
        """;

    [TestCase("Implement(typeof(IRunner))", "Implement", "MAID0001")]
    [TestCase("Expose", "Expose", "MAME0001")]
    public void NonPartialType_ReportsErrorAtAttributeWithoutGeneratingSource(
        string attribute,
        string feature,
        string diagnosticId
    )
    {
        var source =
            $$"""
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IRunner
            {
                void Run();
            }

            public sealed class Runner : IRunner
            {
                public void Run() { }
            }

            public class Wrapper
            {
                [{{attribute}}]
                private readonly Runner _runner = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.Id == diagnosticId);
        var attributeStart = source.IndexOf(attribute, StringComparison.Ordinal);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Id, Is.EqualTo(diagnosticId));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(
                diagnostic.GetMessage(),
                Is.EqualTo($"Type 'Wrapper' must be declared partial to use {feature}")
            );
            Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(attributeStart));
            Assert.That(result.GeneratedSources, Is.Empty);
        });
    }

    [Test]
    public void NonPartialOuterType_ReportsBothFeatureErrorsWithoutGeneratingSource()
    {
        const string source =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;
            public interface IRunner
            {
                void Run();
            }

            public sealed class Runner : IRunner
            {
                public void Run() { }
            }

            public class Outer
            {
                public partial class Wrapper
                {
                    [Implement(typeof(IRunner))]
                    private readonly Runner _runner = new();

                    [Expose]
                    private readonly Runner _exposed = new();
                }
            }
            """;

        var result = RunGenerator(source);
        var diagnostics = result.Diagnostics.Where(static diagnostic => diagnostic.Id is "MAID0001" or "MAME0001").ToArray();

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(new[] { "MAID0001", "MAME0001" })
        );
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.GetMessage()),
            Is.EquivalentTo(new[]
            {
                "Type 'Outer' must be declared partial to use Implement",
                "Type 'Outer' must be declared partial to use Expose",
            })
        );
        Assert.That(result.GeneratedSources, Is.Empty);
        AssertSuccessfulGeneration(result);
    }

    [TestCase("int")]
    [TestCase("System.Collections.Generic.IEnumerable<>")]
    public void InvalidImplementInterface_ReportsAccurateMessageAndLocation(string typeName)
    {
        var source =
            $$"""
            using Macaron.InterfaceDelegation;
            public partial class Wrapper
            {
                [Implement(typeof({{typeName}}))] private object _target = new();
            }
            """;
        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0002");

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(
            diagnostic.GetMessage(),
            Does
                .Contain("Implement attribute")
                .And.Contain("non-generic or constructed generic interface")
                .And.Contain("exactly one directly implemented interface")
        );
        Assert.That(
            diagnostic.Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf("typeof(", StringComparison.Ordinal))
        );
    }

    [TestCase(NoDirectInterfaces)]
    [TestCase(MultipleDirectInterfaces)]
    [TestCase(InheritedInterfaceOnly)]
    public void OmittedImplementType_ReportsErrorUnlessExactlyOneDirectInterfaceExists(string targetDeclaration)
    {
        var source =
            $$"""
            using Macaron.InterfaceDelegation;
            {{targetDeclaration}}
            public partial class Wrapper
            {
                [Implement] private readonly Runner _target = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0002");

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(
            diagnostic.GetMessage(),
            Does.Contain("exactly one directly implemented interface").And.Contain("Runner")
        );
        Assert.That(
            diagnostic.Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf("Implement]", StringComparison.Ordinal))
        );
        AssertSuccessfulGeneration(result);
    }

    [Test]
    public void ReportsDiagnostic_When_ImplementAttributeAppliedToValueTypeProperty()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IFoo
            {
                int GetValue();
            }

            public partial class TestClass : IFoo
            {
                [Implement(typeof(IFoo))]
                private int Impl { get; } = 42;
            }
            """;

        var result = RunGenerator(sourceCode);

        Assert.That(result.Diagnostics, Has.Some.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAID0003"));
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [Test]
    public void ReportsDiagnostic_When_InterfaceIsDelegatedMoreThanOnce()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IFoo
            {
                void Bar();
            }

            public class FooImpl : IFoo
            {
                public void Bar() { }
            }

            public partial class TestClass : IFoo
            {
                [Implement(typeof(IFoo))]
                private readonly IFoo _impl1 = new FooImpl();

                [Implement(typeof(IFoo))]
                private readonly IFoo _impl2 = new FooImpl();
            }
            """;

        var result = RunGenerator(sourceCode);

        Assert.That(result.Diagnostics, Has.Exactly(1).Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAID0004"));
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        AssertSuccessfulGeneration(result);
    }

    [TestCase("filter", "MissingFilter", "MissingFilter")]
    [TestCase("remove", "MissingRemove", "MissingRemove")]
    [TestCase("rename", "MissingRename:Renamed", "MissingRename")]
    public void MissingExposeMember_ReportsWarningAtOptionValue(string option, string optionValue, string memberName)
    {
        var source = $$"""
            using Macaron.MemberExposure;

            namespace Macaron.Delegation.Tests;

            public class ExposeTarget
            {
                public void Existing() { }
            }

            public partial class Wrapper
            {
                [Expose({{option}}: new[] { "{{optionValue}}" })]
                private readonly ExposeTarget _target = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single();
        var optionStart = source.IndexOf($"\"{optionValue}\"", StringComparison.Ordinal);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Id, Is.EqualTo("MAME0002"));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(
                diagnostic.GetMessage(),
                Is.EqualTo($"The member '{memberName}' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option '{option}'")
            );
            Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(optionStart));
        });
    }

    [Test]
    public void ReportsEveryMissingDuckTypedContractMember()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IFoo
            {
                void Run(int value);

                int Count { get; }
            }

            public class FooImpl
            {
                public void Run() { }

                public string Count => "";
            }

            public partial class TestClass : IFoo
            {
                [Implement(typeof(IFoo))]
                private readonly FooImpl _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);
        var memberDiagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == "MAID0005")
            .OrderBy(diagnostic => diagnostic.GetMessage(), StringComparer.Ordinal)
            .ToArray();
        var messages = memberDiagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(memberDiagnostics, Has.Length.EqualTo(2));
        Assert.That(messages, Has.All.Contains("FooImpl"));
        Assert.That(messages, Has.Exactly(1).Contains("IFoo.Count"));
        Assert.That(messages, Has.Exactly(1).Contains("IFoo.Run"));
    }

    [TestCase("event System.EventHandler? Changed;", "public event System.Action? Changed;", "ITarget.Changed")]
    [TestCase("void Run();", "private void Run() { }", "ITarget.Run")]
    [TestCase("int Value { get; set; }", "public int Value { get; private set; }", "ITarget.Value")]
    [TestCase("int Value { get; set; }", "public int Value { private get; set; }", "ITarget.Value")]
    [TestCase("int this[int index] { get; set; }", "public int this[int index] { get => 0; private set { } }", "ITarget.this")]
    public void UnsatisfiedDuckTypedContract_ReportsMemberDiagnostic(
        string contract,
        string implementation,
        string expectedMember
    )
    {
        var result = RunGenerator(
            $$"""
            using Macaron.InterfaceDelegation;

            public interface ITarget
            {
                {{contract}}
            }

            public class Target
            {
                {{implementation}}
            }

            public partial class Wrapper
            {
                [Implement(typeof(ITarget), Mode = ImplementationMode.Implicit)]
                private readonly Target _target = new();
            }
            """
        );
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0005");

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Target").And.Contain(expectedMember));
            Assert.That(result.GeneratedSources, Is.Empty);
        });
    }
}
