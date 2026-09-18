using System.Collections.Immutable;

namespace Macaron.InterfaceDelegation;

internal static class LiftGenerationPipeline
{
    public static ImmutableArray<string> Generate(LiftGenerationContext context)
    {
        var executionContext = DelegationGenerationContext.Create(
            context.DeclaredSymbol,
            context.DelegationTypeSymbol,
            new DirectDelegationDispatch(context.DeclaredSymbol.Name)
        );
        var dispatchRenderer = DelegationDispatchRenderer.Create(executionContext.Dispatch);
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var symbol in LiftGenerationPolicy.GetTargetMembers(context))
        {
            var memberContext = LiftGenerationPolicy.CreateMemberGenerationContext(
                context,
                symbol,
                executionContext.ImplementationIndex
            );

            if (memberContext == null)
            {
                continue;
            }

            LiftRenderingPolicy.RenderMember(
                context: new DelegationRenderingContext(
                    MemberContext: memberContext.Value,
                    DispatchRenderer: dispatchRenderer
                ),
                builder
            );
        }

        return builder.ToImmutable();
    }
}
