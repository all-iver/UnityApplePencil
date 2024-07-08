/* 
 * This is a bridge between Unity and iOS that can provide Apple Pencil events to Unity to work with Unity's 
 * InputSystem package.  It is written in C++ because I don't have a Mac, and C++ lets me compile to a DLL and test on
 * Windows with fake Apple Pencil events.  Also, Objective-C is able to call this code directly since it is compatible 
 * with C++.  Otherwise it could just be written in Objective-C.
 * 
 * The other piece to this is in UnityView+iOS.mm, which is a file that Unity puts in your built XCode project that you
 * need to replace with the one from this project.  Put that along with this file in {BUILD}/Classes/UI/.
 * 
 * For testing on Windows:
 * if __UNITY_DLL__ is defined, we're compiling as a test DLL for Unity on Windows to send fake pencil events.
 * Put the DLL in your Unity Assets folder as a plugin, then write some test code to call AddApplePencilEvent() and 
 * FlushApplePencilEvents() from Unity to simulate pencil events.  I compiled to a DLL with g++ but I'm sure you could 
 * use Visual Studio or something instead.
 * g++ -D__UNITY_DLL__ -c -x c++ iOSApplePencil.hpp -o iOSApplePencil.o; g++ iOSApplePencil.o -o ../Assets/iOSApplePencil.dll; rm iOSApplePencil.o
 */

#ifdef __UNITY_DLL__
    #include <windows.h>

    int WINAPI DllEntryPoint(HINSTANCE hinst, unsigned long reason, void* lpReserved) {
    return 1;
    }

    int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {  
        return 0;
    }

    #define UNITY_EXPORT __declspec(dllexport) 
#else
    #define UNITY_EXPORT
#endif

#ifndef NULL
    #define NULL 0
#endif

// Unity gives us a callback that looks like this, and we'll call it whenever we have new events to send.
typedef void (*ApplePencilEventHandler)(int offset, int numEvents);

// ApplePencilState is the full state of the pencil when we get an event.
// #pragma pack makes it so the compiler doesn't add any extra padding - we need this struct to be exactly the same as
// the C# equivalent.
#pragma pack(push,1)
typedef struct {
    float positionX;
    float positionY;
    float pressure;
    float tiltX;
    float tiltY;
    unsigned short buttons;
    unsigned int estimationUpdateIndex;
    float padding1;
    unsigned short padding2;
} ApplePencilState;
#pragma pack(pop)

// static class to keep track of our buffer and variables.
class ApplePencilManager {
public:
    // the callback we got from Unity
    static ApplePencilEventHandler handler;
    // a circular buffer of ApplePencilState structs, written to by us and read by Unity
    static ApplePencilState *buffer;
    // length of the buffer in ApplePencilStates
    static int bufferLength;
    // current index into the circular buffer that we'll write to next
    static int bufferOffset;
    // we've flushed events to Unity up until this point in the buffer.  the events between lastNotifiedOffset and
    // bufferOffset still need to be sent to Unity (keeping in mind that the buffer is circular).
    static int lastNotifiedOffset;

    // called from iOS code when it knows about a new pencil event - we add a new state to the buffer
    static void AddApplePencilEvent(float _positionX, float _positionY, bool tip, float pressure, float tiltX, 
            float tiltY, unsigned int estimatedPropertiesExpectingUpdates, unsigned int estimationUpdateIndex,
            bool isEstimationUpdate, bool isPredicted) {
        if (buffer == NULL || bufferLength == 0)
            return;
        buffer[bufferOffset].positionX = _positionX;
        buffer[bufferOffset].positionY = _positionY;
        buffer[bufferOffset].estimationUpdateIndex = estimationUpdateIndex;
        buffer[bufferOffset].buttons = tip ? 1 : 0;
        buffer[bufferOffset].buttons |= (estimatedPropertiesExpectingUpdates & 0xf) << 1;
        buffer[bufferOffset].buttons |= isEstimationUpdate << (1 + 4);
        buffer[bufferOffset].buttons |= isPredicted << (2 + 4);
        buffer[bufferOffset].pressure = pressure;
        buffer[bufferOffset].tiltX = tiltX;
        buffer[bufferOffset].tiltY = tiltY;
        bufferOffset ++;
        if (bufferOffset >= bufferLength)
            bufferOffset = 0;
    }

    static void AddApplePencilBarrelTapEvent() {
        if (buffer == NULL || bufferLength == 0)
            return;
        buffer[bufferOffset].positionX = 0;
        buffer[bufferOffset].positionY = 0;
        buffer[bufferOffset].estimationUpdateIndex = 0;
        buffer[bufferOffset].buttons = 1 << (3 + 4);
        buffer[bufferOffset].pressure = 0;
        buffer[bufferOffset].tiltX = 0;
        buffer[bufferOffset].tiltY = 0;
        bufferOffset ++;
        if (bufferOffset >= bufferLength)
            bufferOffset = 0;
    }

    // called from iOS code when we want to send all the new events to Unity
    static void FlushApplePencilEvents() {
        if (buffer == NULL || bufferOffset == lastNotifiedOffset || handler == NULL)
            return;
        // figure out how many states/events there have been since the last time, keeping in mind the buffer is 
        // circular and wraps around
        int numEvents = 0;
        if (bufferOffset > lastNotifiedOffset) {
            numEvents = bufferOffset - lastNotifiedOffset;
        } else {
            numEvents = bufferLength - lastNotifiedOffset;
            numEvents += bufferOffset;
        }
        // this calls back to Unity code
        handler(lastNotifiedOffset, numEvents);
        lastNotifiedOffset = bufferOffset;
    }

};

// required or the linker won't be able to find these...probably a better way to organize this
ApplePencilEventHandler ApplePencilManager::handler = NULL;
ApplePencilState *ApplePencilManager::buffer = NULL;
int ApplePencilManager::bufferLength = 0;
int ApplePencilManager::bufferOffset = 0;
int ApplePencilManager::lastNotifiedOffset = 0;

// extern "C" prevents the compiler from mangling the function names so iOS/C# will be able to find them
extern "C" {

// Unity calls this to set the callback and provide us with the shared buffer we'll write to and Unity reads from
UNITY_EXPORT void SetApplePencilEventHandler(ApplePencilEventHandler _handler, 
        ApplePencilState *_buffer, int _bufferLength) { 
    ApplePencilManager::handler = _handler;
    ApplePencilManager::buffer = _buffer;
    ApplePencilManager::bufferLength = _bufferLength;
    ApplePencilManager::bufferOffset = 0;
    ApplePencilManager::lastNotifiedOffset = 0;
}

// Unity can call this to clean up before releasing the buffer memory
UNITY_EXPORT void UnsetApplePencilEventHandler() { 
    ApplePencilManager::handler = NULL;
    ApplePencilManager::buffer = NULL;
    ApplePencilManager::bufferLength = 0;
    ApplePencilManager::bufferOffset = 0;
    ApplePencilManager::lastNotifiedOffset = 0;
}

UNITY_EXPORT bool ApplePencilHandlerIsEnabled() {
    return ApplePencilManager::handler != NULL;
}

// called by iOS when it knows about a new pencil event.  can also be called from Unity to fake an event for testing.
UNITY_EXPORT void AddApplePencilEvent(float _positionX, float _positionY, bool tip, float pressure, float tiltX, 
        float tiltY, unsigned int estimatedPropertiesExpectingUpdates, unsigned int estimationUpdateIndex, 
        bool isEstimationUpdate, bool isPredicted) {
    ApplePencilManager::AddApplePencilEvent(_positionX, _positionY, tip, pressure, tiltX, tiltY, 
            estimatedPropertiesExpectingUpdates, estimationUpdateIndex, isEstimationUpdate, isPredicted);
}

// called by iOS when it gets a new barrel tap event.  can also be called from Unity for testing.
UNITY_EXPORT void AddApplePencilBarrelTapEvent() {
    ApplePencilManager::AddApplePencilBarrelTapEvent();
}

// called by iOS when it wants to send new events to Unity.  can also be called from Unity for testing.
UNITY_EXPORT void FlushApplePencilEvents() {
    ApplePencilManager::FlushApplePencilEvents();
}

}