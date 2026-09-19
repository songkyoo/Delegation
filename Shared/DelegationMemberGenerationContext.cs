using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal readonly record struct DelegationMemberGenerationContext(
    ISymbol Symbol,
    string SymbolName,
    DelegationMemberDeclaration Declaration
);
