using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal static class ImplementDiagnostics
{
    public static readonly DiagnosticDescriptor ContainingTypeMustBePartialRule = new(
        id: "MAID0001",
        title: "Implement requires a partial containing type",
        messageFormat: "Type '{0}' must be declared partial to use Implement",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidImplementationTargetRule = new(
        id: "MAID0002",
        title: "Implement attribute cannot determine an interface type",
        messageFormat: "Cannot determine an interface for the Implement attribute from '{0}'. Specify a non-generic or constructed generic interface, or omit the type only when the target has exactly one directly implemented interface.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ValueTypePropertyCannotBeDelegatedRule = new(
        id: "MAID0003",
        title: "Value type property cannot be delegated",
        messageFormat: "Property '{0}' is of a value type and cannot be delegated using Implement",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateDelegationTargetRule = new(
        id: "MAID0004",
        title: "Duplicate Implement target",
        messageFormat: "The interface '{0}' is delegated more than once in the same type",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ImplementMemberNotImplementedRule = new(
        id: "MAID0005",
        title: "Implement target does not implement an interface member",
        messageFormat: "The target type '{0}' does not implement interface member '{1}' required by Implement",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
