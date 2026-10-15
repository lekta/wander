namespace Wander.Core.FileSystem;

/// <summary>
/// What a phone or a camera says when it stops answering (OPERATIONS,
/// "Ошибки устройств"), told apart so the status line can say what to do.
/// The codes are Windows Portable Devices' own (facility 0x2A); which of
/// them reach Wander through the shell is the stand's to say (PLAN Y4a), and
/// the list grows with what it finds.
/// </summary>
public static class DeviceErrors {
    private const int DeviceNotOpen = unchecked((int)0x802A0002);   // E_WPD_DEVICE_NOT_OPEN
    private const int DeviceIsHung = unchecked((int)0x802A0006);    // E_WPD_DEVICE_IS_HUNG


    /// <summary>The device stopped answering - a camera switched itself off, or went to sleep.</summary>
    public static bool IsAsleep(Exception ex) {
        return ex.HResult is DeviceNotOpen or DeviceIsHung;
    }
}
