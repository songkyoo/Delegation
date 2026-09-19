using Macaron.Delegation;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.MemberExposure;

internal static class ExposeTargetAnalyzer
{
    public static TargetGenerationOutput Analyze(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken
    )
    {
        var results = ExposeContextFactory.CreateAll(attributeContext, cancellationToken);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var lines = ImmutableArray.CreateBuilder<string>();

        foreach (var (generationContext, contextDiagnostics) in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            diagnostics.AddRange(contextDiagnostics);

            if (generationContext != null)
            {
                TargetGenerationComposer.AppendGeneration(
                    lines,
                    generationContext.DelegationTypeSymbol,
                    ExposeGenerationPipeline.Generate(generationContext)
                );
            }
        }

        return TargetGenerationComposer.CreateOutput(
            targetSymbol: attributeContext.TargetSymbol,
            outputKind: GenerationOutputKind.Expose,
            lines,
            diagnostics: diagnostics.ToImmutable()
        );
    }
}
