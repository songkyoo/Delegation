using Macaron.Delegation;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Macaron.MemberExposure;

[Generator]
public class ExposeGenerator : IIncrementalGenerator
{
    private const string ExposeAttributeMetadataName = "Macaron.MemberExposure.ExposeAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var exposeTargets = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: ExposeAttributeMetadataName,
                predicate: static (syntaxNode, _) => DelegationTargetSyntax.IsSupported(syntaxNode),
                transform: static (attributeContext, cancellationToken) => ExposeTargetAnalyzer.Analyze(
                    attributeContext,
                    cancellationToken
                )
            )
            .WithTrackingName("ExposeAnalysisOutput");

        var exposeSources = exposeTargets
            .SelectMany(static (output, _) => output.Source is { } source
                ? ImmutableArray.Create(source)
                : ImmutableArray<GeneratedSourceOutput>.Empty
            )
            .WithTrackingName("ExposeSourceOutput");

        context.RegisterSourceOutput(exposeSources, static (sourceProductionContext, output) =>
        {
            sourceProductionContext.AddSource(
                hintName: output.HintName,
                sourceText: SourceText.From(output.Source, Encoding.UTF8)
            );
        });

        var exposeDiagnostics = exposeTargets
            .SelectMany(static (output, _) => output.Diagnostics)
            .WithTrackingName("ExposeDiagnostics");

        context.RegisterSourceOutput(exposeDiagnostics, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
    }
}
