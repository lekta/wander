namespace Wander.Core.FileSystem;

/// <summary>
/// What a phone or a camera says when it stops answering (OPERATIONS,
/// "Ошибки устройств"), told apart so the status line can say what to do
/// and an import can wait for the device instead of failing (PLAN Y4d).
/// The codes are Windows Portable Devices' own (facility 0x2A) and Win32's;
/// which of them arrive was the stand's to say (Y4a).
/// </summary>
public static class DeviceErrors {
    private const int DeviceNotOpen = unchecked((int)0x802A0002);   // E_WPD_DEVICE_NOT_OPEN
    private const int DeviceIsHung = unchecked((int)0x802A0006);    // E_WPD_DEVICE_IS_HUNG


    /// <summary>The device stopped answering - a camera switched itself off, or went to sleep.</summary>
    public static bool IsAsleep(Exception ex) {
        return ex.HResult is DeviceNotOpen or DeviceIsHung;
    }

    /// <summary>
    /// The device went away under a read: the cable pulled and the camera
    /// switched off say the same (stand Y4a: 0x8007001F, with the removal
    /// notice in the same tenth of a second), and a device that is no
    /// longer there to open says the rest.
    /// </summary>
    public static bool IsGone(Exception ex) {
        if (IsAsleep(ex)) {
            return true;
        }

        return TransientIo.Win32Code(ex) is 31      // ERROR_GEN_FAILURE
            or 21                                   // ERROR_NOT_READY
            or 1167;                                // ERROR_DEVICE_NOT_CONNECTED
    }
}
