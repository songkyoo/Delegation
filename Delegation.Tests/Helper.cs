using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Macaron.InterfaceDelegation;
using Macaron.MemberExposure;

namespace Macaron.Delegation.Tests;

internal static class Helper
{
    public sealed record GeneratorTestResult(
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> GeneratedSources,
        Compilation OutputCompilation
    );

    public static void AssertGeneratedCode(string sourceCode, string expected)
    {
        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0].SourceText.ToString().ReplaceLineEndings(),
            Is.EqualTo(expected.ReplaceLineEndings())
        );
    }

    public static void AssertGeneratedCodes(string sourceCode, params string[] expected)
    {
        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(expected.Length));
        Assert.That(
            result.GeneratedSources.Select(static source => source.SourceText.ToString().ReplaceLineEndings()),
            Is.EqualTo(expected.Select(static source => source.ReplaceLineEndings()))
        );
    }

    public static void AssertSuccessfulGeneration(GeneratorTestResult result)
    {
        var errors = result
            .OutputCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }

    public static (ImmutableArray<Diagnostic> diagnostics, string generatedCode) CompileAndGetResults(string sourceCode)
    {
        var result = RunGenerator(sourceCode);
        var generatedCode = result.GeneratedSources.Length > 0
            ? result.GeneratedSources[0].SourceText.ToString()
            : "";

        return (result.Diagnostics, generatedCode);
    }

    public static GeneratorTestResult RunGenerator(string sourceCode, params IIncrementalGenerator[] generators)
    {
        return RunGeneratorWithReferences(
            sourceCode,
            CreateReferences(typeof(ImplementAttribute), typeof(ExposeAttribute)),
            generators
        );
    }

    public static ImmutableArray<MetadataReference> CreateReferences(params Type[] apiTypes)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return [
            ..((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.OrdinalIgnoreCase))
                .Concat(apiTypes.Select(static type => type.Assembly.Location))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path))
        ];
    }

    public static GeneratorTestResult RunGeneratorWithReferences(
        string sourceCode,
        ImmutableArray<MetadataReference> references,
        params IIncrementalGenerator[] generators
    )
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Macaron.Delegation.Tests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        if (generators.Length == 0)
        {
            generators = [new ImplementGenerator(), new ExposeGenerator()];
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(static generator => generator.AsSourceGenerator())
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _
        );
        var result = driver.GetRunResult();
        var allDiagnostics = outputCompilation
            .GetDiagnostics()
            .Concat(result.Diagnostics)
            .ToImmutableArray();

        return new GeneratorTestResult(
            allDiagnostics,
            result.Results.SelectMany(static result => result.GeneratedSources).ToImmutableArray(),
            outputCompilation
        );
    }
}
