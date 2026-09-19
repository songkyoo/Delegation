using Macaron.Delegation;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using static Macaron.Delegation.GenerationOutputKind;

namespace Macaron.InterfaceDelegation;

internal static class ImplementTargetAnalyzer
{
    private readonly record struct ImplementApplication(
        ISymbol DeclaredSymbol,
        AttributeData Attribute,
        int SyntaxTreeIndex,
        int SpanStart
    );

    private readonly record struct ImplementApplicationKey(SyntaxTree SyntaxTree, TextSpan Span);

    private readonly record struct ImplementAnalysisEntry(
        ImplementGenerationContext? Context,
        ImmutableArray<Diagnostic> Diagnostics,
        bool IsCanonical
    );

    private sealed class ImplementTypeAnalysis(ImmutableDictionary<ImplementApplicationKey, ImplementAnalysisEntry> entries)
    {
        public bool TryGetEntry(AttributeData attribute, out ImplementAnalysisEntry entry)
        {
            if (attribute.ApplicationSyntaxReference is not { } syntaxReference)
            {
                entry = default;
                return false;
            }

            return entries.TryGetValue(
                new ImplementApplicationKey(syntaxReference.SyntaxTree, syntaxReference.Span),
                out entry
            );
        }
    }

    private sealed class ImplementAnalysisCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<ISymbol, ImplementTypeAnalysis> _typeAnalyses = new(SymbolEqualityComparer.Default);

        public ImplementTypeAnalysis GetOrCreate(
            INamedTypeSymbol typeSymbol,
            Compilation compilation,
            INamedTypeSymbol implementAttributeSymbol,
            CancellationToken cancellationToken
        )
        {
            lock (_gate)
            {
                if (_typeAnalyses.TryGetValue(typeSymbol, out var analysis))
                {
                    return analysis;
                }
            }

            var created = CreateTypeAnalysis(
                typeSymbol,
                compilation,
                implementAttributeSymbol,
                cancellationToken
            );

            lock (_gate)
            {
                if (_typeAnalyses.TryGetValue(typeSymbol, out var analysis))
                {
                    return analysis;
                }

                _typeAnalyses.Add(typeSymbol, created);

                return created;
            }
        }
    }

    private static readonly ConditionalWeakTable<Compilation, ImplementAnalysisCache> AnalysisCaches = new();

    public static TargetGenerationOutput Analyze(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var lines = ImmutableArray.CreateBuilder<string>();
        var implementAttributeSymbol = attributeContext.Attributes.IsEmpty
            ? null
            : attributeContext.Attributes[0].AttributeClass;

        if (implementAttributeSymbol == null)
        {
            return new TargetGenerationOutput(null, ImmutableArray<Diagnostic>.Empty);
        }

        var compilation = attributeContext.SemanticModel.Compilation;
        var analysisCache = AnalysisCaches.GetValue(
            key: compilation,
            createValueCallback: static _ => new ImplementAnalysisCache()
        );
        var typeAnalysis = analysisCache.GetOrCreate(
            typeSymbol: attributeContext.TargetSymbol.ContainingType!,
            compilation,
            implementAttributeSymbol,
            cancellationToken
        );

        foreach (var attribute in attributeContext.Attributes.OrderBy(GetAttributeSpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!typeAnalysis.TryGetEntry(attribute, out var entry))
            {
                continue;
            }

            diagnostics.AddRange(entry.Diagnostics);

            if (entry.Context is not { } implementContext)
            {
                continue;
            }

            if (!entry.IsCanonical)
            {
                diagnostics.Add(Diagnostic.Create(
                    descriptor: ImplementDiagnostics.DuplicateDelegationTargetRule,
                    location: implementContext.Attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                    messageArgs: [implementContext.DelegationTypeSymbol]
                ));

                continue;
            }

            TargetGenerationComposer.AppendGeneration(
                lines,
                implementContext.DelegationTypeSymbol,
                ImplementGenerationPipeline.Generate(implementContext)
            );
        }

        return TargetGenerationComposer.CreateOutput(
            attributeContext.TargetSymbol,
            outputKind: Implement,
            lines,
            diagnostics.ToImmutable()
        );
    }

    private static ImplementTypeAnalysis CreateTypeAnalysis(
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        INamedTypeSymbol implementAttributeSymbol,
        CancellationToken cancellationToken
    )
    {
        var entries = ImmutableDictionary.CreateBuilder<ImplementApplicationKey, ImplementAnalysisEntry>();
        var delegatedInterfaces = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var application in GetApplications(typeSymbol, implementAttributeSymbol, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (generationContext, diagnostics) = ImplementContextFactory.Create(
                application.Attribute,
                application.DeclaredSymbol,
                compilation,
                cancellationToken
            );
            var isCanonical = generationContext == null ||
                delegatedInterfaces.Add(generationContext.DelegationTypeSymbol);
            var syntaxReference = application.Attribute.ApplicationSyntaxReference!;

            entries.Add(
                new ImplementApplicationKey(syntaxReference.SyntaxTree, syntaxReference.Span),
                new ImplementAnalysisEntry(generationContext, diagnostics, isCanonical)
            );
        }

        return new ImplementTypeAnalysis(entries.ToImmutable());
    }

    private static ImmutableArray<ImplementApplication> GetApplications(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol implementAttributeSymbol,
        CancellationToken cancellationToken
    )
    {
        var treeIndexes = new Dictionary<SyntaxTree, int>();
        var syntaxTreeIndex = 0;

        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!treeIndexes.ContainsKey(syntaxReference.SyntaxTree))
            {
                treeIndexes[syntaxReference.SyntaxTree] = syntaxTreeIndex++;
            }
        }

        var applications = ImmutableArray.CreateBuilder<ImplementApplication>();

        foreach (var declaredSymbol in GetSupportedTargets(typeSymbol))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var attribute in declaredSymbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, implementAttributeSymbol)
                    || attribute.ApplicationSyntaxReference is not { } syntaxReference
                )
                {
                    continue;
                }

                applications.Add(new ImplementApplication(
                    DeclaredSymbol: declaredSymbol,
                    Attribute: attribute,
                    SyntaxTreeIndex: treeIndexes.TryGetValue(syntaxReference.SyntaxTree, out var index)
                        ? index
                        : int.MaxValue,
                    SpanStart: syntaxReference.Span.Start
                ));
            }
        }

        return applications
            .OrderBy(static application => application.SyntaxTreeIndex)
            .ThenBy(static application => application.SpanStart)
            .ToImmutableArray();
    }

    private static IEnumerable<ISymbol> GetSupportedTargets(INamedTypeSymbol typeSymbol)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var memberSymbol in typeSymbol.GetMembers())
        {
            if (DelegationTargetSymbol.IsSupported(memberSymbol) && seen.Add(memberSymbol))
            {
                yield return memberSymbol;
            }
        }

        foreach (var constructorSymbol in typeSymbol.InstanceConstructors)
        {
            foreach (var parameterSymbol in constructorSymbol.Parameters)
            {
                if (DelegationTargetSymbol.IsSupported(parameterSymbol) && seen.Add(parameterSymbol))
                {
                    yield return parameterSymbol;
                }
            }
        }
    }

    private static int GetAttributeSpanStart(AttributeData attributeData)
    {
        return attributeData.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue;
    }
}
