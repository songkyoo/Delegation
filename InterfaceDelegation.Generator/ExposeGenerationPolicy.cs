using Microsoft.CodeAnalysis;

using static Macaron.InterfaceDelegation.DelegationMemberGenerationDecision;
using static Macaron.InterfaceDelegation.ImplementationMode;
using static Macaron.InterfaceDelegation.MethodReturnTypeComparison;
using static Microsoft.CodeAnalysis.Accessibility;

namespace Macaron.InterfaceDelegation;

internal static class ExposeGenerationPolicy
{
    public static DelegationDispatch CreateDispatch(ExposeGenerationContext context)
    {
        var declaredSymbol = context.DeclaredSymbol;

        if (!RequiresInterfaceDispatch(context))
        {
            return new DirectDelegationDispatch(declaredSymbol.Name);
        }

        return declaredSymbol is IFieldSymbol
            ? new ConstrainedFieldDelegationDispatch(declaredSymbol.Name, context.DelegationTypeSymbol)
            : new InterfaceCastDelegationDispatch(declaredSymbol.Name, context.DelegationTypeSymbol);
    }

    public static bool RequiresInterfaceDispatch(ExposeGenerationContext context)
    {
        var targetTypeSymbol = DelegationTargetSymbol.GetDeclaredType(context.DeclaredSymbol);

        return
            !SymbolEqualityComparer.Default.Equals(targetTypeSymbol, context.DelegationTypeSymbol)
            && MemberComparisonHelper.ImplementsInterface(targetTypeSymbol, context.DelegationTypeSymbol);
    }

    public static IEnumerable<ISymbol> GetTargetMembers(ExposeGenerationContext context)
    {
        foreach (var symbol in DelegationMemberProvider.GetMembersIncludingBaseTypes(context.DelegationTypeSymbol))
        {
            if (ExposeMemberRules.IsSupportedInterfaceMember(symbol))
            {
                yield return symbol;
            }
        }
    }

    public static DelegationMemberGenerationContext? CreateMemberGenerationContext(
        ExposeGenerationContext context,
        ISymbol symbol,
        MemberImplementationIndex implementationIndex
    )
    {
        var typeSymbol = context.DeclaredSymbol.ContainingType;
        var symbolName = symbol.Name;
        var implicitMember = implementationIndex.FindImplicit(symbol, symbolName, Match);
        var explicitMember = implementationIndex.FindExplicit(symbol, symbolName, Match);
        var mode = symbolName == typeSymbol.Name ? Explicit : context.Mode;
        var decision = mode switch
        {
            Explicit => explicitMember == null ? GenerateExplicitInterfaceImplementation : Skip,
            _ => (implicitMember, explicitMember) switch
            {
                (null, null) => Generate,
                ({ IsAbstract: true }, null) when !SymbolEqualityComparer.Default.Equals(
                    implicitMember.ContainingType, typeSymbol
                ) => OverrideAbstractMember,
                _ => Skip,
            },
        };

        if (decision == Skip)
        {
            return null;
        }

        DelegationMemberDeclaration declaration = decision switch
        {
            Generate => new ImplicitDelegationMemberDeclaration(Accessibility: Public),
            GenerateExplicitInterfaceImplementation => new ExplicitInterfaceDelegationMemberDeclaration(
                context.DelegationTypeSymbol
            ),
            OverrideAbstractMember => new OverrideDelegationMemberDeclaration(Accessibility: Public),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
        };

        return new DelegationMemberGenerationContext(
            Symbol: symbol,
            SymbolName: symbolName,
            Declaration: declaration
        );
    }
}
