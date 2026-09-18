using System.Collections.Immutable;

namespace Macaron.InterfaceDelegation;

internal static class ExposeGenerationPipeline
{
    public static ImmutableArray<string> Generate(ExposeGenerationContext context)
    {
        var executionContext = DelegationGenerationContext.Create(
            context.DeclaredSymbol,
            context.DelegationTypeSymbol,
            ExposeGenerationPolicy.CreateDispatch(context)
        );
        var dispatchRenderer = DelegationDispatchRenderer.Create(executionContext.Dispatch);
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var symbol in ExposeGenerationPolicy.GetTargetMembers(context))
        {
            var memberContext = ExposeGenerationPolicy.CreateMemberGenerationContext(
                context,
                symbol,
                executionContext.ImplementationIndex
            );

            if (memberContext == null)
            {
                continue;
            }

            ExposeRenderingPolicy.RenderMember(
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
