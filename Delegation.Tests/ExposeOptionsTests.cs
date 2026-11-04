using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ExposeOptionsTests
{
    private const string TargetDeclaration =
        """
        using Macaron.MemberExposure;

        public class TargetBase
        {
            public int BaseValue { get; }
        }

        public class Target : TargetBase
        {
            public int Value => 42;

            public void GetA() { }

            public void GetA(int value) { }

            public void GetB() { }

            public void GetC() { }
        }

        """;

    [Test]
    public void Filter_SelectsEveryOverloadOfNamedMembers()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { "Value", "GetA" })]
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
                Does.Contain("int Value").And.Contain("void GetA()").And.Contain("void GetA(int value)")
            );
            Assert.That(source, Does.Not.Contain("GetB").And.Not.Contain("GetC").And.Not.Contain("BaseValue"));
        });
    }

    [Test]
    public void Remove_ExcludesMembersAfterFiltering()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { "Value", "GetA", "GetB" }, remove: new[] { "GetB" })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(
            source,
            Does.Contain("int Value").And.Contain("void GetA()").And.Not.Contain("GetB").And.Not.Contain("GetC")
        );
    }

    [Test]
    public void Rename_ChangesDeclarationsWhilePreservingTargetMemberNames()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { "Value", "GetA" }, rename: new[] { "Value:Answer", "GetA:Renamed" })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("int Answer").And.Contain("get => _target.Value;"));
            Assert.That(source, Does.Contain("void Renamed()").And.Contain("=> _target.GetA();"));
            Assert.That(source, Does.Contain("void Renamed(int value)").And.Contain("=> _target.GetA(value);"));
        });
    }

    [Test]
    public void RenameCollision_SkipsOnlyMatchingOverload()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                public void Renamed() { }

                [Expose(filter: new[] { "GetA" }, rename: new[] { "GetA:Renamed" })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("void Renamed(int value)").And.Not.Contain("void Renamed()"));
    }

    [Test]
    public void IncludeBaseTypes_AllowsRenamingInheritedMember()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { "BaseValue" }, rename: new[] { "BaseValue:ParentValue" }, IncludeBaseTypes = true)]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("int ParentValue").And.Contain("get => _target.BaseValue;"));
    }

    [TestCase("Expose")]
    [TestCase("Expose(IncludeBaseTypes = false)")]
    public void BaseMembers_AreExcludedUnlessRequested(string attribute)
    {
        var result = RunGenerator(
            TargetDeclaration + $$"""
            public partial class Wrapper
            {
                [{{attribute}}]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("int Value").And.Not.Contain("BaseValue"));
    }

    [Test]
    public void SeparateTargets_ApplyTheirOwnOptions()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { "Value" }, rename: new[] { "Value:Answer" })]
                private readonly Target _first = new();

                [Expose(filter: new[] { "GetB" })]
                private readonly Target _second = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);

        var sources = result.GeneratedSources.Select(static source => source.SourceText.ToString()).ToArray();
        var hintNames = result.GeneratedSources.Select(static source => source.HintName).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(sources, Has.Length.EqualTo(2));
            Assert.That(hintNames, Is.Unique);
            Assert.That(sources, Has.Exactly(1).Contains("get => _first.Value;"));
            Assert.That(sources, Has.Exactly(1).Contains("=> _second.GetB();"));
            Assert.That(sources, Has.None.Contains("GetA"));
        });
    }

    [TestCase("Expose")]
    [TestCase("Expose(null, null, null)")]
    [TestCase("Expose(filter: new string[] { })")]
    [TestCase("Expose(remove: new string[] { })")]
    [TestCase("Expose(rename: new string[] { })")]
    public void EmptyOptions_PreserveDefaultGenerationWithoutDiagnostics(string attribute)
    {
        var result = RunGenerator(
            TargetDeclaration + $$"""
            public partial class Wrapper
            {
                [{{attribute}}]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);

        var source = result.GeneratedSources.Single().SourceText.ToString();

        Assert.That(source, Does.Contain("int Value").And.Contain("void GetA()").And.Contain("void GetB()").And.Contain("void GetC()"));
    }

    [Test]
    public void WhitespaceFilter_RemainsActiveWithoutMissingMemberWarning()
    {
        var result = RunGenerator(
            TargetDeclaration + """
            public partial class Wrapper
            {
                [Expose(filter: new[] { " " })]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Is.Empty);
    }
}
