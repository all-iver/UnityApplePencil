This project aims to create an Apple Pencil device for Unity's InputSystem package.

**The problem:** Unity does not fully integrate with Apple Pencil.  Apple Pencil can provide events at 240hz, but only does 60hz by default with an extra API for getting all the events.  Apple also provides an API for estimating values like pen pressure before the real values are ready (filling in the correct values later), as well as predicting future events in order to reduce visual latency when drawing, not to mention barrel-tap events with the Apple Pencil 2.  As of writing this, Unity doesn't support any of that, which means that it's impossible to draw a smooth curve with Apple Pencil using Unity.

**Unity's Input Situation:** As of writing this, Unity has two built-in Input methods.  The original Input (Old Input), and the newer InputSystem package which is still in development but is packaged with newer versions of Unity.  The old Input system is capped to framerate, so even if we could get 240z from Apple Pencil we still wouldn't be able to consume the events since Input would throw most of them away.  *(Note - A very recent alpha build adds support for getting coalesced events with Old Input on Windows, but in my testing it is still weirdly giving me fewer events on lower framerates.)*  The newer InputSystem package is able to give us uncapped events using its lower level `onEvent()` API, but it doesn't call the full Apple API so we still only get 60hz on the device.

**The solution:** We need to write native code that can retrieve Apple Pencil events using the full API, and then make an InputSystem Device subclass that provides them in Unity.  There are a few parts to this:

1) A replacement UnityView+iOS.mm that needs to be copied into your built XCode project.  This lets us modify Unity's touch event handlers to call our own code instead.
1) A native code bridge, iOSApplePencil.hpp, which exposes methods we can call from Unity code.  UnityView+iOS.mm sends touch events to the bridge, which puts them in a buffer that both it and Unity can use directly.  The bridge then tells Unity when new events have been put in the buffer.  *(Note - The bridge is unfortunately written in C++ instead of Objective-C because I don't have a Mac and can't compile Objective-C code without going through Unity Cloud Build, meaning 20 minute compile times.  Writing it in C++ allows me to test it locally using fake events by compiling it into a DLL.)*
1) An ApplePencil Device that acts as an InputSystem Pointer.

**What's working currently:** Currently I can get Apple Pencil events at 240hz on my iPad, with the original Apple Pencil (I don't have an Apple Pencil 2 to test with).  Right now it only sends position, pressure and tilt to Unity.

**To use:** Follow these steps:
1) In Player Settings in Unity, check the `allow unsafe code` option.  This lets us get a pointer to a memory buffer that we can share with the native code bridge.
1) Put ApplePencil.cs in your Unity project.  To get the full 240hz, you'll need to use it with `onEvent()` like this:

        InputSystem.onEvent += OnInputEvent;
        void OnInputEvent(InputEventPtr eventPtr, InputDevice device) {
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
                return;
            var pencil = device as ApplePencil;
            if (pencil == null)
                return;
            var pos = pencil.position.ReadValueFromEvent(eventPtr);
            var pressure = pencil.pressure.ReadValueFromEvent(eventPtr);
            // draw at a smooth 240hz
        }

1) After building your iOS project in Unity, copy `UnityView+iOS.mm` and `iOSApplePencil.hpp` to `{BUILD}/Classes/UI`.  I'm using Cloud Build and a post-export hook to do this automatically, but maybe you could do it with a Unity post-build hook too.