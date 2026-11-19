using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Over-the-shoulder third-person camera. Lives on the Player.
    /// PlayerCharacterController rotates the player (yaw) and the camera pivot (pitch);
    /// this rig places the camera behind the pivot with wall collision, aim zoom, speed FOV and shake.
    /// The camera must be a child of the pivot.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterController))]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [Tooltip("Camera offset from the pivot (x = shoulder, y = up, z = back)")]
        [SerializeField] private Vector3 m_DefaultOffset = new Vector3(1.1f, 0.55f, -4.5f);

        [Tooltip("Camera offset while drawing the bow")]
        [SerializeField] private Vector3 m_AimOffset = new Vector3(1.0f, 0.35f, -2.4f);

        [Tooltip("How fast the camera moves between offsets")]
        [SerializeField] private float m_OffsetSharpness = 12f;

        [Header("Field of View")]
        [SerializeField] private float m_DefaultFov = 65f;
        [SerializeField] private float m_AimFov = 45f;

        [Tooltip("Extra FOV added at high speed (grapple, falls)")]
        [SerializeField] private float m_SpeedFovBonus = 15f;

        [Tooltip("Speed at which the speed FOV starts")]
        [SerializeField] private float m_SpeedFovMinSpeed = 16f;

        [Tooltip("Speed at which the speed FOV is at its maximum")]
        [SerializeField] private float m_SpeedFovMaxSpeed = 40f;

        [SerializeField] private float m_FovSharpness = 8f;

        [Header("Collision")]
        [SerializeField] private float m_CollisionRadius = 0.25f;
        [SerializeField] private LayerMask m_CollisionLayers = 1; // Default
        [SerializeField] private float m_MinDistanceFromPivot = 0.4f;

        [Header("Shake")]
        [Tooltip("Maximum positional shake (meters)")]
        [SerializeField] private float m_MaxShake = 0.35f;

        [Tooltip("How fast the shake fades (per real-time second)")]
        [SerializeField] private float m_ShakeDecay = 4f;

        [SerializeField] private float m_ShakeFrequency = 30f;

        PlayerCharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;
        Camera m_Camera;
        Vector3 m_CurrentOffset;
        float m_ShakeTrauma;
        readonly RaycastHit[] m_Hits = new RaycastHit[32];

        public Camera Camera => m_Camera;

        void Awake()
        {
            m_Controller = GetComponent<PlayerCharacterController>();
            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            m_Camera = m_Controller.PlayerCamera;
            m_CurrentOffset = m_DefaultOffset;
        }

        /// <summary>Adds camera shake (0..1 trauma, summed and clamped).</summary>
        public void AddShake(float amount)
        {
            m_ShakeTrauma = Mathf.Clamp01(m_ShakeTrauma + amount);
        }

        void LateUpdate()
        {
            Transform pivot = m_Controller.CameraPivot;
            bool aiming = m_WeaponsManager != null && m_WeaponsManager.IsAiming;
            float dt = Time.unscaledDeltaTime;

            // offset
            Vector3 targetOffset = aiming ? m_AimOffset : m_DefaultOffset;
            m_CurrentOffset = Vector3.Lerp(m_CurrentOffset, targetOffset, 1f - Mathf.Exp(-m_OffsetSharpness * dt));

            // collision: sphere cast from pivot to desired position
            Vector3 pivotPos = pivot.position;
            Vector3 desired = pivot.TransformPoint(m_CurrentOffset);
            Vector3 toDesired = desired - pivotPos;
            float desiredDistance = toDesired.magnitude;
            Vector3 dir = desiredDistance > 0.0001f ? toDesired / desiredDistance : -pivot.forward;
            float distance = desiredDistance;

            int count = Physics.SphereCastNonAlloc(pivotPos, m_CollisionRadius, dir, m_Hits, desiredDistance,
                m_CollisionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = m_Hits[i];
                if (IsOwnCollider(hit.collider) || hit.distance <= 0f)
                    continue;
                distance = Mathf.Min(distance, hit.distance);
            }

            distance = Mathf.Max(distance, m_MinDistanceFromPivot);
            Vector3 cameraPos = pivotPos + dir * distance;

            // shake
            float shake = m_ShakeTrauma * m_ShakeTrauma;
            if (shake > 0f)
            {
                float t = Time.unscaledTime * m_ShakeFrequency;
                Vector3 noise = new Vector3(
                    Mathf.PerlinNoise(t, 0.1f) - 0.5f,
                    Mathf.PerlinNoise(0.2f, t) - 0.5f,
                    0f) * 2f;
                cameraPos += pivot.rotation * noise * (m_MaxShake * shake);
                m_ShakeTrauma = Mathf.Max(0f, m_ShakeTrauma - m_ShakeDecay * dt);
            }

            m_Camera.transform.SetPositionAndRotation(cameraPos, pivot.rotation);

            // FOV
            float speed = m_Controller.CharacterVelocity.magnitude;
            float speedRatio = Mathf.InverseLerp(m_SpeedFovMinSpeed, m_SpeedFovMaxSpeed, speed);
            float targetFov = (aiming ? m_AimFov : m_DefaultFov) + speedRatio * m_SpeedFovBonus;
            m_Camera.fieldOfView = Mathf.Lerp(m_Camera.fieldOfView, targetFov, 1f - Mathf.Exp(-m_FovSharpness * dt));
        }

        /// <summary>
        /// Raycast through the crosshair (screen center), ignoring the player and anything between the camera and the player.
        /// </summary>
        public bool TryGetCrosshairHit(float maxDistance, LayerMask layers, QueryTriggerInteraction triggers,
            out RaycastHit result, float radius = 0f)
        {
            Transform cam = m_Camera.transform;
            Vector3 forward = cam.forward;
            float startOffset = Mathf.Max(0f, Vector3.Dot(m_Controller.CameraPivot.position - cam.position, forward));
            Vector3 origin = cam.position + forward * startOffset;

            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, forward, m_Hits, maxDistance, layers, triggers)
                : Physics.RaycastNonAlloc(origin, forward, m_Hits, maxDistance, layers, triggers);

            result = default;
            float closest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = m_Hits[i];
                if (IsOwnCollider(hit.collider))
                    continue;

                // sphere casts starting inside a collider report distance 0 and point zero: skip them
                if (radius > 0f && hit.distance <= 0f)
                    continue;

                if (hit.distance < closest)
                {
                    closest = hit.distance;
                    result = hit;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Origin of the crosshair ray (just in front of the player, along the camera forward).</summary>
        public Vector3 GetCrosshairRayOrigin()
        {
            Transform cam = m_Camera.transform;
            float startOffset = Mathf.Max(0f, Vector3.Dot(m_Controller.CameraPivot.position - cam.position, cam.forward));
            return cam.position + cam.forward * startOffset;
        }

        public bool IsOwnCollider(Collider c)
        {
            return c != null && c.transform.IsChildOf(transform);
        }
    }
}
