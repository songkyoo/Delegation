using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ExposeAccessorTests
{
    [TestCase("get => 0;", "get; private set;")]
    [TestCase("set { }", "private get; set;")]
    public void ExistingProperty_IsMatchedUsingOnlyExposedAccessors(string existingAccessors, string targetAccessors)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { {{targetAccessors}} }
            }

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

    [TestCase("get; private set;", "get => _target.Value;", "set =>")]
    [TestCase("get; protected set;", "get => _target.Value;", "set =>")]
    [TestCase("get; private protected set;", "get => _target.Value;", "set =>")]
    [TestCase("private get; set;", "set => _target.Value = value;", "get =>")]
    [TestCase("protected get; set;", "set => _target.Value = value;", "get =>")]
    [TestCase("private protected get; set;", "set => _target.Value = value;", "get =>")]
    public void InaccessibleAccessor_IsOmitted(string accessors, string expectedAccessor, string omittedAccessor)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { {{accessors}} }
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

        Assert.That(source, Does.Contain(expectedAccessor).And.Not.Contain(omittedAccessor));
    }

    [TestCase("internal")]
    [TestCase("protected internal")]
    public void InternalAccessors_AreCallableWithinSameAssembly(string accessibility)
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
    public void NestedWrapper_CanCallPrivateSetter()
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

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("get => _target.Value;").And.Contain("set => _target.Value = value;"));
    }

    [Test]
    public void ProtectedSetter_IsOmittedForBaseTypeReceiver()
    {
        var result = RunGenerator(
            """
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { get; protected set; }
            }

            public partial class Wrapper : Target
            {
                [Expose(rename: new[] { "Value:DelegatedValue" }, IncludeBaseTypes = true)]
                private readonly Target _target = null!;
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("get => _target.Value;").And.Not.Contain("set => _target.Value"));
    }

    [Test]
    public void ProtectedSetter_IsCallableForDerivedTypeReceiver()
    {
        var result = RunGenerator(
            """
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { get; protected set; }
            }

            public partial class Wrapper : Target
            {
                [Expose(rename: new[] { "Value:DelegatedValue" }, IncludeBaseTypes = true)]
                private readonly Wrapper _target = null!;
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("get => _target.Value;").And.Contain("set => _target.Value = value;"));
    }

    [TestCase("private get; init;")]
    [TestCase("init { }")]
    public void PropertyWithoutCallableAccessors_IsSkipped(string accessors)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { {{accessors}} }
            }

            public partial class Wrapper
            {
                [Expose]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [TestCase("private get; init;")]
    [TestCase("init { }")]
    public void FilterNamingPropertyWithoutCallableAccessors_ReportsMissingMember(string accessors)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { {{accessors}} }
            }

            public partial class Wrapper
            {
                [Expose(filter: new[] { "Value" })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var diagnostic = result.Diagnostics.Single();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Id, Is.EqualTo("MAME0002"));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostic.GetMessage(), Does.Contain("'Value'").And.Contain("'filter'"));
            Assert.That(result.GeneratedSources, Is.Empty);
        });
    }

    [Test]
    public void InitProperty_ExposesOnlyGetter()
    {
        var result = RunGenerator(
            """
            using Macaron.MemberExposure;

            public class Target
            {
                public int Value { get; init; }
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

        Assert.That(source, Does.Contain("get => _target.Value;").And.Not.Contain("set").And.Not.Contain("init"));
    }
}
