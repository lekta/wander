using System.Runtime.InteropServices;
using Wander.Core.Logging;
using Wander.Core.Operations;

namespace Wander.Platform.Windows.Power;

/// <summary>
/// <see cref="IPowerGuard"/> over the system's power requests: a request
/// with a reason, set to "system required" for as long as it is held. Not
/// "display required" - the screen goes dark as usual, the copy goes on.
/// <c>powercfg /requests</c> lists the request with its reason while it is
/// held, which is how to see it at work.
///
/// <para>
/// A request that cannot be made is logged and the operation runs without
/// it: a computer that might sleep is no reason to refuse a copy.
/// </para>
/// </summary>
public sealed class PowerRequestGuard : IPowerGuard {
    private readonly ILogger _log;


    public PowerRequestGuard(ILogger log) {
        _log = log;
    }


    public IDisposable KeepAwake(string reason) {
        var context = new REASON_CONTEXT {
            Version = POWER_REQUEST_CONTEXT_VERSION,
            Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = reason,
        };
        IntPtr handle = PowerCreateRequest(ref context);
        if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero) {
            _log.Warn($"Power request not created (error {Marshal.GetLastWin32Error()}): the computer may sleep during the operation");

            return Released.Instance;
        }

        if (!PowerSetRequest(handle, PowerRequestSystemRequired)) {
            _log.Warn($"Power request not set (error {Marshal.GetLastWin32Error()}): the computer may sleep during the operation");
            CloseHandle(handle);

            return Released.Instance;
        }

        _log.Info("Power request set: the computer stays awake while operations run");

        return new Request(handle, _log);
    }


    /// <summary>A held request; disposing clears and closes it, once.</summary>
    private sealed class Request : IDisposable {
        private readonly ILogger _log;
        private IntPtr _handle;


        public Request(IntPtr handle, ILogger log) {
            _handle = handle;
            _log = log;
        }


        public void Dispose() {
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle == IntPtr.Zero) {
                return;
            }

            PowerClearRequest(handle, PowerRequestSystemRequired);
            CloseHandle(handle);
            _log.Info("Power request cleared: the computer may sleep again");
        }
    }

    /// <summary>What a request that could not be made hands back.</summary>
    private sealed class Released : IDisposable {
        public static readonly Released Instance = new();


        public void Dispose() {
        }
    }


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    private const int PowerRequestSystemRequired = 1;
    private const nint INVALID_HANDLE_VALUE = -1;

    /// <summary>
    /// The simple-string shape of REASON_CONTEXT: the union's other arm is
    /// for a localised resource, and with SIMPLE_STRING set only this
    /// pointer is read. The system copies the string.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct REASON_CONTEXT {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
