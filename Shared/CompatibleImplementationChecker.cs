using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal sealed class CompatibleImplementationChecker(
    MemberImplementationIndex implementationIndex,
    HashSet<ISymbol> explicitImplementations
)
{
    public bool HasImplementation(ISymbol interfaceMemberSymbol)
    {
        return implementationIndex.FindImplicit(
                interfaceMemberSymbol,
                interfaceMemberSymbol.Name,
                MethodReturnTypeComparison.Match
            ) != null
            || explicitImplementations.Contains(interfaceMemberSymbol);
    }
}
