using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal readonly record struct GenerationAnalysisResult<TContext>(
    TContext? Context,
    ImmutableArray<Diagnostic> Diagnostics
) where TContext : class;
