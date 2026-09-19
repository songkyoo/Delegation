namespace Macaron.Delegation;

internal enum DelegationMemberGenerationDecision
{
    Generate,
    GenerateExplicitInterfaceImplementation,
    OverrideAbstractMember,
    Skip,
}
