using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Macaron.InterfaceDelegation;
using Macaron.MemberExposure;

namespace Macaron.Delegation.Tests;

[TestFixture]
public class DelegationIncrementalGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References = Helper.CreateReferences(
        typeof(ImplementAttribute),
        typeof(ExposeAttribute)
    );

    [Test]
    public void RecognizesAliasedMemberExposureAttribute()
    {
        const string source = """
            using ExposeAlias = Macaron.MemberExposure.ExposeAttribute;
            public sealed class Target { public int Value => 42; }
            public partial class Wrapper
            {
                [ExposeAlias(filter: new[] { "Value" })] private Target _target = new();
            }
            """;
        var compilation = CreateCompilation(("Wrapper.cs", source));
        var driver = CreateTrackedDriver();
        var result = Run(ref driver, compilation, out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(GetSources(result), Has.Length.EqualTo(1));
        Assert.That(GetReasons(result, "ExposeAnalysisOutput"), Is.EqualTo(new[] { IncrementalStepRunReason.New }));
        Assert.That(GetReasons(result, "ImplementAnalysisOutput"), Is.Empty);
    }

    [TestCase("_implement", "ImplementSourceOutput", "ExposeSourceOutput")]
    [TestCase("_expose", "ExposeSourceOutput", "ImplementSourceOutput")]
    public void EditingOneFeature_PreservesOtherGeneratorOutputCache(
        string fieldName,
        string changedTrackingName,
        string cachedTrackingName
    )
    {
        const string source = """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;
            public interface IRunner { void Run(); }
            public sealed class Target : IRunner
            {
                public void Run() { }
                public int Value => 42;
            }
            public partial class Wrapper : IRunner
            {
                [Implement(typeof(IRunner))] private Target _implement = new();
                [Expose(filter: new[] { "Value" })] private Target _expose = new();
            }
            """;
        var compilation = CreateCompilation(("Wrapper.cs", source));
        var driver = CreateTrackedDriver();
        var original = Run(ref driver, compilation, out var originalCompilation);
        AssertNoCompilationErrors(originalCompilation);
        var oldTree = compilation.SyntaxTrees.Single();
        var newTree = oldTree.WithChangedText(SourceText.From(source.Replace(fieldName, fieldName + "Changed")));
        var result = Run(ref driver, compilation.ReplaceSyntaxTree(oldTree, newTree), out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(GetReasons(result, changedTrackingName), Is.EqualTo(new[] { IncrementalStepRunReason.Modified }));
        Assert.That(GetReasons(result, cachedTrackingName), Is.EqualTo(new[] { IncrementalStepRunReason.Cached }));
        var originalCached = original.Results.Single(result => result.TrackedSteps.ContainsKey(cachedTrackingName));
        var currentCached = result.Results.Single(result => result.TrackedSteps.ContainsKey(cachedTrackingName));
        Assert.That(currentCached.GeneratedSources.Single().HintName, Is.EqualTo(originalCached.GeneratedSources.Single().HintName));
        Assert.That(currentCached.GeneratedSources.Single().SourceText.ToString(),
            Is.EqualTo(originalCached.GeneratedSources.Single().SourceText.ToString()));
    }

    [Test]
    public void CachesUnchangedOutput_WhenAnotherTargetChanges()
    {
        const string sourceA =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IA { void RunA(); }
            public sealed class AImpl : IA { public void RunA() { } }
            public partial class A : IA
            {
                [Implement(typeof(IA))]
                private readonly IA _implA = new AImpl();
            }
            """;
        const string sourceB =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public interface IB { void RunB(); }
            public sealed class BImpl : IB { public void RunB() { } }
            public partial class B : IB
            {
                [Implement(typeof(IB))]
                private readonly IB _implB = new BImpl();
            }
            """;
        var compilation = CreateCompilation(("A.cs", sourceA), ("B.cs", sourceB));
        var driver = CreateTrackedDriver();
        _ = Run(ref driver, compilation, out _);

        var oldTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "A.cs");
        var oldText = oldTree.GetText();
        var fieldNameStart = oldText.ToString().IndexOf("_implA", StringComparison.Ordinal);
        var changedText = oldText.WithChanges(new TextChange(
            new TextSpan(fieldNameStart, "_implA".Length),
            "_renamedA"
        ));
        var changedTree = oldTree.WithChangedText(changedText);
        var changedCompilation = compilation.ReplaceSyntaxTree(oldTree, changedTree);

        var result = Run(ref driver, changedCompilation, out var outputCompilation);
        var reasons = GetReasons(result, "ImplementSourceOutput");
        var analysisReasons = GetReasons(result, "ImplementAnalysisOutput");

        AssertNoCompilationErrors(outputCompilation);
        Assert.Multiple(() =>
        {
            Assert.That(reasons.Count(static reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
            Assert.That(reasons.Count(static reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
            Assert.That(analysisReasons.Count(static reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
            Assert.That(analysisReasons.Count(static reason => reason == IncrementalStepRunReason.Unchanged), Is.EqualTo(1));
            Assert.That(GetSources(result), Has.Length.EqualTo(2));
            Assert.That(
                GetSources(result).Count(static source => source.SourceText.ToString().Contains("_renamedA", StringComparison.Ordinal)),
                Is.EqualTo(1)
            );
            Assert.That(
                GetSources(result).Count(static source => source.SourceText.ToString().Contains("_implB", StringComparison.Ordinal)),
                Is.EqualTo(1)
            );
        });
    }

    [Test]
    public void CachesUnchangedExposeOutput_WhenAnotherTargetChanges()
    {
        const string sourceA =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public sealed class ATarget { public void RunA() { } }
            public partial class A
            {
                [Expose]
                private readonly ATarget _implA = new();
            }
            """;
        const string sourceB =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            public sealed class BTarget { public void RunB() { } }
            public partial class B
            {
                [Expose]
                private readonly BTarget _implB = new();
            }
            """;
        var compilation = CreateCompilation(("A.cs", sourceA), ("B.cs", sourceB));
        var driver = CreateTrackedDriver();
        _ = Run(ref driver, compilation, out _);

        var oldTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "A.cs");
        var oldText = oldTree.GetText();
        var fieldNameStart = oldText.ToString().IndexOf("_implA", StringComparison.Ordinal);
        var changedText = oldText.WithChanges(new TextChange(
            new TextSpan(fieldNameStart, "_implA".Length),
            "_renamedA"
        ));
        var changedTree = oldTree.WithChangedText(changedText);
        var changedCompilation = compilation.ReplaceSyntaxTree(oldTree, changedTree);

        var result = Run(ref driver, changedCompilation, out var outputCompilation);
        var sourceReasons = GetReasons(result, "ExposeSourceOutput");
        var analysisReasons = GetReasons(result, "ExposeAnalysisOutput");

        AssertNoCompilationErrors(outputCompilation);
        Assert.Multiple(() =>
        {
            Assert.That(sourceReasons.Count(static reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
            Assert.That(sourceReasons.Count(static reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
            Assert.That(analysisReasons.Count(static reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
            Assert.That(analysisReasons.Count(static reason => reason == IncrementalStepRunReason.Unchanged), Is.EqualTo(1));
            Assert.That(GetSources(result), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void IgnoresUnrelatedAttributesBeforeSemanticTransform()
    {
        const string source =
            """
            using System;

            namespace Other
            {
                [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
                public sealed class ImplementAttribute : Attribute { }

                [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
                public sealed class ExposeAttribute : Attribute { }
            }

            namespace Example
            {
                [AttributeUsage(AttributeTargets.All)]
                public sealed class NoiseAttribute : Attribute { }

                public sealed record NoiseRecord([Noise] int Value);

                public sealed class NoiseTarget
                {
                    [Obsolete]
                    private int _field;

                    [Noise]
                    public int Value { get; }

                    [Other.Implement]
                    private int _otherImplement;

                    [Other.Expose]
                    private int _otherExpose;
                }
            }
            """;
        var compilation = CreateCompilation(("Noise.cs", source));
        var driver = CreateTrackedDriver();

        var result = Run(ref driver, compilation, out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.Multiple(() =>
        {
            Assert.That(GetSources(result), Is.Empty);
            Assert.That(GetReasons(result, "ImplementAnalysisOutput"), Is.Empty);
            Assert.That(GetReasons(result, "ExposeAnalysisOutput"), Is.Empty);
        });
    }

    [Test]
    public void RecognizesAliasedImplementAttribute()
    {
        const string source =
            """
            using ImplementAlias = Macaron.InterfaceDelegation.ImplementAttribute;

            public interface IFoo { void Run(); }
            public sealed class Foo : IFoo { public void Run() { } }
            public partial class Wrapper : IFoo
            {
                [ImplementAlias(typeof(IFoo))]
                private readonly IFoo _impl = new Foo();
            }
            """;
        var compilation = CreateCompilation(("Alias.cs", source));
        var driver = CreateTrackedDriver();

        var result = Run(ref driver, compilation, out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.That(GetSources(result), Has.Length.EqualTo(1));
    }

    [Test]
    public void PromotesNextPartialTarget_WhenCanonicalImplementIsRemoved()
    {
        const string contracts =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public interface IFoo { void Run(); }
            public sealed class Foo : IFoo { public void Run() { } }
            """;
        const string partA =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo))] private readonly IFoo _a = new Foo();
            }
            """;
        const string partB =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public partial class Wrapper
            {
                [Implement(typeof(IFoo))] private readonly IFoo _b = new Foo();
            }
            """;
        var compilation = CreateCompilation(
            ("Contracts.cs", contracts),
            ("Wrapper.A.cs", partA),
            ("Wrapper.B.cs", partB)
        );
        var driver = CreateTrackedDriver();

        var firstResult = Run(ref driver, compilation, out _);
        Assert.Multiple(() =>
        {
            Assert.That(GetSources(firstResult), Has.Length.EqualTo(1));
            Assert.That(GetSources(firstResult)[0].SourceText.ToString(), Does.Contain("_a.Run()"));
            Assert.That(firstResult.Diagnostics.Count(static diagnostic => diagnostic.Id == "MAID0003"), Is.EqualTo(1));
        });

        var oldTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Wrapper.A.cs");
        var oldText = oldTree.GetText();
        var attributeStart = oldText.ToString().IndexOf("[Implement(typeof(IFoo))] ", StringComparison.Ordinal);
        var changedText = oldText.WithChanges(new TextChange(
            new TextSpan(attributeStart, "[Implement(typeof(IFoo))] ".Length),
            ""
        ));
        var changedTree = oldTree.WithChangedText(changedText);
        var changedCompilation = compilation.ReplaceSyntaxTree(oldTree, changedTree);

        var secondResult = Run(ref driver, changedCompilation, out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.Multiple(() =>
        {
            Assert.That(GetSources(secondResult), Has.Length.EqualTo(1));
            Assert.That(GetSources(secondResult)[0].SourceText.ToString(), Does.Contain("_b.Run()"));
            Assert.That(secondResult.Diagnostics, Has.None.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAID0003"));
        });
    }

    [Test]
    public void CanonicalImplementFollowsCompilationSyntaxTreeOrder()
    {
        const string contracts =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public interface IFoo { void Run(); }
            public sealed class Foo : IFoo { public void Run() { } }
            """;
        const string partA =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo))] private readonly IFoo _a = new Foo();
            }
            """;
        const string partB =
            """
            using Macaron.InterfaceDelegation;
            using Macaron.MemberExposure;

            namespace Example;

            public partial class Wrapper
            {
                [Implement(typeof(IFoo))] private readonly IFoo _b = new Foo();
            }
            """;
        var compilation = CreateCompilation(
            ("Contracts.cs", contracts),
            ("Wrapper.A.cs", partA),
            ("Wrapper.B.cs", partB)
        );
        var driver = CreateTrackedDriver();

        var firstResult = Run(ref driver, compilation, out _);
        Assert.That(GetSources(firstResult).Single().SourceText.ToString(), Does.Contain("_a.Run()"));

        var reorderedCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(
            compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Contracts.cs"),
            compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Wrapper.B.cs"),
            compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Wrapper.A.cs")
        );

        var secondResult = Run(ref driver, reorderedCompilation, out var outputCompilation);

        AssertNoCompilationErrors(outputCompilation);
        Assert.Multiple(() =>
        {
            Assert.That(GetSources(secondResult), Has.Length.EqualTo(1));
            Assert.That(GetSources(secondResult)[0].SourceText.ToString(), Does.Contain("_b.Run()"));
            Assert.That(secondResult.Diagnostics.Count(static diagnostic => diagnostic.Id == "MAID0003"), Is.EqualTo(1));
        });
    }

    private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources.Select(static source =>
            CSharpSyntaxTree.ParseText(source.Source, path: source.Path)
        );
        return CSharpCompilation.Create(
            assemblyName: "Macaron.Delegation.IncrementalTests",
            syntaxTrees: syntaxTrees,
            references: References,
            options: new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static GeneratorDriver CreateTrackedDriver()
    {
        return CSharpGeneratorDriver.Create(
            generators:
            [
                new ImplementGenerator().AsSourceGenerator(),
                new ExposeGenerator().AsSourceGenerator()
            ],
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
    }

    private static GeneratorDriverRunResult Run(
        ref GeneratorDriver driver,
        Compilation compilation,
        out Compilation outputCompilation
    )
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out _
        );
        return driver.GetRunResult();
    }

    private static ImmutableArray<IncrementalStepRunReason> GetReasons(
        GeneratorDriverRunResult result,
        string trackingName
    )
    {
        return [
            ..result.Results
                .SelectMany(result => result.TrackedSteps.TryGetValue(trackingName, out var steps)
                    ? steps
                    : ImmutableArray<IncrementalGeneratorRunStep>.Empty)
                .SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
        ];
    }

    private static ImmutableArray<GeneratedSourceResult> GetSources(GeneratorDriverRunResult result)
    {
        return [..result.Results.SelectMany(static result => result.GeneratedSources)];
    }

    private static void AssertNoCompilationErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }
}
