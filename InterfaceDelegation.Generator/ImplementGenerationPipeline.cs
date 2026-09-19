using Macaron.Delegation;
using System.Collections.Immutable;

namespace Macaron.InterfaceDelegation;

internal static class ImplementGenerationPipeline
{
    public static ImmutableArray<string> Generate(ImplementGenerationContext context)
    {
        var executionContext = DelegationGenerationContext.Create(
            context.DeclaredSymbol,
            context.DelegationTypeSymbol,
            ImplementGenerationPolicy.CreateDispatch(context)
        );
        var dispatchRenderer = DelegationDispatchRenderer.Create(executionContext.Dispatch);
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var symbol in ImplementGenerationPolicy.GetTargetMembers(context))
        {
            var memberContext = ImplementGenerationPolicy.CreateMemberGenerationContext(
                context,
                symbol,
                executionContext.ImplementationIndex
            );

            if (memberContext == null)
            {
                continue;
            }

            ImplementRenderingPolicy.RenderMember(
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
