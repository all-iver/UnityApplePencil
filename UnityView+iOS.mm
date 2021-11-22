/* 
 * This is a copy of Unity's UnityView+iOS.mm that it puts in your built XCode project.  It has been modified to send 
 * Apple Pencil touch events to iOSApplePencil.hpp, which buffers them and sends them to Unity.  The only modifications
 * are the touch event handlers at the bottom, and an extra include line.
 */
#if PLATFORM_IOS

#import "UnityView.h"
#import "UnityAppController+Rendering.h"
#include "OrientationSupport.h"

#include "iOSApplePencil.hpp"

extern bool _unityAppReady;

@interface UnityView ()
@property (nonatomic, readwrite) ScreenOrientation contentOrientation;
@end

@implementation UnityView (iOS)
- (void)willRotateToOrientation:(UIInterfaceOrientation)toOrientation fromOrientation:(UIInterfaceOrientation)fromOrientation;
{
    // to support the case of interface and unity content orientation being different
    // we will cheat a bit:
    // we will calculate transform between interface orientations and apply it to unity view orientation
    // you can still tweak unity view as you see fit in AppController, but this is what you want in 99% of cases

    ScreenOrientation to    = ConvertToUnityScreenOrientation(toOrientation);
    ScreenOrientation from  = ConvertToUnityScreenOrientation(fromOrientation);

    if (fromOrientation == UIInterfaceOrientationUnknown)
        _curOrientation = to;
    else
        _curOrientation = OrientationAfterTransform(_curOrientation, TransformBetweenOrientations(from, to));

    _viewIsRotating = YES;
}

- (void)didRotate
{
    if (_shouldRecreateView)
    {
        [self recreateRenderingSurface];
    }

    _viewIsRotating = NO;
}

// - (void)touchesBegan:(NSSet*)touches withEvent:(UIEvent*)event      { UnitySendTouchesBegin(touches, event); }
// - (void)touchesMoved:(NSSet*)touches withEvent:(UIEvent*)event      { UnitySendTouchesMoved(touches, event); }
// - (void)touchesEnded:(NSSet*)touches withEvent:(UIEvent*)event      { UnitySendTouchesEnded(touches, event); }

// fixme - do we need to support this for pencil?
- (void)touchesCancelled:(NSSet*)touches withEvent:(UIEvent*)event  { UnitySendTouchesCancelled(touches, event); }

- (bool)sendPencilTouchesToUnity:(NSSet *)touches withEvent:(UIEvent *)event isEnded:(bool)isEnded {
    bool foundPencilTouches = false;
    for (UITouch *touch in touches) {
        // if it's not a pencil touch, ignore it
        if ((int) touch.type != 2) // UITouch.TouchType.Pencil...can't figure out how to use the enum
            continue;
        foundPencilTouches = true;
        NSArray *coalescedTouches = [event coalescedTouchesForTouch:touch];
        for (UITouch *ct in coalescedTouches) {
            CGPoint location = [ct locationInView:self]; // fixme - do we want preciseLocationInView?
            // convert the location from points to pixels and invert y
            location.x *= self.contentScaleFactor;
            location.y = (self.bounds.size.height - location.y) * self.contentScaleFactor;
            CGFloat pressure = [ct force];
            AddApplePencilEvent(location.x, location.y, !isEnded, pressure, 0, 0);
        }
    }
    // dispatch on the Unity main thread...I think this is required?
    dispatch_async(dispatch_get_main_queue(), ^{
        FlushApplePencilEvents();
    });
    return foundPencilTouches;
}

- (void)touchesBegan:(NSSet*)touches withEvent:(UIEvent*)event {
    // fixme - can we ever get pencil touches and finger touches in the same event?
    if ([self sendPencilTouchesToUnity:touches withEvent:event isEnded:false])
        return;
    UnitySendTouchesBegin(touches, event);
}

- (void)touchesEnded:(NSSet*)touches withEvent:(UIEvent*)event {
    // fixme - can we ever get pencil touches and finger touches in the same event?
    if ([self sendPencilTouchesToUnity:touches withEvent:event isEnded:true])
        return;
    UnitySendTouchesEnded(touches, event);
}

- (void)touchesMoved:(NSSet*)touches withEvent:(UIEvent*)event { 
    // fixme - can we ever get pencil touches and finger touches in the same event?
    if ([self sendPencilTouchesToUnity:touches withEvent:event isEnded:false])
        return;
    UnitySendTouchesMoved(touches, event);
}

@end

#endif // PLATFORM_IOS
