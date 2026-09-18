using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Macaron.InterfaceDelegation;

[Generator]
public class LiftGenerator : IIncrementalGenerator
{
    private const string LiftAttributeMetadataName = "Macaron.InterfaceDelegation.LiftAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var liftTargets = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: LiftAttributeMetadataName,
                predicate: static (syntaxNode, _) => DelegationTargetSyntax.IsSupported(syntaxNode),
                transform: static (attributeContext, cancellationToken) => LiftTargetAnalyzer.Analyze(
                    attributeContext,
                    cancellationToken
                )
            )
            .WithTrackingName("LiftAnalysisOutput");

        var liftSources = liftTargets
            .SelectMany(static (output, _) => output.Source is { } source
                ? ImmutableArray.Create(source)
                : ImmutableArray<GeneratedSourceOutput>.Empty
            )
            .WithTrackingName("LiftSourceOutput");

        context.RegisterSourceOutput(liftSources, static (sourceProductionContext, output) =>
        {
            sourceProductionContext.AddSource(
                hintName: output.HintName,
                sourceText: SourceText.From(output.Source, Encoding.UTF8)
            );
        });

        var liftDiagnostics = liftTargets
            .SelectMany(static (output, _) => output.Diagnostics)
            .WithTrackingName("LiftDiagnostics");

        context.RegisterSourceOutput(liftDiagnostics, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
    }
}
