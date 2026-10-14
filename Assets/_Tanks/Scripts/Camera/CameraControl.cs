using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tanks.Complete
{
    /// <summary>
    /// Advanced Dynamic Camera Controller for Unity 6 (Tanks Complete).
    /// Supports Perspective Conversion, Seamless Mouse Scrollwheel Zoom, 
    /// Two Distinct Camera Modes (Tactical Overview and Chase/Roof View),
    /// and 360-degree Free Orbit via Right Mouse Button.
    /// Fully backward compatible with GameManager.cs.
    /// </summary>
    public class CameraControl : MonoBehaviour
    {
        [Header("GameManager Compatibility Fields")]
        public float m_DampTime = 0.2f;                 // Smooth damp time for camera movement
        public float m_ScreenEdgeBuffer = 4f;           // Kept for backward compatibility with GameManager
        public float m_MinSize = 6.5f;                  // Kept for backward compatibility with GameManager
        [HideInInspector] public Transform[] m_Targets; // Target array populated by GameManager (index 0 is player)

        [Header("Perspective Camera Settings")]
        public float m_FieldOfView = 60f;

        [Header("Distance Limits & Zoom")]
        public float m_MinDistance = 4.5f;              // Close Third-Person / Roof view
        public float m_MaxDistance = 42.0f;             // High Bird's-Eye tactical view
        public float m_ZoomSpeed = 12.0f;
        public float m_ZoomDampTime = 0.15f;

        [Header("Dynamic Interpolation Offsets")]
        public float m_MinPitch = 16.0f;                // Pitch angle at minDistance (~16 deg)
        public float m_MaxPitch = 55.0f;                // Pitch angle at maxDistance (~55 deg)
        public float m_MinHeightOffset = 1.6f;          // Target height offset at minDistance (~1.6m)
        public float m_MaxHeightOffset = 2.5f;          // Target height offset at maxDistance (~2.5m)

        [Header("Mode Presets")]
        // Mode 1: High Tactical Overview
        public float m_TacticalDistance = 28.0f;
        public float m_TacticalPitch = 50.0f;

        // Mode 2: Close Roof / Chase View
        public float m_ChaseDistance = 6.0f;
        public float m_ChasePitch = 16.0f;

        [Header("Orbit Sensitivity")]
        public float m_OrbitSensitivityX = 3.0f;
        public float m_OrbitSensitivityY = 2.2f;
        public float m_MinOrbitPitch = 5.0f;
        public float m_MaxOrbitPitch = 85.0f;

        public enum CameraMode
        {
            TacticalOverview, // Mode 1: High tactical top-down view
            ChaseRoofView     // Mode 2: Close behind-roof chase view
        }

        [Header("Runtime State")]
        public CameraMode m_CurrentMode = CameraMode.TacticalOverview;

        private Camera m_Camera;
        private float m_CurrentDistance;
        private float m_TargetDistance;
        private float m_DistanceVelocity;

        private float m_CurrentPitch;
        private float m_TargetPitch;
        private float m_PitchVelocity;

        private float m_CurrentYaw;
        private float m_TargetYaw;
        private float m_YawVelocity;

        private Vector3 m_MoveVelocity;
        private Vector3 m_TargetPivot;
        private Vector3 m_CurrentPivot;
        private Vector3 m_PivotVelocity;

        private bool m_IsOrbiting = false;

        private void Awake()
        {
            // Requirement 2: Perspective Conversion
            m_Camera = GetComponentInChildren<Camera>();
            if (m_Camera == null)
            {
                m_Camera = GetComponent<Camera>();
            }

            if (m_Camera != null)
            {
                // Switch orthographic off and configure perspective FOV
                m_Camera.orthographic = false;
                m_Camera.fieldOfView = m_FieldOfView;

                // Reset child camera's local transform so CameraControl directly governs orbital positioning
                m_Camera.transform.localPosition = Vector3.zero;
                m_Camera.transform.localRotation = Quaternion.identity;
            }

            // Initialize default values for Mode 1 (Tactical Overview)
            m_CurrentDistance = m_TacticalDistance;
            m_TargetDistance = m_TacticalDistance;
            m_CurrentPitch = m_TacticalPitch;
            m_TargetPitch = m_TacticalPitch;
            m_CurrentYaw = transform.eulerAngles.y;
            m_TargetYaw = m_CurrentYaw;
        }

        private void Start()
        {
            SetStartPositionAndSize();
        }

        private void Update()
        {
            HandleModeSwitch();
            HandleZoom();
            HandleOrbit();
        }

        private void LateUpdate()
        {
            UpdateCameraTransform(Time.deltaTime);
        }

        /// <summary>
        /// Backward-compatible method called by GameManager.cs at round start.
        /// Instantly snaps the camera to target without damp lag.
        /// </summary>
        public void SetStartPositionAndSize()
        {
            Transform target = GetPrimaryTarget();
            if (target != null)
            {
                float tDist = Mathf.InverseLerp(m_MinDistance, m_MaxDistance, m_TargetDistance);
                float heightOffset = Mathf.Lerp(m_MinHeightOffset, m_MaxHeightOffset, tDist);

                m_TargetPivot = target.position + Vector3.up * heightOffset;
                m_CurrentPivot = m_TargetPivot;

                if (m_CurrentMode == CameraMode.ChaseRoofView)
                {
                    m_TargetYaw = target.eulerAngles.y;
                    m_CurrentYaw = m_TargetYaw;
                }

                m_CurrentDistance = m_TargetDistance;
                m_CurrentPitch = m_TargetPitch;

                Quaternion rotation = Quaternion.Euler(m_CurrentPitch, m_CurrentYaw, 0f);
                Vector3 position = m_CurrentPivot - (rotation * Vector3.forward * m_CurrentDistance);

                transform.position = position;
                transform.rotation = rotation;

                m_MoveVelocity = Vector3.zero;
                m_PivotVelocity = Vector3.zero;
                m_DistanceVelocity = 0f;
                m_PitchVelocity = 0f;
                m_YawVelocity = 0f;
            }
        }

        /// <summary>
        /// Requirement 4: Mode Switching on Key 'C'.
        /// Toggles between Tactical Overview (28m, 50 deg) and Close Roof View (6m, 16 deg).
        /// </summary>
        private void HandleModeSwitch()
        {
            bool switchPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                switchPressed = true;
            }
#endif
            if (!switchPressed)
            {
                try
                {
                    if (Input.GetKeyDown(KeyCode.C))
                    {
                        switchPressed = true;
                    }
                }
                catch
                {
                    // Fallback if legacy input is inactive
                }
            }

            if (switchPressed)
            {
                if (m_CurrentMode == CameraMode.TacticalOverview)
                {
                    // Switch to Mode 2: Close Roof / Chase View
                    m_CurrentMode = CameraMode.ChaseRoofView;
                    m_TargetDistance = m_ChaseDistance;
                    m_TargetPitch = m_ChasePitch;

                    // Snap horizontal yaw directly behind vehicle
                    Transform target = GetPrimaryTarget();
                    if (target != null)
                    {
                        m_TargetYaw = target.eulerAngles.y;
                        m_CurrentYaw = target.eulerAngles.y;
                    }
                }
                else
                {
                    // Switch to Mode 1: High Tactical Overview
                    m_CurrentMode = CameraMode.TacticalOverview;
                    m_TargetDistance = m_TacticalDistance;
                    m_TargetPitch = m_TacticalPitch;
                }
            }
        }

        /// <summary>
        /// Requirement 3: Unified Zoom & Seamless Transition via Mouse ScrollWheel.
        /// Seamlessly zooms distance between 4.5m and 42.0m, smoothly interpolating pitch and height offset.
        /// </summary>
        private void HandleZoom()
        {
            float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                float newScroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(newScroll) > 0.01f)
                {
                    scrollDelta = newScroll * 0.002f;
                }
            }
#endif
            if (Mathf.Abs(scrollDelta) < 0.0001f)
            {
                try
                {
                    float legacyScroll = Input.GetAxis("Mouse ScrollWheel");
                    if (Mathf.Abs(legacyScroll) > 0.0001f)
                    {
                        scrollDelta = legacyScroll;
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            if (Mathf.Abs(scrollDelta) > 0.0001f)
            {
                // Adjust target distance clamped between minDistance and maxDistance
                m_TargetDistance = Mathf.Clamp(m_TargetDistance - scrollDelta * m_ZoomSpeed, m_MinDistance, m_MaxDistance);

                // If not freely orbiting with RMB, smoothly align pitch with the zoom interpolation curve
                if (!m_IsOrbiting)
                {
                    float t = Mathf.InverseLerp(m_MinDistance, m_MaxDistance, m_TargetDistance);
                    m_TargetPitch = Mathf.Lerp(m_MinPitch, m_MaxPitch, t);
                }
            }
        }

        /// <summary>
        /// Requirement 5: Free Orbit Rotation via Right Mouse Button.
        /// Allows 360-degree horizontal yaw and clamped 5-85 vertical pitch rotation.
        /// </summary>
        private void HandleOrbit()
        {
            bool rmbHeld = false;
            float deltaX = 0f;
            float deltaY = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (Mouse.current.rightButton.isPressed)
                {
                    rmbHeld = true;
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    deltaX = delta.x * 0.1f;
                    deltaY = delta.y * 0.1f;
                }
            }
#endif
            if (!rmbHeld)
            {
                try
                {
                    if (Input.GetMouseButton(1))
                    {
                        rmbHeld = true;
                        deltaX = Input.GetAxis("Mouse X");
                        deltaY = Input.GetAxis("Mouse Y");
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            m_IsOrbiting = rmbHeld;

            if (m_IsOrbiting)
            {
                m_TargetYaw += deltaX * m_OrbitSensitivityX;
                m_TargetPitch -= deltaY * m_OrbitSensitivityY;
                m_TargetPitch = Mathf.Clamp(m_TargetPitch, m_MinOrbitPitch, m_MaxOrbitPitch);
            }
            else
            {
                // In Chase Mode, automatically follow tank heading when not manually orbiting
                if (m_CurrentMode == CameraMode.ChaseRoofView)
                {
                    Transform target = GetPrimaryTarget();
                    if (target != null)
                    {
                        m_TargetYaw = Mathf.MoveTowardsAngle(m_TargetYaw, target.eulerAngles.y, 120f * Time.deltaTime);
                    }
                }
            }
        }

        /// <summary>
        /// Calculates smooth camera position and orientation relative to the target tank.
        /// </summary>
        private void UpdateCameraTransform(float deltaTime)
        {
            Transform target = GetPrimaryTarget();
            if (target == null) return;

            // Smoothly interpolate distance
            m_CurrentDistance = Mathf.SmoothDamp(m_CurrentDistance, m_TargetDistance, ref m_DistanceVelocity, m_ZoomDampTime);

            // Compute dynamic height offset based on current distance
            float tDistance = Mathf.InverseLerp(m_MinDistance, m_MaxDistance, m_CurrentDistance);
            float targetHeight = Mathf.Lerp(m_MinHeightOffset, m_MaxHeightOffset, tDistance);

            // Calculate pivot with height offset
            m_TargetPivot = target.position + Vector3.up * targetHeight;
            m_CurrentPivot = Vector3.SmoothDamp(m_CurrentPivot, m_TargetPivot, ref m_PivotVelocity, m_DampTime);

            // Smooth angles
            m_CurrentPitch = Mathf.SmoothDampAngle(m_CurrentPitch, m_TargetPitch, ref m_PitchVelocity, 0.08f);
            m_CurrentYaw = Mathf.SmoothDampAngle(m_CurrentYaw, m_TargetYaw, ref m_YawVelocity, 0.08f);

            // Compute orbital orientation and target position
            Quaternion targetRotation = Quaternion.Euler(m_CurrentPitch, m_CurrentYaw, 0f);
            Vector3 desiredPosition = m_CurrentPivot - (targetRotation * Vector3.forward * m_CurrentDistance);

            // Smoothly damp camera position
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref m_MoveVelocity, m_DampTime);
            transform.rotation = targetRotation;
        }

        /// <summary>
        /// Helper to retrieve the primary human player's tank (index 0, blue ATV) as assigned by GameManager.
        /// </summary>
        private Transform GetPrimaryTarget()
        {
            if (m_Targets != null && m_Targets.Length > 0)
            {
                // Prefer index 0 if valid and active
                if (m_Targets[0] != null && m_Targets[0].gameObject.activeSelf)
                {
                    return m_Targets[0];
                }

                // Otherwise pick first active target
                for (int i = 0; i < m_Targets.Length; i++)
                {
                    if (m_Targets[i] != null && m_Targets[i].gameObject.activeSelf)
                    {
                        return m_Targets[i];
                    }
                }

                // If inactive, return index 0 as reference
                if (m_Targets[0] != null) return m_Targets[0];
            }

            return null;
        }
    }
}