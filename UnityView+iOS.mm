/* 
 * This is a copy of Unity's UnityView+iOS.mm that it puts in your built XCode project.  It has been modified to send 
 * Apple Pencil touch events to iOSApplePencil.hpp, which buffers them and sends them to Unity.  The only modifications
 * are the touch event handlers at the bottom, and some extra include lines.
 */
#if PLATFORM_IOS

#import "UnityView.h"
#import "UnityAppController+Rendering.h"
#include "OrientationSupport.h"

#include <math.h>
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
// - (void)touchesCancelled:(NSSet*)touches withEvent:(UIEvent*)event  { UnitySendTouchesCancelled(touches, event); }

- (void)addApplePencilEventForTouch:(UITouch *)touch isEstimationUpdate:(BOOL)isEstimationUpdate isPredicted:(BOOL)isPredicted {
    // I think preciseLocationInView has sub-pixel accuracy, vs locationInView which gives you pixels, maybe?
    CGPoint location = [touch preciseLocationInView:self];
    // convert the location from points to pixels and invert y
    location.x *= self.contentScaleFactor;
    location.y = (self.bounds.size.height - location.y) * self.contentScaleFactor;
    // the touch force is scaled so an average touch is 1, but we'll rescale so 1 is the max possible force,
    // which is more in line with what InputSystem does for other devices.
    CGFloat pressure = [touch force] / [touch maximumPossibleForce];
    // we have azimuth and altitude (both in radians)
    CGFloat azimuth = [touch azimuthAngleInView:self];
    CGFloat altitude = [touch altitudeAngle];
    // InputSystem's Pen wants tiltX and tiltY, which each go from -1 to 1 with 0 being perpendicular to the
    // screen, so we need to calculate those.  I believe Windows uses that style of coordinates natively.
    // adapted from https://gist.github.com/k3a/2903719bb42b48c9198d20c2d6f73ac1
    CGFloat tiltXrad = 0, tiltYrad = 0;
    if (altitude == M_PI/2) {
        // perpendicular to the pad
        tiltXrad = 0;
        tiltYrad = 0;
    } else if (altitude == 0) {
        // when pen is laying on the pad it is impossible to precisely encode but at least approximate for 4 cases
        if (azimuth > 7 * M_PI/4 || azimuth <= M_PI/4) {
            // for azimuth == 0, the pen is on the positive Y axis
            tiltXrad = 0;
            tiltYrad = M_PI/2;
        } else if (azimuth > M_PI/4 && azimuth <= 3 * M_PI/4) {
            // for azimuth == M_PI/2 the pen is on the positive X axis
            tiltXrad = M_PI/2;
            tiltYrad = 0;
        } else if (azimuth > 3 * M_PI/4 && azimuth <= 5 * M_PI/4) {
            // for azimuth == M_PI, the pen is on the negative Y axis
            tiltXrad = 0;
            tiltYrad = -M_PI/2;
        } else if (azimuth > 5 * M_PI/4 && azimuth <= 7 * M_PI/4) {
            // for azimuth == M_PI + M_PI/2 pen on negative X axis
            tiltXrad = -M_PI/2;
            tiltYrad = 0;
        }
    } else {
        CGFloat tanAlt = tanf(altitude); // tan(x) = sin(x)/cos(x)
        tiltXrad = atanf(sinf(azimuth) / tanAlt);
        tiltYrad = atanf(cosf(azimuth) / tanAlt);
    }
    CGFloat tiltX = tiltXrad / (M_PI/2);
    CGFloat tiltY = tiltYrad / (M_PI/2);
    // 'tip' means the pen is touching the screen, so not ended (3) or cancelled (4).  estimation updates can come in
    // after the touch has ended which will make Unity think it's a new stroke, so we have a workaround for that on the
    // Unity side.
    bool tip = touch.phase != UITouchPhaseEnded && touch.phase != UITouchPhaseCancelled;
    AddApplePencilEvent(location.x, location.y, tip, pressure, tiltX, tiltY, 
            (unsigned int) touch.estimatedPropertiesExpectingUpdates, 
            touch.estimationUpdateIndex != nil ? touch.estimationUpdateIndex.unsignedIntegerValue : 0, 
            isEstimationUpdate, isPredicted);
}

- (void)sendPencilTouchesToUnity:(NSSet *)touches withEvent:(UIEvent *)event {
    // fixme - is it possible to have multiple pencil touches in one event?  if so, we're sending predicted touches
    //      for each one when we only need to do it for the last one
    for (UITouch *touch in touches) {
        // if it's not a pencil touch, ignore it
        if ((int) touch.type != UITouchTypePencil)
            continue;
        // get the high-fidelity touches (these include the existing touch)
        NSArray *coalescedTouches = [event coalescedTouchesForTouch:touch];
        for (UITouch *ct in coalescedTouches)
            [self addApplePencilEventForTouch:ct isEstimationUpdate:false isPredicted:false];
        // get any predicted touches too.  it's important that the predicted ones get sent after real ones because
        // the frontend wants to easily be able to discard these after any new real touches.
        for (UITouch *pt in [event predictedTouchesForTouch:touch])
            [self addApplePencilEventForTouch:pt isEstimationUpdate:false isPredicted:true];
    }
    // dispatch on the Unity main thread...I think this is required?
    // dispatch_async(dispatch_get_main_queue(), ^{
        FlushApplePencilEvents();
    // });
}

- (NSSet *)getPencilTouches:(NSSet *)touches {
    NSMutableSet *filteredTouches = [NSMutableSet setWithCapacity:touches.count];
    for (UITouch *touch in touches) {
        if ((int) touch.type == UITouchTypePencil)
            [filteredTouches addObject:touch];
    }
    return filteredTouches;
}

- (NSSet *)getFingerTouches:(NSSet *)touches {
    NSMutableSet *filteredTouches = [NSMutableSet setWithCapacity:touches.count];
    for (UITouch *touch in touches) {
        if ((int) touch.type == UITouchTypeDirect)
            [filteredTouches addObject:touch];
    }
    return filteredTouches;
}

- (void)touchesBegan:(NSSet*)touches withEvent:(UIEvent*)event {
    // fixme - can we ever get pencil touches and finger touches in the same event?
    NSSet *fingerTouches = [self getFingerTouches:touches];
    NSSet *pencilTouches = [self getPencilTouches:touches];
    if (fingerTouches.count > 0) {
        UnitySendTouchesBegin(fingerTouches, event);
    }
    if (pencilTouches.count > 0) {
        if (ApplePencilHandlerIsEnabled())
            [self sendPencilTouchesToUnity:pencilTouches withEvent:event];
        else
            UnitySendTouchesBegin(pencilTouches, event);
    }
}

- (void)touchesMoved:(NSSet*)touches withEvent:(UIEvent*)event { 
    // fixme - can we ever get pencil touches and finger touches in the same event?
    NSSet *fingerTouches = [self getFingerTouches:touches];
    NSSet *pencilTouches = [self getPencilTouches:touches];
    if (fingerTouches.count > 0) {
        UnitySendTouchesMoved(fingerTouches, event);
    }
    if (pencilTouches.count > 0) {
        if (ApplePencilHandlerIsEnabled())
            [self sendPencilTouchesToUnity:pencilTouches withEvent:event];
        else
            UnitySendTouchesMoved(pencilTouches, event);
    }
}

- (void)touchesEnded:(NSSet*)touches withEvent:(UIEvent*)event {
    // fixme - can we ever get pencil touches and finger touches in the same event?
    NSSet *fingerTouches = [self getFingerTouches:touches];
    NSSet *pencilTouches = [self getPencilTouches:touches];
    if (fingerTouches.count > 0) {
        UnitySendTouchesEnded(fingerTouches, event);
    }
    if (pencilTouches.count > 0) {
        if (ApplePencilHandlerIsEnabled())
            [self sendPencilTouchesToUnity:pencilTouches withEvent:event];
        else
            UnitySendTouchesEnded(pencilTouches, event);
    }
}

- (void)touchesCancelled:(NSSet*)touches withEvent:(UIEvent*)event {
    // fixme - can we ever get pencil touches and finger touches in the same event?
    NSSet *fingerTouches = [self getFingerTouches:touches];
    NSSet *pencilTouches = [self getPencilTouches:touches];
    if (fingerTouches.count > 0) {
        UnitySendTouchesCancelled(fingerTouches, event);
    }
    if (pencilTouches.count > 0) {
        if (ApplePencilHandlerIsEnabled())
            [self sendPencilTouchesToUnity:pencilTouches withEvent:event];
        else
            UnitySendTouchesCancelled(pencilTouches, event);
    }
}

- (void)touchesEstimatedPropertiesUpdated:(NSSet*)touches {
    if (!ApplePencilHandlerIsEnabled())
        return;
    for (UITouch *t in touches)
        [self addApplePencilEventForTouch:t isEstimationUpdate:true isPredicted:false];
    // dispatch on the Unity main thread...I think this is required?
    // dispatch_async(dispatch_get_main_queue(), ^{
        FlushApplePencilEvents();
    // });
}

@end

#endif // PLATFORM_IOS
