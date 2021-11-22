/* 
 * InputSystem device for Apple Pencil.
 * IMPORTANT: You must check "allow unsafe code" in player settings to get the `unsafe` blocks to compile.
 */ 

using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.InputSystem.Utilities;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.InputSystem.LowLevel
{

/// <summary>InputSystem state for Apple Pencil.  Size must be a power of two.  Size and layout must match exactly with
/// the corresponding ApplePencilState struct in the native bridge code.</summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct ApplePencilState : IInputStateTypeInfo {
    public static FourCC kFormat => new FourCC('P', 'N', 'C', 'L');

    [InputControl(usage = "Point", dontReset = true)]
    [FieldOffset(0)]
    public Vector2 position;

    [InputControl(layout = "Analog", usage = "Pressure", defaultState = 0.0f)]
    [FieldOffset(8)]
    public float pressure;
    
    [InputControl(layout = "Vector2", displayName = "Tilt", usage = "Tilt")]
    [FieldOffset(12)]
    public Vector2 tilt;

    [InputControl(name = "tip", displayName = "Tip", layout = "Button", bit = (int) PenButton.Tip, usage = "PrimaryAction")]
    [InputControl(name = "press", useStateFrom = "tip", synthetic = true, usages = new string[0])]
    [FieldOffset(20)]
    public ushort buttons;

    [FieldOffset(22)]
    public Vector2 padding1;
    [FieldOffset(30)]
    public ushort padding2;

    public FourCC format => kFormat;
}

}

namespace UnityEngine.InputSystem {

/// <summary>An ApplePencil Device for Unity's InputSystem.</summary>
[InputControlLayout(stateType = typeof(ApplePencilState), isGenericTypeOfDevice = false)]
public class ApplePencil : Pen {
    /// <summary>Size of the shared memory buffer in ApplePencilStates.  Native code will write to this and we'll read
    /// from it, but we're in charge of creating and destroying it.</summary>
    public const int BufferLength = 1000;
    /// <summary>Have we done our setup work yet?</summary>
    public static bool initialized { get; private set; }
    /// <summary>The shared memory buffer for ApplePencilStates.  It's a circular buffer.</summary>
    public static NativeArray<ApplePencilState> buffer;

    /// <summary>Called on startup to initialize the shared memory buffer and establish communication with our native
    /// code bridge, etc.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() {
        if (initialized)
            return;
        Debug.Log("Registering the Apple Pencil layout with InputSystem...");
        InputSystem.RegisterLayout<ApplePencil>(matches: new InputDeviceMatcher().WithInterface("Apple Pencil"));
        Debug.Log($"Creating state buffer...");
        buffer = new NativeArray<ApplePencilState>(BufferLength, Allocator.Persistent);
        Debug.Log($"Setting Apple Pencil event handler...");
        // unsafe here means we can pass the buffer pointer straight to native code, and .NET isn't managing it
        unsafe {
            SetApplePencilEventHandler(OnApplePencilEvent, NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);
        }
        // normally I guess the system would discover the device and add it, but I don't know how to hook that up
        Debug.Log($"Creating Apple Pencil device...");
        InputSystem.AddDevice(new InputDeviceDescription {
            interfaceName = "Apple Pencil",
            product = "Apple Pencil"
        });
        initialized = true;
    }

    public static new ApplePencil current { get; private set; }
    public override void MakeCurrent() {
        base.MakeCurrent();
        current = this;
    }

    protected override void OnAdded() {
        base.OnAdded();
    }

    protected override void OnRemoved() {
        base.OnRemoved();
        if (current == this)
            current = null;
    }

    /// <summary>Signature of the callback the native bridge is expecting.</summary>
    delegate void ApplePencilEventHandler(int offset, int numEvents);

    /// <summary>Called by native code after it puts new states/events in our shared buffer.  We need to read the new 
    /// states from the buffer starting at offset, wrapping around to 0 when we hit the end.</summary>
    [AOT.MonoPInvokeCallback(typeof(ApplePencilEventHandler))]
    static void OnApplePencilEvent(int offset, int numEvents) { 
        while (numEvents > 0) {
            var state = buffer[offset];
            InputSystem.QueueStateEvent(current, buffer[offset]);
            offset ++;
            if (offset == buffer.Length)
                offset = 0;
            numEvents --;
        }
    }

    /// <summary>Tells Unity to look for this method in native code.</summary>
    [DllImport ("__Internal")] 
    unsafe private static extern void SetApplePencilEventHandler(ApplePencilEventHandler handler, void *buffer, 
            int bufferLength);

    /// <summary>This is native code you can call from Unity for testing, to add a fake pencil event.</summary>
    [DllImport ("__Internal")] 
    public static extern void AddApplePencilEvent(float positionX, float positionY, bool tip, float pressure, 
            float tiltX, float tiltY);

    /// <summary>This is native code you can call from Unity for testing, to flush fake pencil events.</summary>
    [DllImport ("__Internal")] 
    public static extern void FlushApplePencilEvents();

}

}