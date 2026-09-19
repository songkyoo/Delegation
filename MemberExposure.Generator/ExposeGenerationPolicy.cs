using Macaron.Delegation;
using Microsoft.CodeAnalysis;

using static Macaron.Delegation.DelegationMemberGenerationDecision;
using static Macaron.Delegation.MethodReturnTypeComparison;
using static Microsoft.CodeAnalysis.Accessibility;

namespace Macaron.MemberExposure;

internal static class ExposeGenerationPolicy
{
    public static IEnumerable<ISymbol> GetTargetMembers(ExposeGenerationContext context)
    {
        var symbols = !context.PrecomputedTargetMembers.IsDefault
            ? context.PrecomputedTargetMembers
            : context.IncludeBaseTypes
            ? DelegationMemberProvider.GetMembersIncludingBaseTypes(context.DelegationTypeSymbol)
            : DelegationMemberProvider.GetDeclaredMembers(context.DelegationTypeSymbol);

        foreach (var symbol in symbols)
        {
            if (!ShouldIncludeSymbol(context, symbol))
            {
                continue;
            }

            yield return symbol;
        }
    }

    public static DelegationMemberGenerationContext? CreateMemberGenerationContext(
        ExposeGenerationContext context,
        ISymbol symbol,
        MemberImplementationIndex implementationIndex
    )
    {
        var typeSymbol = context.DeclaredSymbol.ContainingType;
        var symbolName = context.Rename.TryGetValue(symbol.Name, out var renamed)
            ? renamed
            : symbol.Name;
        var implicitMember = implementationIndex.FindImplicit(symbol, symbolName, Ignore);
        var decision = implicitMember switch
        {
            null => Generate,
            { IsAbstract: true } when !SymbolEqualityComparer.Default.Equals(
                implicitMember.ContainingType, typeSymbol
            ) => OverrideAbstractMember,
            _ => Skip,
        };

        if (decision == Skip)
        {
            return null;
        }

        DelegationMemberDeclaration declaration = decision switch
        {
            Generate => new ImplicitDelegationMemberDeclaration(symbol.DeclaredAccessibility),
            OverrideAbstractMember => new OverrideDelegationMemberDeclaration(symbol.DeclaredAccessibility),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
        };

        return new DelegationMemberGenerationContext(
            Symbol: symbol,
            SymbolName: symbolName,
            Declaration: declaration
        );
    }

    private static bool ShouldIncludeSymbol(ExposeGenerationContext context, ISymbol symbol)
    {
        if (symbol.DeclaredAccessibility is not Public and not Internal)
        {
            return false;
        }

        if (!context.Filter.IsEmpty && !context.Filter.Contains(symbol.Name))
        {
            return false;
        }

        return !context.Remove.Contains(symbol.Name);
    }
}
