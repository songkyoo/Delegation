using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal sealed record TargetGenerationOutput(
    GeneratedSourceOutput? Source,
    ImmutableArray<Diagnostic> Diagnostics
);
