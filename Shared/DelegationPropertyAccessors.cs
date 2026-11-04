namespace Macaron.Delegation;

[Flags]
internal enum DelegationPropertyAccessors
{
    None = 0x00,
    Get = 0x01,
    Set = 0x02,
    All = Get | Set,
}
