using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.MemberExposure;

internal sealed record ExposeGenerationContext(
    ISymbol DeclaredSymbol,
    ITypeSymbol DelegationTypeSymbol,
    bool IncludeBaseTypes,
    ImmutableHashSet<string> Filter,
    ImmutableHashSet<string> Remove,
    ImmutableDictionary<string, string> Rename,
    ImmutableArray<ISymbol> PrecomputedTargetMembers
);
