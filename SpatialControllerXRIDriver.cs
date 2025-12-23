using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

#if UNITY_VISIONOS
using Apple.visionOS.SpatialController;
#endif

namespace Archipelago
{
    /// <summary>
    /// Drives XR Interaction Toolkit controllers using Apple's Spatial Controller on visionOS.
    /// Attach this to your XR Origin root or a manager GameObject.
    /// </summary>
    public class SpatialControllerXRIDriver : MonoBehaviour
    {
        [Header("XR Rig References")]
        [Tooltip("The Left Controller transform from your XR Rig")]
        public Transform leftControllerTransform;
        
        [Tooltip("The Right Controller transform from your XR Rig")]
        public Transform rightControllerTransform;

        [Header("Tracking Settings")]
        [Tooltip("Which position on the controller to track")]
        public TrackingLocation trackingLocation = TrackingLocation.Aim;
        
        [Tooltip("Use raw ARKit transform directly (required for fully immersive Metal apps)")]
        public bool useRawArkitTransform = true;
        
        [Header("Debug")]
        public bool showDebugLogs = true; // Enabled by default to help troubleshoot

        public enum TrackingLocation
        {
            Origin,
            Aim,
            Grip,
            GripSurface
        }

        // Current controller state
        private string _leftControllerUid;
        private string _rightControllerUid;
        
        // Track initialization state
        private bool _trackingReady = false;
        private float _lastTrackingStateLogTime = 0f;
        
        // Public input state for other scripts to read
        public ControllerInputState LeftInput { get; private set; } = new ControllerInputState();
        public ControllerInputState RightInput { get; private set; } = new ControllerInputState();

        [System.Serializable]
        public class ControllerInputState
        {
            public bool TriggerPressed;
            public float TriggerValue;
            public bool GripPressed;
            public float GripValue;
            public bool ButtonAPressed;
            public bool ButtonBPressed;
            public bool MenuPressed;
            public bool ThumbstickPressed;
            public Vector2 ThumbstickValue;
            public bool IsTracking;
        }

#if UNITY_VISIONOS
        private void Start()
        {
            // Initialize the SpatialController system
            AccessoryTracking.Init();
            
            // Register for connection events
            AccessoryTracking.AddAccessoryConnectionHandlers<SpatialControllerXRIDriver>(
                OnAccessoryConnected, 
                OnAccessoryDisconnected, 
                this);

            if (showDebugLogs)
                Debug.Log("[SpatialControllerXRI] Initialized and waiting for controllers...");
        }

        private void OnDestroy()
        {
            AccessoryTracking.RemoveAccessoryConnectionHandlers<SpatialControllerXRIDriver>(
                OnAccessoryConnected, 
                OnAccessoryDisconnected, 
                this);
            AccessoryTracking.Destroy();
        }

        private void OnAccessoryConnected(Accessory accessory, SpatialControllerXRIDriver context)
        {
            string uid = accessory.source.uniqueId;
            var chirality = accessory.inherentChirality;
            
            if (chirality == AccessoryChirality.Left)
                _leftControllerUid = uid;
            else if (chirality == AccessoryChirality.Right)
                _rightControllerUid = uid;

            if (showDebugLogs)
                Debug.Log($"[SpatialControllerXRI] {chirality} controller connected: {uid}");
        }

        private void OnAccessoryDisconnected(Accessory accessory, SpatialControllerXRIDriver context)
        {
            string uid = accessory.source.uniqueId;
            
            if (_leftControllerUid == uid)
            {
                _leftControllerUid = null;
                LeftInput.IsTracking = false;
            }
            else if (_rightControllerUid == uid)
            {
                _rightControllerUid = null;
                RightInput.IsTracking = false;
            }

            if (showDebugLogs)
                Debug.Log($"[SpatialControllerXRI] Controller disconnected: {uid}");
        }

        private void Update()
        {
            // Check if accessory tracking is ready
            var trackingState = AccessoryTracking.GetAccessoryTrackingState();
            var authState = AccessoryTracking.GetAccessoryTrackingAuthorizationState();
            
            if (trackingState != AccessoryTrackingState.Running)
            {
                // Log state periodically during initialization
                if (showDebugLogs && Time.time - _lastTrackingStateLogTime > 1f)
                {
                    _lastTrackingStateLogTime = Time.time;
                    Debug.Log($"[SpatialControllerXRI] Waiting for tracking... State: {trackingState}, Auth: {authState}");
                }
                
                // Mark as not tracking during initialization
                LeftInput.IsTracking = false;
                RightInput.IsTracking = false;
                _trackingReady = false;
                return;
            }
            
            if (!_trackingReady)
            {
                _trackingReady = true;
                if (showDebugLogs)
                    Debug.Log("[SpatialControllerXRI] Accessory tracking is now running!");
            }
            
            // Process all connected accessories
            var accessories = AccessoryTracking.GetConnectedAccessories();
            
            foreach (var accessory in accessories)
            {
                var chirality = accessory.inherentChirality;
                var uid = accessory.source.uniqueId;
                var state = AccessoryTracking.PollController(uid);

                if (state == null) continue;

                // Determine which hand this is
                Transform targetTransform = null;
                ControllerInputState inputState = null;

                if (chirality == AccessoryChirality.Left && leftControllerTransform != null)
                {
                    targetTransform = leftControllerTransform;
                    inputState = LeftInput;
                    _leftControllerUid = uid;
                }
                else if (chirality == AccessoryChirality.Right && rightControllerTransform != null)
                {
                    targetTransform = rightControllerTransform;
                    inputState = RightInput;
                    _rightControllerUid = uid;
                }

                if (targetTransform == null || inputState == null) continue;

                // Update tracking
                UpdateTracking(uid, state, targetTransform, inputState);
                
                // Update input
                UpdateInput(state, inputState);
            }
        }

        private void UpdateTracking(string uid, ControllerState state, Transform targetTransform, ControllerInputState inputState)
        {
            if (state.accessoryAnchors == null || state.accessoryAnchors.Length == 0)
            {
                if (showDebugLogs && inputState.IsTracking)
                    Debug.Log($"[SpatialControllerXRI] {uid}: No accessory anchors available");
                inputState.IsTracking = false;
                return;
            }

            AccessoryAnchor anchor = null;
            
            // For fully immersive Metal apps, use current time since GetPredictedNextFrameTime requires UIWindowScene
            if (useRawArkitTransform)
            {
                // Try prediction with current time + latency offset (no UIWindowScene dependency)
                var currentTime = AccessoryTracking.GetCurrentTime();
                var predictTime = currentTime + 0.022; // ~22ms latency compensation
                anchor = AccessoryTracking.PredictAnchor(uid, predictTime);
                
                // Fallback to polled anchor if prediction fails
                if (anchor == null && state.accessoryAnchors.Length > 0)
                {
                    anchor = state.accessoryAnchors[0];
                }
            }
            else
            {
                // PolySpatial mode: use the utility which queries UIWindowScene timing
                const double DefaultFrameLatencySeconds = 0.022;
                var predictTime = SpatialControllerUtils.GetPredictAnchorTime(DefaultFrameLatencySeconds);
                anchor = AccessoryTracking.PredictAnchor(uid, predictTime);

                // Fallback to polled anchor if prediction fails
                if (anchor == null && state.accessoryAnchors.Length > 0)
                {
                    anchor = state.accessoryAnchors[0];
                    if (showDebugLogs && !inputState.IsTracking)
                        Debug.Log($"[SpatialControllerXRI] {uid}: Using polled anchor (prediction unavailable)");
                }
            }

            if (anchor == null)
            {
                if (showDebugLogs && inputState.IsTracking)
                    Debug.Log($"[SpatialControllerXRI] {uid}: No anchor data available");
                inputState.IsTracking = false;
                return;
            }

            // Get pose - in fully immersive Metal, use raw ARKit transform directly
            Pose? pose = null;
            
            if (useRawArkitTransform)
            {
                // Fully immersive Metal: use originFromAnchorTransform directly (ARKit world space)
                pose = anchor.originFromAnchorTransform;
            }
            else
            {
                // PolySpatial mode: use coordinate space conversion for the selected location
                pose = GetPoseForLocation(anchor);
                
                // Fallback to raw transform if location-specific pose unavailable
                if (!pose.HasValue)
                {
                    pose = anchor.originFromAnchorTransform;
                    if (showDebugLogs && !inputState.IsTracking)
                        Debug.Log($"[SpatialControllerXRI] {uid}: Using originFromAnchorTransform (location pose unavailable)");
                }
            }

            if (pose.HasValue)
            {
                // Since the parent hierarchy (CameraRig/FloorOffset/CameraScale) is all at local (0,0,0),
                // we can set local position/rotation directly from the ARKit pose.
                // This matches how TrackedPoseDriver works on the Camera.
                targetTransform.localPosition = pose.Value.position;
                targetTransform.localRotation = pose.Value.rotation;
                
                if (!inputState.IsTracking && showDebugLogs)
                {
                    Debug.Log($"[SpatialControllerXRI] {uid}: Tracking started! LocalPos: {pose.Value.position}");
                }
                
                inputState.IsTracking = true;
            }
            else
            {
                inputState.IsTracking = false;
            }
        }

        private Pose? GetPoseForLocation(AccessoryAnchor anchor)
        {
            switch (trackingLocation)
            {
                case TrackingLocation.Origin:
                    return anchor.coordinateSpace(ARKitCoordinateSpace.Correction.None);
                    
                case TrackingLocation.Aim:
                    return anchor.coordinateSpace(Accessory.LocationName.aim, ARKitCoordinateSpace.Correction.None);
                    
                case TrackingLocation.Grip:
                    return anchor.coordinateSpace(Accessory.LocationName.grip, ARKitCoordinateSpace.Correction.None);
                    
                case TrackingLocation.GripSurface:
                    return anchor.coordinateSpace(Accessory.LocationName.gripSurface, ARKitCoordinateSpace.Correction.None);
                    
                default:
                    return anchor.coordinateSpace(ARKitCoordinateSpace.Correction.None);
            }
        }

        private void UpdateInput(ControllerState state, ControllerInputState inputState)
        {
            var input = state.input;
            if (input == null) return;

            // Trigger (L1/R1)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonTrigger, out var trigger))
            {
                inputState.TriggerValue = trigger.value;
                inputState.TriggerPressed = trigger.isPressed;
            }
            else
            {
                inputState.TriggerValue = 0f;
                inputState.TriggerPressed = false;
            }

            // Grip (L2/R2)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonGrip, out var grip))
            {
                inputState.GripValue = grip.value;
                inputState.GripPressed = grip.isPressed;
            }
            else
            {
                inputState.GripValue = 0f;
                inputState.GripPressed = false;
            }

            // Button A (Cross/Square)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonA, out var buttonA))
            {
                inputState.ButtonAPressed = buttonA.isPressed;
            }
            else
            {
                inputState.ButtonAPressed = false;
            }

            // Button B (Circle/Triangle)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonB, out var buttonB))
            {
                inputState.ButtonBPressed = buttonB.isPressed;
            }
            else
            {
                inputState.ButtonBPressed = false;
            }

            // Menu (Options/Share)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonMenu, out var menu))
            {
                inputState.MenuPressed = menu.isPressed;
            }
            else
            {
                inputState.MenuPressed = false;
            }

            // Thumbstick button (L3/R3)
            if (input.buttons.TryGetValue(ControllerInputName.ButtonThumbstick, out var thumbstickBtn))
            {
                inputState.ThumbstickPressed = thumbstickBtn.isPressed;
            }
            else
            {
                inputState.ThumbstickPressed = false;
            }

            // Thumbstick axis
            if (input.dpads.TryGetValue(ControllerInputName.DPadThumbstick, out var thumbstick))
            {
                inputState.ThumbstickValue = new Vector2(thumbstick.xAxis, thumbstick.yAxis);
            }
            else
            {
                inputState.ThumbstickValue = Vector2.zero;
            }
        }
#else
        private void Start()
        {
            Debug.LogWarning("[SpatialControllerXRI] This script only works on visionOS. Disabling.");
            enabled = false;
        }
#endif
    }
}
