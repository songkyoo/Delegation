using Macaron.Delegation;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Macaron.InterfaceDelegation;

[Generator]
public class ImplementGenerator : IIncrementalGenerator
{
    private const string ImplementAttributeMetadataName = "Macaron.InterfaceDelegation.ImplementAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var implementTargets = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: ImplementAttributeMetadataName,
                predicate: static (syntaxNode, _) => DelegationTargetSyntax.IsSupported(syntaxNode),
                transform: static (attributeContext, cancellationToken) => ImplementTargetAnalyzer.Analyze(
                    attributeContext,
                    cancellationToken
                )
            )
            .WithTrackingName("ImplementAnalysisOutput");

        var implementSources = implementTargets
            .SelectMany(static (output, _) => output.Source is { } source
                ? ImmutableArray.Create(source)
                : ImmutableArray<GeneratedSourceOutput>.Empty
            )
            .WithTrackingName("ImplementSourceOutput");

        context.RegisterSourceOutput(implementSources, static (sourceProductionContext, output) =>
        {
            sourceProductionContext.AddSource(
                hintName: output.HintName,
                sourceText: SourceText.From(output.Source, Encoding.UTF8)
            );
        });

        var implementDiagnostics = implementTargets
            .SelectMany(static (output, _) => output.Diagnostics)
            .WithTrackingName("ImplementDiagnostics");

        context.RegisterSourceOutput(implementDiagnostics, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
    }
}
