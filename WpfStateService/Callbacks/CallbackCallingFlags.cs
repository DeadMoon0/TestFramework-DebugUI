namespace WpfStateService.Callbacks;

[Flags]
public enum CallbackCallingFlags : ushort
{
    None = 0,
    CalledForChild /* */ = 0b0000_0001,
    IsNowNull /*      */ = 0b0000_0010,
    IsEqual /*        */ = 0b0000_0100,
    CalledForParent /**/ = 0b0000_1000,
}