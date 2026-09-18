using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal sealed record LiftGenerationContext(
    AttributeData Attribute,
    ISymbol DeclaredSymbol,
    ITypeSymbol DelegationTypeSymbol,
    bool IncludeBaseTypes,
    ImmutableHashSet<string> Filter,
    ImmutableHashSet<string> Remove,
    ImmutableDictionary<string, string> Rename,
    ImmutableArray<ISymbol> PrecomputedTargetMembers
);
