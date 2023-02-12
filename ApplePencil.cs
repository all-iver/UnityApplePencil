/* 
 * InputSystem device for Apple Pencil.
 * IMPORTANT: You must check "allow unsafe code" in player settings to get the `unsafe` blocks to compile.
 */ 

using System.Runtime.InteropServices;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.Controls;

namespace UnityEngine.InputSystem.LowLevel
{

// from Apple's UITouch docs - specifies the bit each property uses in OUR bitmask (Apple's is minus 1)
public enum UITouchProperty {
    Force = 1,
    Azimuth = 2, // if this changes it changes tilt (x & y)
    Altitude = 3, // if this changes it changes tilt (x & y)
    Location = 4,
    All = 1 << Force | 1 << Azimuth | 1 << Altitude | 1 << Location // get all of them from the buttons bitmask
}

/// <summary>InputSystem state for Apple Pencil.  Size must be a power of two.  Size and layout must match exactly with
/// the corresponding ApplePencilState struct in the native bridge code.</summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct ApplePencilState : IInputStateTypeInfo {
    public static FourCC kFormat => new FourCC('P', 'N', 'C', 'L');

    /// <summary>Position of the touch in the view.  TODO - describe the coordinate space.</summary>
    [InputControl(usage = "Point", dontReset = true)]
    [FieldOffset(0)]
    public Vector2 position;

    /// <summary>Pressure/force of the current touch, scaled from 0 to Apple's maximumPossibleForce value.</summary>
    [InputControl(layout = "Analog", usage = "Pressure", defaultState = 0.0f)]
    [FieldOffset(8)]
    public float pressure;
    
    /// <summary>As per the existing Pen device, this is the tilt on the X and Y axes, scaled from -1 to 1 with 0 being
    /// perpendicular to the surface.  This is the behavior that's documented in the InputSystem docs.</summary>
    [InputControl(layout = "Vector2", displayName = "Tilt", usage = "Tilt")]
    [FieldOffset(12)]
    public Vector2 tilt;

    /// <summary>Button bitmask.  Bit 0 is tip/press, which means the Pencil is currently touching the screen.  Since 
    /// Pencil doesn't send information when it's near the screen but not touching, this is always going to be true 
    /// except for an end/cancel event.  I'm using bits 1-5 for the estimated touch property bitmask.</summary>
    [InputControl(name = "tip", displayName = "Tip", layout = "Button", bit = (int) PenButton.Tip, usage = "PrimaryAction")]
    [InputControl(name = "press", useStateFrom = "tip", synthetic = true, usages = new string[0])]
    [InputControl(name = "expectingUpdateForForce", displayName = "Expecting Update for Force", layout = "Button", bit = (int) UITouchProperty.Force)]
    [InputControl(name = "expectingUpdateForAzimuth", displayName = "Expecting Update for Azimuth", layout = "Button", bit = (int) UITouchProperty.Azimuth)]
    [InputControl(name = "expectingUpdateForAltitude", displayName = "Expecting Update for Altitude", layout = "Button", bit = (int) UITouchProperty.Altitude)]
    [InputControl(name = "expectingUpdateForLocation", displayName = "Expecting Update for Location", layout = "Button", bit = (int) UITouchProperty.Location)]
    // if this is from an iOS touchesEstimatedPropertiesUpdated: call, this bit will be set and it means this is an
    // update to a previous touch with estimated properties.
    [InputControl(name = "isEstimationUpdate", displayName = "Updates Estimated Properties", layout = "Button", bit = (int) UITouchProperty.Location + 1)]
    // if this is from an iOS predictedTouchesForTouch: call, this bit will be set and it means this is a predicted
    // touch that should be thrown away when we get a new real touch.
    [InputControl(name = "isPredicted", displayName = "Is Predicted", layout = "Button", bit = (int) UITouchProperty.Location + 2)]
    // this is so we can get this whole thing with one read...I just want the entire "expecting update" bitmask 
    // without having to do 4 reads, but I don't know how to make an InputControl for it.
    [InputControl(name = "buttonsBitmask", displayName = "Buttons Bitmask", layout = "Integer")]
    [FieldOffset(20)]
    public ushort buttons;

    /// <summary>If iOS is going to send us future updates for any properties, this is the ID of the event so we can
    /// look it up later.  Only relevant if any of the "expecting update" properties is set.  (I bet we could use just 
    /// 2 bytes for this if we ever need more space.  Not sure what Apple uses.)</summary>
    [InputControl(name = "estimationUpdateIndex", displayName = "Estimation Update Index", layout = "Integer")]
    [FieldOffset(22)]
    public uint estimationUpdateIndex;
    [FieldOffset(26)]
    public uint padding1;
    [FieldOffset(30)]
    public ushort padding2;

    public FourCC format => kFormat;

    public bool isPressed => (buttons & (1 << (int) PenButton.Tip)) != 0;

    public bool isEstimationUpdate => (buttons & (1 << ((int) UITouchProperty.Location + 1))) != 0;

    public bool isPredicted => (buttons & (1 << ((int) UITouchProperty.Location + 2))) != 0;
}

}

namespace UnityEngine.InputSystem {

/// <summary>An ApplePencil Device for Unity's InputSystem.</summary>
[InputControlLayout(stateType = typeof(ApplePencilState), isGenericTypeOfDevice = false)]
public class ApplePencil : Pen {
    /// <summary>Size of the shared memory buffer in ApplePencilStates.  Native code will write to this and we'll read
    /// from it, but we're in charge of creating and destroying it.</summary>
    public const int BufferLength = 10000;
    /// <summary>Have we done our setup work yet?</summary>
    public static bool initialized { get; private set; }
    /// <summary>The shared memory buffer for ApplePencilStates.  It's a circular buffer.</summary>
    public static NativeArray<ApplePencilState> buffer;
    /// <summary>The device we added, since we can only have one for now.</summary>
    public static ApplePencil pencil;
    /// <summary>Enable to send events for Apple Pencil estimation updates.  This doesn't work well with UIToolkit, so 
    /// it's best to disable this unless ApplePencil-aware code has captured the pointer.</summary>
    public static bool enableEstimationUpdates = false;    
    /// <summary>Enable to send events for Apple Pencil predictions.  This doesn't work well with UIToolkit, so it's 
    /// best to disable this unless ApplePencil-aware code has captured the pointer.</summary>
    public static bool enablePredictions = true;

    public IntegerControl estimationUpdateIndex { get; private set; }
    public ButtonControl expectingUpdateForForce { get; private set; }
    public ButtonControl expectingUpdateForAzimuth { get; private set; }
    public ButtonControl expectingUpdateForAltitude { get; private set; }
    public ButtonControl expectingUpdateForLocation { get; private set; }
    public IntegerControl buttonsBitmask { get; private set; }
    public ButtonControl isEstimationUpdate { get; private set; }
    public ButtonControl isPredicted { get; private set; }

    /// <summary>Stores the last value of isPressed we had from a non-temporary state.  Used to stop estimated/
    /// predicted touches from changing the pressed state on the device.</summary>
    bool nonEstimatedIsPressed = false;

    /// <summary>Called on startup to initialize the shared memory buffer and establish communication with our native
    /// code bridge, etc.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() {
        if (initialized)
            return;
    #if UNITY_IOS
        Debug.Log("Registering the Apple Pencil layout with InputSystem...");
        InputSystem.RegisterLayout<ApplePencil>(matches: new InputDeviceMatcher().WithInterface("Apple Pencil"));
        Start();
    #endif
    }

    public static void Start() {
        if (initialized) {
            Debug.LogWarning($"ApplePencil already initialized");
            return;
        }
    #if UNITY_IOS
        Debug.Log($"Creating state buffer...");
        buffer = new NativeArray<ApplePencilState>(BufferLength, Allocator.Persistent);
        Debug.Log($"Setting Apple Pencil event handler...");
        // unsafe here means we can pass the buffer pointer straight to native code, and .NET isn't managing it
        unsafe {
            SetApplePencilEventHandler(OnApplePencilEvent, NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);
        }
        // add our new ApplePencil device
        Debug.Log($"Creating Apple Pencil device...");
        pencil = InputSystem.AddDevice(new InputDeviceDescription {
            interfaceName = "Apple Pencil",
            product = "Apple Pencil"
        }) as ApplePencil;
    #endif
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

    protected override void FinishSetup() {
        estimationUpdateIndex = GetChildControl<IntegerControl>("estimationUpdateIndex");
        expectingUpdateForForce = GetChildControl<ButtonControl>("expectingUpdateForForce");
        expectingUpdateForAzimuth = GetChildControl<ButtonControl>("expectingUpdateForAzimuth");
        expectingUpdateForAltitude = GetChildControl<ButtonControl>("expectingUpdateForAltitude");
        expectingUpdateForLocation = GetChildControl<ButtonControl>("expectingUpdateForLocation");
        buttonsBitmask = GetChildControl<IntegerControl>("buttonsBitmask");
        isEstimationUpdate = GetChildControl<ButtonControl>("isEstimationUpdate");
        isPredicted = GetChildControl<ButtonControl>("isPredicted");
        base.FinishSetup();
    }

    public static void Shutdown() {
    #if UNITY_IOS
        if (initialized) {
            Debug.Log("Shutting down Apple Pencil bridge");
            UnsetApplePencilEventHandler();
            buffer.Dispose();
            if (pencil != null) {
                InputSystem.RemoveDevice(pencil);
                pencil = null;
            }
            initialized = false;
       }
    #endif
    }

    /// <summary>Signature of the callback the native bridge is expecting.</summary>
    delegate void ApplePencilEventHandler(int offset, int numEvents);

    /// <summary>Called by native code after it puts new states/events in our shared buffer.  We need to read the new 
    /// states from the buffer starting at offset, wrapping around to 0 when we hit the end.</summary>
    [AOT.MonoPInvokeCallback(typeof(ApplePencilEventHandler))]
    static void OnApplePencilEvent(int offset, int numEvents) {
        while (numEvents > 0) {
            var state = buffer[offset];

            // discard this event depending on how we're configured
            bool useEvent = true;
            if (state.isEstimationUpdate && !enableEstimationUpdates)
                useEvent = false;
            if (state.isPredicted && !enablePredictions)
                useEvent = false;

            if (useEvent) {
                // this sucks, but if it's an estimation update or a predicted touch, we don't want to change the 
                // existing value of `tip` or InputSystem will think a stroke began/ended when it didn't.  InputSystem 
                // changes the state on the device based on our estimated/predicted events, which don't have the right 
                // value, and that makes it hard for code to tell if a press or non-press state is a change or if it 
                // has just been toggled randomly, since you can't rely on the device's attributes to tell you if it
                // was previously pressed or not.  this solution keeps the press state from the last real touch and 
                // applies it to estimated/predicted touches so the latter never change its state.
                // FIXME - find a better way to do this?
                if (state.isEstimationUpdate || state.isPredicted) {
                    // rely on PenButton.Tip being bit 0
                    state.buttons >>= 1;
                    state.buttons <<= 1;
                    if (current.nonEstimatedIsPressed)
                        state.buttons |= (1 << (int) PenButton.Tip);
                } else
                    current.nonEstimatedIsPressed = state.isPressed;

                InputSystem.QueueStateEvent(current, state);
            }

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

    /// <summary>Clear the buffer and handler from native code and resume using Unity's built-in ApplePencil 
    /// support.</summary>
    [DllImport ("__Internal")]
    unsafe private static extern void UnsetApplePencilEventHandler();

    /// <summary>This is native code you can call from Unity for testing, to add a fake pencil event.</summary>
    [DllImport ("__Internal")] 
    public static extern void AddApplePencilEvent(float positionX, float positionY, bool tip, float pressure, 
            float tiltX, float tiltY, uint estimatedPropertiesExpectingUpdates, uint estimationUpdateIndex, 
            bool isEstimationUpdate, bool isPredicted);

    /// <summary>This is native code you can call from Unity for testing, to flush fake pencil events.</summary>
    [DllImport ("__Internal")] 
    public static extern void FlushApplePencilEvents();

}

}