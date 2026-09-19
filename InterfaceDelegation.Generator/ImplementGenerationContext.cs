using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal sealed record ImplementGenerationContext(
    AttributeData Attribute,
    ISymbol DeclaredSymbol,
    ITypeSymbol DelegationTypeSymbol,
    ImplementationMode Mode
);
