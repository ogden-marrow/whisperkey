using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Whisperkey;

// Hand-written UI Automation COM, because NativeAOT cannot use classic COM interop.
//
// The placeholder slots below are load-bearing. A wrong count silently calls a
// different function pointer and hangs rather than failing - see ADR 0002.

[GeneratedComInterface, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
partial interface IUIAutomation {
    void _0(); void _1();                                  // CompareElements, CompareRuntimeIds
    [PreserveSig] int GetRootElement(out IUIAutomationElement e);
    [PreserveSig] int ElementFromHandle(nint hwnd, out IUIAutomationElement e);
    [PreserveSig] int ElementFromPoint(long pt, out IUIAutomationElement e);
    [PreserveSig] int GetFocusedElement(out IUIAutomationElement e);
}

[GeneratedComInterface, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
partial interface IUIAutomationElement {
    void _0(); void _1(); void _2(); void _3();            // SetFocus, GetRuntimeId, FindFirst, FindAll
    void _4(); void _5(); void _6();                       // FindFirstBuildCache, FindAllBuildCache, BuildUpdatedCache
    [PreserveSig] int GetCurrentPropertyValue(int propertyId, out Variant value);
}

[StructLayout(LayoutKind.Sequential)]
struct Variant { public ushort vt; public ushort r1, r2, r3; public nint data; public nint data2; }

static class UiaProp {
    public const int ProcessId = 30002, ControlType = 30003, BoundingRectangle = 30001,
                     HasKeyboardFocus = 30008, IsKeyboardFocusable = 30009,
                     IsPassword = 30019, IsValuePatternAvailable = 30043,
                     IsTextPatternAvailable = 30040, ValueIsReadOnly = 30046;
}

static class UiaCtrl {
    public const int ComboBox = 50003, Edit = 50004, Document = 50030;
}

static class Uia {
    public const int VT_BOOL = 11, VT_I4 = 3, VT_R8_ARRAY = 0x2005;

    [DllImport("ole32.dll")] public static extern int CoInitializeEx(nint p, int flags);
    [DllImport("ole32.dll")] public static extern int CoCreateInstance(in Guid clsid, nint outer, int ctx, in Guid iid, out nint ppv);
    [DllImport("oleaut32.dll")] public static extern int VariantClear(ref Variant v);

    public const int COINIT_MULTITHREADED = 0;   // an STA here deadlocks cross-process UIA calls
    public static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    public static bool Flag(IUIAutomationElement el, int prop) {
        if (el.GetCurrentPropertyValue(prop, out var v) < 0) return false;
        bool b = v.vt == VT_BOOL && v.data != 0;
        VariantClear(ref v);
        return b;
    }

    public static int Int(IUIAutomationElement el, int prop) {
        if (el.GetCurrentPropertyValue(prop, out var v) < 0) return 0;
        int i = v.vt == VT_I4 ? (int)v.data : 0;
        VariantClear(ref v);
        return i;
    }
}
