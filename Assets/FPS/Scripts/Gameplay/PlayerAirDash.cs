using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Ghostrunner-style air dash ("sensory boost"):
    /// press Sprint while airborne -> time slows down while you hold it and aim (camera + WASD),
    /// release (or run out of aim time) -> dash in that direction. Charges refill on landing and after a grapple pull.
    /// Runs before PlayerCharacterController so it can take over the velocity during the dash.
    /// </summary>
    [DefaultExecutionOrder(-55)]
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerInputHandler))]
    public class PlayerAirDash : MonoBehaviour
    {
        enum State
        {
            Ready,
            Aiming,
            Dashing,
        }

        [Header("Dash")]
        [SerializeField] private float m_DashSpeed = 45f;
        [SerializeField] private float m_DashDuration = 0.2f;

        [Tooltip("Fraction of the dash speed kept as momentum after the dash")]
        [Range(0f, 1f)] [SerializeField] private float m_EndSpeedKept = 0.45f;

        [Tooltip("Dashes available per airtime")]
        [SerializeField] private int m_MaxCharges = 1;

        [SerializeField] private float m_Cooldown = 0.2f;

        [Tooltip("Can't start a dash this soon after leaving the ground (avoids accidental dashes on jump)")]
        [SerializeField] private float m_MinAirTime = 0.08f;

        [Header("Slow Motion Aim")]
        [Tooltip("Tap Sprint = instant dash. Holding it longer than this (real seconds) slows time so you can aim the dash")]
        [SerializeField] private float m_SlowMotionHoldDelay = 0.15f;

        [Range(0.02f, 1f)] [SerializeField] private float m_SlowMotionScale = 0.15f;

        [Tooltip("Max real-time seconds of slow motion before the dash triggers automatically")]
        [SerializeField] private float m_MaxAimTime = 1.2f;

        [Header("Feedback")]
        [SerializeField] private float m_DashShake = 0.25f;
        [SerializeField] private AudioClip m_DashSfx;

        public bool IsAiming => m_State == State.Aiming;
        public bool IsDashing => m_State == State.Dashing;
        public int ChargesLeft => m_Charges;
        public Vector3 DashDirection => m_DashDirection;

        PlayerCharacterController m_Controller;
        PlayerInputHandler m_Input;
        ThirdPersonCameraRig m_CameraRig;
        GrappleHook m_Grapple;
        AudioSource m_AudioSource;

        State m_State;
        int m_Charges;
        float m_AimStartUnscaled;
        bool m_SlowMotionApplied;
        float m_DashTimer;
        float m_LastDashEndTime = -10f;
        float m_LastGroundedTime;
        Vector3 m_DashDirection;

        void Awake()
        {
            m_Controller = GetComponent<PlayerCharacterController>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_CameraRig = GetComponent<ThirdPersonCameraRig>();
            m_Grapple = GetComponent<GrappleHook>();
            m_AudioSource = GetComponent<AudioSource>();
            m_Charges = m_MaxCharges;
        }

        void OnEnable()
        {
            if (m_Grapple == null)
                m_Grapple = GetComponent<GrappleHook>();
            if (m_Grapple != null)
                m_Grapple.OnPullFinished += RefillCharges;
        }

        void OnDisable()
        {
            if (m_Grapple != null)
                m_Grapple.OnPullFinished -= RefillCharges;
            if (m_State == State.Aiming)
                HitStop.SetSlowMotion(1f);
            m_State = State.Ready;
        }

        void RefillCharges()
        {
            m_Charges = m_MaxCharges;
        }

        void Update()
        {
            if (m_Controller.IsDead)
            {
                CancelAim();
                m_State = State.Ready;
                return;
            }

            if (m_Controller.IsGrounded)
            {
                m_LastGroundedTime = Time.time;
                if (m_State != State.Dashing)
                    RefillCharges();
            }

            switch (m_State)
            {
                case State.Ready:
                    if (CanStartAim() && m_Input.GetSprintInputDown())
                    {
                        m_State = State.Aiming;
                        m_AimStartUnscaled = Time.unscaledTime;
                        m_SlowMotionApplied = false;
                    }

                    break;

                case State.Aiming:
                    if (!m_Input.CanProcessInput() || m_Controller.IsGrounded || (m_Grapple != null && m_Grapple.IsActive))
                    {
                        CancelAim();
                        break;
                    }

                    if (m_Input.GetSprintInputReleased() || Time.unscaledTime >= m_AimStartUnscaled + m_MaxAimTime)
                    {
                        StartDash();
                        UpdateDash();
                        break;
                    }

                    // held (not tapped): slow time down so the dash can be aimed
                    if (!m_SlowMotionApplied && Time.unscaledTime >= m_AimStartUnscaled + m_SlowMotionHoldDelay)
                    {
                        HitStop.SetSlowMotion(m_SlowMotionScale);
                        m_SlowMotionApplied = true;
                    }

                    break;

                case State.Dashing:
                    UpdateDash();
                    break;
            }
        }

        bool CanStartAim()
        {
            return !m_Controller.IsGrounded
                   && m_Charges > 0
                   && Time.time >= m_LastDashEndTime + m_Cooldown
                   && Time.time >= m_LastGroundedTime + m_MinAirTime
                   && (m_Grapple == null || !m_Grapple.IsActive);
        }

        void CancelAim()
        {
            if (m_State == State.Aiming)
            {
                HitStop.SetSlowMotion(1f);
                m_State = State.Ready;
            }
        }

        void StartDash()
        {
            HitStop.SetSlowMotion(1f);

            Transform cam = m_CameraRig != null ? m_CameraRig.Camera.transform : transform;
            Vector3 input = m_Input.GetMoveInput();
            Vector3 dir = input.sqrMagnitude > 0.01f
                ? cam.forward * input.z + cam.right * input.x
                : cam.forward;
            m_DashDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

            m_Charges--;
            m_DashTimer = 0f;
            m_State = State.Dashing;

            if (m_CameraRig)
                m_CameraRig.AddShake(m_DashShake);
            if (m_DashSfx && m_AudioSource)
                m_AudioSource.PlayOneShot(m_DashSfx);
        }

        void UpdateDash()
        {
            // a grapple started during the dash takes over the movement
            if (m_Grapple != null && m_Grapple.IsPulling)
            {
                m_LastDashEndTime = Time.time;
                m_State = State.Ready;
                return;
            }

            m_DashTimer += Time.deltaTime;
            if (m_DashTimer >= m_DashDuration)
            {
                m_Controller.CharacterVelocity = m_DashDirection * (m_DashSpeed * m_EndSpeedKept);
                m_LastDashEndTime = Time.time;
                m_State = State.Ready;
                return;
            }

            m_Controller.RequestMovementOverride();
            m_Controller.CharacterVelocity = m_DashDirection * m_DashSpeed;
        }
    }
}
