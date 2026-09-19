using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

using Macaron.InterfaceDelegation;
using Macaron.MemberExposure;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class GeneratorIsolationTests
{
    private const string Source = """
        using Macaron.InterfaceDelegation;
        using Macaron.MemberExposure;

        public interface IRunner { void Run(); }
        public sealed class Target : IRunner
        {
            public void Run() { }
            public int Value => 42;
        }
        public partial class Wrapper
        {
            [Implement(typeof(IRunner), ImplementationMode.Implicit)]
            [Expose(filter: new[] { "Value" })]
            private Target _target = new();
        }
        """;

    [Test]
    public void ImplementAlone_ProducesOnlyInterfaceMembers()
    {
        var result = RunGenerator(Source, new ImplementGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("void Run()").And.Not.Contain("int Value"));
    }

    [Test]
    public void ExposeAlone_ProducesOnlySelectedMembers()
    {
        var result = RunGenerator(Source, new ExposeGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("int Value").And.Not.Contain("void Run()"));
    }

    [Test]
    public void CombinedGenerators_PreserveStandaloneSourcesAndHintNames_InEitherOrder()
    {
        var implement = RunGenerator(Source, new ImplementGenerator());
        var expose = RunGenerator(Source, new ExposeGenerator());
        var expected = implement.GeneratedSources.Concat(expose.GeneratedSources)
            .Select(static source => (source.HintName, Source: source.SourceText.ToString())).ToArray();

        foreach (var generators in new IIncrementalGenerator[][]
        {
            [new ImplementGenerator(), new ExposeGenerator()],
            [new ExposeGenerator(), new ImplementGenerator()],
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
        var implement = RunGenerator(source, new ImplementGenerator());
        var expose = RunGenerator(source, new ExposeGenerator());
        var combined = RunGenerator(source);

        Assert.That(implement.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAID0001" }));
        Assert.That(expose.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAME0001" }));
        Assert.That(combined.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EquivalentTo(new[] { "MAID0001", "MAME0001" }));
        AssertSuccessfulGeneration(expose);
    }

    [Test]
    public void Implement_CompilesWithoutMemberExposureReference()
    {
        var source = Source.Replace("using Macaron.MemberExposure;", "")
            .Replace("[Expose(filter: new[] { \"Value\" })]", "")
            .Replace("class Wrapper", "class Wrapper : IRunner");
        var result = RunGeneratorWithReferences(source, CreateReferences(typeof(ImplementAttribute)), new ImplementGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.OutputCompilation.GetTypeByMetadataName("Macaron.MemberExposure.ExposeAttribute"), Is.Null);
    }

    [Test]
    public void Expose_CompilesWithoutInterfaceDelegationReference()
    {
        var source = Source.Replace("using Macaron.InterfaceDelegation;", "")
            .Replace("[Implement(typeof(IRunner), ImplementationMode.Implicit)]", "");
        var result = RunGeneratorWithReferences(source, CreateReferences(typeof(ExposeAttribute)), new ExposeGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.OutputCompilation.GetTypeByMetadataName("Macaron.InterfaceDelegation.ImplementAttribute"), Is.Null);
    }

    [Test]
    public void LegacyAttributeNames_DoNotTriggerEitherGenerator()
    {
        const string source = """
            namespace Macaron.InterfaceDelegation
            {
                public sealed class ExposeAttribute : System.Attribute { }
                public sealed class LiftAttribute : System.Attribute { }
            }
            public partial class Wrapper
            {
                [Macaron.InterfaceDelegation.Expose]
                [Macaron.InterfaceDelegation.Lift]
                private string _value = "";
            }
            """;
        var result = RunGenerator(source);

        AssertSuccessfulGeneration(result);
        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(result.Diagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("MA", StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    public void EachAssembly_RegistersOnlyItsOwnGenerator_WithoutOtherFeatureDependencies()
    {
        foreach (var (generatorType, ownPrefix, otherPrefix) in new[]
        {
            (typeof(ImplementGenerator), "Macaron.InterfaceDelegation", "Macaron.MemberExposure"),
            (typeof(ExposeGenerator), "Macaron.MemberExposure", "Macaron.InterfaceDelegation"),
        })
        {
            var assembly = generatorType.Assembly;
            var generators = assembly.GetTypes()
                .Where(static type => type.IsDefined(typeof(GeneratorAttribute), inherit: false));

            Assert.That(generators, Is.EqualTo(new[] { generatorType }));
            Assert.That(assembly.GetReferencedAssemblies().Select(static reference => reference.Name),
                Has.None.StartsWith(otherPrefix));
            Assert.That(assembly.GetExportedTypes(), Is.EqualTo(new[] { generatorType }));
            Assert.That(assembly.GetName().Name, Is.EqualTo(ownPrefix + ".Generator"));
        }

        Assert.That(typeof(ImplementAttribute).Assembly, Is.Not.EqualTo(typeof(ExposeAttribute).Assembly));
        Assert.That(typeof(ImplementAttribute).Assembly.GetExportedTypes(),
            Is.EquivalentTo(new[] { typeof(ImplementAttribute), typeof(ImplementationMode) }));
        Assert.That(typeof(ExposeAttribute).Assembly.GetExportedTypes(), Is.EqualTo(new[] { typeof(ExposeAttribute) }));
    }
}
