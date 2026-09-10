using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Whisperkey;

/// Speaks state changes to screen readers.
///
/// The pill is a picture. Someone using Narrator gets nothing from it, so
/// "listening" and "inserted" have to be said out loud. UiaRaiseNotificationEvent
/// is the supported way to do that without pretending to be a real control, but it
/// still needs a provider object - so this is a small COM server, which .NET can
/// generate for NativeAOT via GeneratedComClass.
[GeneratedComInterface, Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
partial interface IRawElementProviderSimple {
    [PreserveSig] int get_ProviderOptions(out int options);
    [PreserveSig] int GetPatternProvider(int patternId, out nint provider);
    [PreserveSig] int GetPropertyValue(int propertyId, out Variant value);
    [PreserveSig] int get_HostRawElementProvider(out nint provider);
}

[GeneratedComClass]
sealed partial class HwndProvider(nint hwnd) : IRawElementProviderSimple {
    const int ProviderOptions_ServerSideProvider = 1;

    [DllImport("uiautomationcore.dll")] static extern int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    public int get_ProviderOptions(out int options) { options = ProviderOptions_ServerSideProvider; return 0; }
    public int GetPatternProvider(int patternId, out nint provider) { provider = 0; return 0; }
    public int GetPropertyValue(int propertyId, out Variant value) { value = default; return 0; }
    public int get_HostRawElementProvider(out nint provider) => UiaHostProviderFromHwnd(hwnd, out provider);
}

static unsafe class Announce {
    // NotificationKind_ActionCompleted, NotificationProcessing_MostRecent:
    // "this just happened", and a newer message supersedes an older one rather
    // than queueing up behind it.
    const int ActionCompleted = 3, MostRecent = 2;

    // Raw pointers rather than the interface and [MarshalAs(BStr)]: letting the
    // runtime marshal COM here trips IL2050, which means trimming could silently
    // break the announcement. Marshalling by hand keeps the build at zero warnings.
    [DllImport("uiautomationcore.dll")]
    static extern int UiaRaiseNotificationEvent(nint provider, int kind, int processing, nint display, nint activityId);

    [DllImport("uiautomationcore.dll")] static extern int UiaClientsAreListening();
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] static extern nint SysAllocString(string s);
    [DllImport("oleaut32.dll")] static extern void SysFreeString(nint bstr);

    static HwndProvider _provider;
    static nint _providerPtr;

    public static void Attach(nint hwnd) {
        _provider = new HwndProvider(hwnd);
        _providerPtr = (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(_provider);
    }

    public static void Say(string what) {
        // Costs nothing when no assistive technology is running.
        if (_providerPtr == 0 || UiaClientsAreListening() == 0) return;
        nint display = 0, activity = 0;
        try {
            display = SysAllocString(what);
            activity = SysAllocString("Whisperkey");
            UiaRaiseNotificationEvent(_providerPtr, ActionCompleted, MostRecent, display, activity);
        } catch { }
        finally { if (display != 0) SysFreeString(display); if (activity != 0) SysFreeString(activity); }
    }
}
