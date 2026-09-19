using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal readonly record struct GenerationAnalysisResult<TContext>(
    TContext? Context,
    ImmutableArray<Diagnostic> Diagnostics
) where TContext : class;
