using Microsoft.CodeAnalysis;
using static Macaron.Delegation.Tests.Helper;

using Macaron.InterfaceDelegation;
using Macaron.MemberExposure;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class GeneratorDiagnosticTests
{
    [TestCase("int")]
    [TestCase("System.Collections.Generic.IEnumerable<>")]
    public void InvalidImplementInterface_ReportsAccurateMessageAndLocation(string typeName)
    {
        var source = $$"""
            using Macaron.InterfaceDelegation;
            public partial class Wrapper
            {
                [Implement(typeof({{typeName}}))] private object _target = new();
            }
            """;
        var result = RunGenerator(source);
        var diagnostic = result.Diagnostics.Single(static diagnostic => diagnostic.Id == "MAID0001");

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Implement attribute").And.Contain("constructed generic interfaces"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.IndexOf("typeof(", StringComparison.Ordinal)));
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

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "MAID0002"));
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

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "MAID0003"));
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
        var mame0001Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAME0001")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.That(mame0001Diagnostics, Has.Length.EqualTo(3));
        Assert.That(mame0001Diagnostics.Select(diagnostic => diagnostic.GetMessage()), Is.EqualTo(new[]
        {
            "The member 'MissingFilter' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'filter'",
            "The member 'MissingRemove' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'remove'",
            "The member 'MissingRename' was not found on 'Macaron.Delegation.Tests.ExposeTarget' for Expose option 'rename'",
        }));
        Assert.That(mame0001Diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start), Is.EqualTo(new[]
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
        var maid0005Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAID0005")
            .OrderBy(diagnostic => diagnostic.GetMessage(), StringComparer.Ordinal)
            .ToArray();
        var messages = maid0005Diagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();

        Assert.That(generatedCode, Is.Empty);
        Assert.That(maid0005Diagnostics, Has.Length.EqualTo(2));
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
        var maid0005Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "MAID0005")
            .ToArray();

        Assert.That(generatedCode, Is.Empty);
        Assert.That(maid0005Diagnostics, Has.Length.EqualTo(1));
        Assert.That(maid0005Diagnostics[0].GetMessage(), Does.Contain("INotifier.Changed"));
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
