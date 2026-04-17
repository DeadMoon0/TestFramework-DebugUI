namespace WpfStateService.Callbacks;

[Flags]
public enum CallbackFlags : ushort
{
    None /*          */ = 0b0000_0000,
    OnChildChange /* */ = 0b0000_0001,
    OnNotNull /*     */ = 0b0000_0010,
    OnValueDiffers /**/ = 0b0000_0100,
}