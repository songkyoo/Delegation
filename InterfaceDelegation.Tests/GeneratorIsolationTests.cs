using Microsoft.CodeAnalysis;

using static Macaron.InterfaceDelegation.Tests.Helper;

namespace Macaron.InterfaceDelegation.Tests;

[TestFixture]
public sealed class GeneratorIsolationTests
{
    private const string Source = """
        using Macaron.InterfaceDelegation;

        public interface IRunner { void Run(); }
        public sealed class Target : IRunner
        {
            public void Run() { }
            public int Value => 42;
        }
        public partial class Wrapper
        {
            [Expose(typeof(IRunner))]
            [Lift(filter: new[] { "Value" })]
            private Target _target = new();
        }
        """;

    [Test]
    public void ExposeAlone_ProducesOnlyInterfaceMembers()
    {
        var result = RunGenerator(Source, new ExposeGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("void Run()").And.Not.Contain("int Value"));
    }

    [Test]
    public void LiftAlone_ProducesOnlySelectedMembers()
    {
        var result = RunGenerator(Source, new LiftGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("int Value").And.Not.Contain("void Run()"));
    }

    [Test]
    public void CombinedGenerators_PreserveStandaloneSourcesAndHintNames_InEitherOrder()
    {
        var expose = RunGenerator(Source, new ExposeGenerator());
        var lift = RunGenerator(Source, new LiftGenerator());
        var expected = expose.GeneratedSources.Concat(lift.GeneratedSources)
            .Select(static source => (source.HintName, Source: source.SourceText.ToString())).ToArray();

        foreach (var generators in new IIncrementalGenerator[][]
        {
            [new ExposeGenerator(), new LiftGenerator()],
            [new LiftGenerator(), new ExposeGenerator()],
        })
        {
            var result = RunGenerator(Source, generators);

            AssertSuccessfulGeneration(result);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources.Select(static source => (source.HintName, Source: source.SourceText.ToString())),
                Is.EquivalentTo(expected));
        }
    }

    [Test]
    public void EachGenerator_ReportsOnlyItsOwnDiagnostics()
    {
        var source = Source.Replace("typeof(IRunner)", "typeof(int)")
            .Replace("\"Value\"", "\"Missing\"");
        var expose = RunGenerator(source, new ExposeGenerator());
        var lift = RunGenerator(source, new LiftGenerator());
        var combined = RunGenerator(source);

        Assert.That(expose.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAID0001" }));
        Assert.That(lift.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAID0004" }));
        Assert.That(combined.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EquivalentTo(new[] { "MAID0001", "MAID0004" }));
        AssertSuccessfulGeneration(lift);
    }

    [Test]
    public void Assembly_RegistersExactlyTheTwoFeatureGenerators()
    {
        var generators = typeof(ExposeGenerator).Assembly.GetTypes()
            .Where(static type => type.IsDefined(typeof(GeneratorAttribute), inherit: false));

        Assert.That(generators, Is.EquivalentTo(new[] { typeof(ExposeGenerator), typeof(LiftGenerator) }));
    }
}
