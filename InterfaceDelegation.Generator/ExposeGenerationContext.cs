using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal sealed record ExposeGenerationContext(
    AttributeData Attribute,
    ISymbol DeclaredSymbol,
    ITypeSymbol DelegationTypeSymbol,
    ImplementationMode Mode
);
