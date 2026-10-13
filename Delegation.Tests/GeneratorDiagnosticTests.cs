using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class GeneratorDiagnosticTests
{
    [Test]
    public void NonPartialImplementType_ReportsErrorWithoutGeneratingSource()
    {
        const string source =
            """
            using Macaron.InterfaceDelegation;
            public interface IRunner { void Run(); }
            public sealed class Runner : IRunner { public void Run() { } }
            public class Wrapper
            {
                [Implement(typeof(IRunner))] private readonly Runner _runner = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0001");

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.GetMessage(), Is.EqualTo("Type 'Wrapper' must be declared partial to use Implement"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.IndexOf("Implement(", StringComparison.Ordinal)));
        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    [Test]
    public void NonPartialExposeType_ReportsErrorWithoutGeneratingSource()
    {
        const string source =
            """
            using Macaron.MemberExposure;
            public sealed class Worker { public void Run() { } }
            public class Wrapper
            {
                [Expose] private readonly Worker _worker = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAME0001");

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.GetMessage(), Is.EqualTo("Type 'Wrapper' must be declared partial to use Expose"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.IndexOf("Expose]", StringComparison.Ordinal)));
        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    [Test]
    public void NonPartialOuterType_ReportsBothFeatureErrorsWithoutGeneratingSource()
    {
        const string source =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;
            public interface IRunner { void Run(); }
            public sealed class Runner : IRunner { public void Run() { } }
            public class Outer
            {
                public partial class Wrapper
                {
                    [Implement(typeof(IRunner))] private readonly Runner _runner = new();
                    [Expose] private readonly Runner _exposed = new();
                }
            }
            """;

        var result = RunGenerator(source);
        var diagnostics = result.Diagnostics.Where(static diagnostic => diagnostic.Id is "MAID0001" or "MAME0001").ToArray();

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EquivalentTo(new[] { "MAID0001", "MAME0001" }));
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage()),
            Is.EquivalentTo(new[]
            {
                "Type 'Outer' must be declared partial to use Implement",
                "Type 'Outer' must be declared partial to use Expose",
            }));
        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
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
        Assert.That(diagnostic.GetMessage(), Does.Contain("Implement attribute")
            .And.Contain("non-generic or constructed generic interface")
            .And.Contain("exactly one directly implemented interface"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.IndexOf("typeof(", StringComparison.Ordinal)));
    }

    [TestCase("public sealed class Runner { }", "Runner")]
    [TestCase("public interface IFirst { } public interface ISecond { } public sealed class Runner : IFirst, ISecond { }", "Runner")]
    [TestCase("public interface IRunner { void Run(); } public class Base : IRunner { public void Run() { } } public sealed class Runner : Base { }", "Runner")]
    public void OmittedImplementType_ReportsExistingDiagnostic_When_DirectInterfaceCountIsNotOne(
        string targetDeclaration,
        string targetTypeName
    )
    {
        var source =
            $$"""
            using Macaron.InterfaceDelegation;
            {{targetDeclaration}}
            public partial class Wrapper
            {
                [Implement] private readonly {{targetTypeName}} _target = new();
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0002");

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.GetMessage(), Does.Contain("exactly one directly implemented interface").And.Contain("Runner"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.IndexOf("Implement]", StringComparison.Ordinal)));
        Assert.That(result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
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

        var (diagnostics, _) = CompileAndGetResults(sourceCode);

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "MAID0003"));
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

        var (diagnostics, _) = CompileAndGetResults(sourceCode);

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "MAID0004"));
    }

    [Test]
    public void ReportsDiagnostic_When_ExposeOptionReferencesMissingMember()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public class ExposeTarget
            {
                public void Existing() { }
            }

            public partial class TestClass
            {
                [Expose(
                    filter: new[] { "MissingFilter" },
                    remove: new[] { "MissingRemove" },
                    rename: new[] { "MissingRename:Renamed" }
                )]
                private readonly ExposeTarget _impl = new();
            }
            """;

        var (diagnostics, _) = CompileAndGetResults(sourceCode);
        var mame0002Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAME0002")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.That(mame0002Diagnostics, Has.Length.EqualTo(3));
        Assert.That(mame0002Diagnostics.Select(diagnostic => diagnostic.GetMessage()), Is.EqualTo(new[]
        {
            "The member 'MissingFilter' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'filter'",
            "The member 'MissingRemove' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'remove'",
            "The member 'MissingRename' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'rename'",
        }));
        Assert.That(mame0002Diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start), Is.EqualTo(new[]
        {
            sourceCode.IndexOf("\"MissingFilter\"", StringComparison.Ordinal),
            sourceCode.IndexOf("\"MissingRemove\"", StringComparison.Ordinal),
            sourceCode.IndexOf("\"MissingRename:Renamed\"", StringComparison.Ordinal),
        }));
    }

    [Test]
    public void ReportsDiagnostic_When_ImplementTargetDoesNotImplementInterfaceMembers()
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

        var (diagnostics, generatedCode) = CompileAndGetResults(sourceCode);
        var maid0006Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAID0005")
            .OrderBy(diagnostic => diagnostic.GetMessage(), StringComparer.Ordinal)
            .ToArray();
        var messages = maid0006Diagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();

        Assert.That(generatedCode, Is.Empty);
        Assert.That(maid0006Diagnostics, Has.Length.EqualTo(2));
        Assert.That(messages.Any(message => message.Contains("FooImpl", StringComparison.Ordinal) && message.Contains("IFoo.Count", StringComparison.Ordinal)), Is.True);
        Assert.That(messages.Any(message => message.Contains("FooImpl", StringComparison.Ordinal) && message.Contains("Run", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void ReportsDiagnostic_When_ImplementTargetEventSignatureDoesNotMatch()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            using System;

            public interface INotifier
            {
                event EventHandler? Changed;

                void Notify();
            }

            public class NotifierImpl
            {
                public event Action? Changed;

                public void Notify() { }
            }

            public partial class TestEventDelegation : INotifier
            {
                [Implement(typeof(INotifier))]
                private readonly NotifierImpl _impl = new NotifierImpl();
            }
            """;

        var (diagnostics, generatedCode) = CompileAndGetResults(sourceCode);
        var maid0006Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAID0005")
            .ToArray();

        Assert.That(generatedCode, Is.Empty);
        Assert.That(maid0006Diagnostics, Has.Length.EqualTo(1));
        Assert.That(maid0006Diagnostics[0].GetMessage(), Does.Contain("INotifier.Changed"));
    }

    [Test]
    public void RejectsInaccessibleDuckTypedMembers()
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

            public sealed class DuckFoo
            {
                private void Run() { }
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo))]
                private readonly DuckFoo _impl = new();
            }
            """;

        var (diagnostics, generatedCode) = CompileAndGetResults(sourceCode);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAID0005"));
            Assert.That(generatedCode, Is.Empty);
        });
    }
}
