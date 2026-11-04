using Macaron.InterfaceDelegation;
using Macaron.MemberExposure;
using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class GeneratorIsolationTests
{
    private const string Source =
        """
        using Macaron.InterfaceDelegation;
        using Macaron.MemberExposure;

        public interface IRunner
        {
            void Run();
        }

        public sealed class Target : IRunner
        {
            public void Run() { }

            public int Value => 42;
        }

        public partial class Wrapper
        {
            [Implement(typeof(IRunner), Mode = ImplementationMode.Implicit)]
            [Expose(filter: new[] { "Value" })]
            private Target _target = new();
        }

        """;

    public static IEnumerable<TestCaseData> GeneratorOrders =>
    [
        new TestCaseData((object)new IIncrementalGenerator[] { new ImplementGenerator(), new ExposeGenerator() })
            .SetName("CombinedGenerators_PreserveSources_WhenImplementRunsFirst"),
        new TestCaseData((object)new IIncrementalGenerator[] { new ExposeGenerator(), new ImplementGenerator() })
            .SetName("CombinedGenerators_PreserveSources_WhenExposeRunsFirst"),
    ];

    [Test]
    public void ImplementAlone_ProducesOnlyInterfaceMembers()
    {
        var result = RunGenerator(Source, new ImplementGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0].SourceText.ToString(),
            Does.Contain("void Run()").And.Not.Contain("int Value")
        );
    }

    [Test]
    public void ExposeAlone_ProducesOnlySelectedMembers()
    {
        var result = RunGenerator(Source, new ExposeGenerator());

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0].SourceText.ToString(),
            Does.Contain("int Value").And.Not.Contain("void Run()")
        );
    }

    [TestCaseSource(nameof(GeneratorOrders))]
    public void CombinedGenerators_PreserveStandaloneSourcesAndHintNames(IIncrementalGenerator[] generators)
    {
        var implement = RunGenerator(Source, new ImplementGenerator());
        var expose = RunGenerator(Source, new ExposeGenerator());
        var expected = implement
            .GeneratedSources
            .Concat(expose.GeneratedSources)
            .Select(static source => (source.HintName, Source: source.SourceText.ToString()))
            .ToArray();

        var result = RunGenerator(Source, generators);
        var actual = result
            .GeneratedSources
            .Select(static source => (source.HintName, Source: source.SourceText.ToString()))
            .ToArray();
        var hintNames = result.GeneratedSources.Select(static source => source.HintName).ToArray();

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(actual, Has.Length.EqualTo(2));
            Assert.That(actual, Is.EquivalentTo(expected));
            Assert.That(hintNames, Is.Unique);
        });
    }

    [Test]
    public void EachGenerator_ReportsOnlyItsOwnDiagnostics()
    {
        var source = Source.Replace("typeof(IRunner)", "typeof(int)")
            .Replace("\"Value\"", "\"Missing\"");
        var implement = RunGenerator(source, new ImplementGenerator());
        var expose = RunGenerator(source, new ExposeGenerator());
        var combined = RunGenerator(source);

        Assert.That(implement.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAID0002" }));
        Assert.That(expose.Diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MAME0002" }));
        Assert.That(
            combined.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(new[] { "MAID0002", "MAME0002" })
        );
        AssertSuccessfulGeneration(expose);
    }

    [Test]
    public void Implement_CompilesWithoutMemberExposureReference()
    {
        var source = Source
            .Replace("using Macaron.MemberExposure;", "")
            .Replace("[Expose(filter: new[] { \"Value\" })]", "")
            .Replace("class Wrapper", "class Wrapper : IRunner");
        var result = RunGeneratorWithReferences(
            source,
            CreateReferences(typeof(ImplementAttribute)), new ImplementGenerator()
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(result.OutputCompilation.GetTypeByMetadataName("Macaron.MemberExposure.ExposeAttribute"), Is.Null);
    }

    [Test]
    public void Expose_CompilesWithoutInterfaceDelegationReference()
    {
        var source = Source
            .Replace("using Macaron.InterfaceDelegation;", "")
            .Replace("[Implement(typeof(IRunner), Mode = ImplementationMode.Implicit)]", "");
        var result = RunGeneratorWithReferences(
            source,
            CreateReferences(typeof(ExposeAttribute)), new ExposeGenerator()
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(
            result.OutputCompilation.GetTypeByMetadataName("Macaron.InterfaceDelegation.ImplementAttribute"),
            Is.Null
        );
    }

    [Test]
    public void LegacyAttributeNames_DoNotTriggerEitherGenerator()
    {
        const string source =
            """
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
        Assert.That(
            result.Diagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("MA", StringComparison.Ordinal)),
            Is.Empty
        );
    }

    [TestCase(typeof(ImplementGenerator), "Macaron.InterfaceDelegation", "Macaron.MemberExposure")]
    [TestCase(typeof(ExposeGenerator), "Macaron.MemberExposure", "Macaron.InterfaceDelegation")]
    public void GeneratorAssembly_ExportsOnlyOwnGeneratorWithoutOtherFeatureDependencies(
        Type generatorType,
        string ownFeature,
        string otherFeature
    )
    {
        var assembly = generatorType.Assembly;
        var registeredGenerators = assembly.GetTypes()
            .Where(static type => type.IsDefined(typeof(GeneratorAttribute), inherit: false))
            .ToArray();
        var referencedAssemblies = assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(registeredGenerators, Is.EqualTo(new[] { generatorType }));
            Assert.That(referencedAssemblies, Has.None.StartsWith(otherFeature));
            Assert.That(assembly.GetExportedTypes(), Is.EqualTo(new[] { generatorType }));
            Assert.That(assembly.GetName().Name, Is.EqualTo(ownFeature + ".Generator"));
        });
    }

    [Test]
    public void AttributeAssemblies_KeepPublicApisSeparate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(ImplementAttribute).Assembly, Is.Not.EqualTo(typeof(ExposeAttribute).Assembly));
            Assert.That(
                typeof(ImplementAttribute).Assembly.GetExportedTypes(),
                Is.EquivalentTo(new[] { typeof(ImplementAttribute), typeof(ImplementationMode) })
            );
            Assert.That(
                typeof(ExposeAttribute).Assembly.GetExportedTypes(),
                Is.EqualTo(new[] { typeof(ExposeAttribute) })
            );
        });
    }
}
