namespace Wander.Core.FileSystem;

/// <summary>
/// Which input/output errors are worth a second try: the ones a card, a
/// cable or a network gives for a moment and then stops giving - the
/// device not ready yet, a bad read the next read gets past, a share that
/// dropped the connection. Told by the Win32 code in the low word of the
/// exception's HRESULT (0x8007xxxx), the way <see cref="FileInUse"/> tells
/// a held file.
///
/// <para>
/// Not to be confused with <see cref="TransientFiles.IsTransient(string)"/>,
/// which is about a path (a temporary file), not about an error. A held file
/// is not here either: waiting for it is <see cref="BusyGate"/>'s, with its
/// own budget and a holder to name.
/// </para>
/// </summary>
public static class TransientIo {
    // Win32 error codes.
    private const int NotReady = 21;                // ERROR_NOT_READY
    private const int Crc = 23;                     // ERROR_CRC
    private const int GenFailure = 31;              // ERROR_GEN_FAILURE
    private const int UnexpectedNetError = 59;      // ERROR_UNEXP_NET_ERR
    private const int NetNameDeleted = 64;          // ERROR_NETNAME_DELETED
    private const int SemaphoreTimeout = 121;       // ERROR_SEM_TIMEOUT
    private const int IoDevice = 1117;              // ERROR_IO_DEVICE
    private const int DeviceNotConnected = 1167;    // ERROR_DEVICE_NOT_CONNECTED
    private const int NetworkUnreachable = 1231;    // ERROR_NETWORK_UNREACHABLE

    /// <summary>HRESULT_FROM_WIN32: the facility and severity bits of a Win32 error.</summary>
    private const int Win32Facility = unchecked((int)0x80070000);


    /// <summary>The error may well not happen again: the item is worth another try. A copy that did not read back is one.</summary>
    public static bool IsTransient(Exception ex) {
        return ex is CopyMismatchException || Win32Code(ex) is NotReady or Crc or GenFailure or UnexpectedNetError or NetNameDeleted
            or SemaphoreTimeout or IoDevice or DeviceNotConnected or NetworkUnreachable;
    }

    /// <summary>The Win32 error code behind <paramref name="ex"/>, or 0 when it carries none.</summary>
    public static int Win32Code(Exception ex) {
        if (ex is OperationCanceledException) {
            return 0;
        }

        return (ex.HResult & unchecked((int)0xFFFF0000)) == Win32Facility ? ex.HResult & 0xFFFF : 0;
    }
}


/// <summary>
/// How often and how patiently an item is tried again after a transient
/// error: three tries in all by default, a second and then three before the
/// second and the third - long enough for a card reader to come back, short
/// enough not to look hung.
/// </summary>
/// <param name="Attempts">Tries in all, the first included.</param>
/// <param name="Delays">The pause before each further try; the last one repeats.</param>
public sealed record RetryPolicy(int Attempts, IReadOnlyList<TimeSpan> Delays) {
    public static readonly RetryPolicy Default = new(
        3, new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) });


    /// <summary>The pause before try number <paramref name="attempt"/> (2 and up).</summary>
    public TimeSpan DelayBefore(int attempt) {
        if (Delays.Count == 0) {
            return TimeSpan.Zero;
        }

        return Delays[Math.Clamp(attempt - 2, 0, Delays.Count - 1)];
    }
}


/// <summary>Runs one piece of input/output again while it fails transiently (<see cref="TransientIo"/>).</summary>
public static class IoRetry {
    /// <summary>
    /// <paramref name="action"/>, tried up to <see cref="RetryPolicy.Attempts"/>
    /// times while it throws a transient error; any other error goes out at
    /// once, and so does the last transient one. The pause between tries
    /// ends early when <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="onRetry">Told the number of the try about to be made (2, 3...) and what failed, before the pause.</param>
    /// <returns>How many tries it took.</returns>
    /// <exception cref="OperationCanceledException">Cancelled during a pause.</exception>
    public static int Run(Action action, RetryPolicy policy, CancellationToken ct, Action<int, Exception>? onRetry = null) {
        for (int attempt = 1; ; attempt++) {
            try {
                action();

                return attempt;
            } catch (Exception ex) when (attempt < policy.Attempts && TransientIo.IsTransient(ex) && !ct.IsCancellationRequested) {
                onRetry?.Invoke(attempt + 1, ex);
                if (ct.WaitHandle.WaitOne(policy.DelayBefore(attempt + 1))) {
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
    }
}
