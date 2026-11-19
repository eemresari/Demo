using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(AudioSource))]
    public class PlayerCharacterController : MonoBehaviour
    {
        [Header("References")] [Tooltip("Reference to the main camera used for the player")]
        public Camera PlayerCamera;

        [Tooltip("Third-person: transform that receives the camera pitch and height (camera is its child). " +
                 "If empty, the camera itself is used (first-person behaviour).")]
        [SerializeField] private Transform m_CameraPivot;

        [Tooltip("Audio source for footsteps, jump, etc...")]
        public AudioSource AudioSource;

        [Header("General")] [Tooltip("Force applied downward when in the air")]
        public float GravityDownForce = 20f;

        [Tooltip("Physic layers checked to consider the player grounded")]
        public LayerMask GroundCheckLayers = -1;

        [Tooltip("distance from the bottom of the character controller capsule to test for grounded")]
        public float GroundCheckDistance = 0.05f;

        [Header("Movement")] [Tooltip("Max movement speed when grounded (when not sprinting)")]
        public float MaxSpeedOnGround = 10f;

        [Tooltip(
            "Sharpness for the movement when grounded, a low value will make the player accelerate and decelerate slowly, a high value will do the opposite")]
        public float MovementSharpnessOnGround = 15;

        [Tooltip("Max movement speed when crouching")] [Range(0, 1)]
        public float MaxSpeedCrouchedRatio = 0.5f;

        [Tooltip("Max movement speed when not grounded")]
        public float MaxSpeedInAir = 10f;

        [Tooltip("Acceleration speed when in the air")]
        public float AccelerationSpeedInAir = 25f;

        [Tooltip("When horizontal air speed is above MaxSpeedInAir (e.g. after a grapple release), " +
                 "it decays toward the max at this rate (m/s per second) instead of being clamped instantly")]
        [SerializeField] private float m_AirMomentumDecay = 6f;

        [Tooltip("Ground speed multiplier while aiming / drawing the bow")]
        [Range(0.1f, 1f)] [SerializeField] private float m_AimingMoveSpeedMultiplier = 0.75f;

        [Header("Slide (Crouch while moving fast)")]
        [SerializeField] private bool m_SlideEnabled = true;

        [Tooltip("Minimum horizontal speed to start a slide (otherwise Crouch just crouches)")]
        [SerializeField] private float m_SlideMinStartSpeed = 9f;

        [Tooltip("Speed added when the slide starts")]
        [SerializeField] private float m_SlideBoost = 5f;

        [SerializeField] private float m_SlideMaxSpeed = 32f;

        [Tooltip("Deceleration on flat ground (m/s per second)")]
        [SerializeField] private float m_SlideFriction = 7f;

        [Tooltip("Slide ends below this speed")]
        [SerializeField] private float m_SlideEndSpeed = 6f;

        [Tooltip("How fast WASD can bend the slide direction (degrees per second)")]
        [SerializeField] private float m_SlideSteering = 90f;

        [SerializeField] private float m_SlideMaxDuration = 2.5f;

        [Tooltip("Multiplicator for the sprint speed (based on grounded speed)")]
        public float SprintSpeedModifier = 2f;

        [Tooltip("Height at which the player dies instantly when falling off the map")]
        public float KillHeight = -50f;

        [Header("Rotation")] [Tooltip("Rotation speed for moving the camera")]
        public float RotationSpeed = 200f;

        [Range(0.1f, 1f)] [Tooltip("Rotation speed multiplier when aiming")]
        public float AimingRotationMultiplier = 0.4f;

        [Header("Jump")] [Tooltip("Force applied upward when jumping")]
        public float JumpForce = 9f;

        [Tooltip("Extra jumps available in the air (1 = double jump). Refilled on landing and after a grapple pull.")]
        [SerializeField] private int m_MaxAirJumps = 1;

        [Tooltip("Upward speed of an air jump (sets vertical velocity, so it works even while falling)")]
        [SerializeField] private float m_AirJumpForce = 11f;

        [Tooltip("On an air jump, horizontal velocity is redirected toward the WASD direction (keeping at least this speed)")]
        [SerializeField] private float m_AirJumpMinHorizontalSpeed = 8f;

        [Header("Stance")] [Tooltip("Ratio (0-1) of the character height where the camera will be at")]
        public float CameraHeightRatio = 0.9f;

        [Tooltip("Height of character when standing")]
        public float CapsuleHeightStanding = 1.8f;

        [Tooltip("Height of character when crouching")]
        public float CapsuleHeightCrouching = 0.9f;

        [Tooltip("Speed of crouching transitions")]
        public float CrouchingSharpness = 10f;

        [Header("Audio")] [Tooltip("Amount of footstep sounds played when moving one meter")]
        public float FootstepSfxFrequency = 1f;

        [Tooltip("Amount of footstep sounds played when moving one meter while sprinting")]
        public float FootstepSfxFrequencyWhileSprinting = 1f;

        [Tooltip("Sound played for footsteps")]
        public AudioClip FootstepSfx;

        [Tooltip("Sound played when jumping")] public AudioClip JumpSfx;
        [Tooltip("Sound played when landing")] public AudioClip LandSfx;

        [Tooltip("Sound played when taking damage froma fall")]
        public AudioClip FallDamageSfx;

        [Header("Fall Damage")]
        [Tooltip("Whether the player will recieve damage when hitting the ground at high speed")]
        public bool RecievesFallDamage;

        [Tooltip("Minimun fall speed for recieving fall damage")]
        public float MinSpeedForFallDamage = 10f;

        [Tooltip("Fall speed for recieving th emaximum amount of fall damage")]
        public float MaxSpeedForFallDamage = 30f;

        [Tooltip("Damage recieved when falling at the mimimum speed")]
        public float FallDamageAtMinSpeed = 10f;

        [Tooltip("Damage recieved when falling at the maximum speed")]
        public float FallDamageAtMaxSpeed = 50f;

        public UnityAction<bool> OnStanceChanged;

        public Vector3 CharacterVelocity { get; set; }
        public bool IsGrounded { get; private set; }
        public bool HasJumpedThisFrame { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSliding { get; private set; }
        public int AirJumpsLeft => m_AirJumpsLeft;

        /// <summary>Another system used this frame's Jump press (e.g. grapple cancel): don't air-jump on it.</summary>
        public void ConsumeJumpInputThisFrame()
        {
            m_JumpConsumedFrame = Time.frameCount;
        }

        /// <summary>Gives back the air jumps (e.g. after a grapple pull).</summary>
        public void RefillAirJumps()
        {
            m_AirJumpsLeft = m_MaxAirJumps;
        }

        /// <summary>Transform that holds camera pitch (pivot in third person, camera in first person).</summary>
        public Transform CameraPivot => m_CameraPivot != null ? m_CameraPivot : PlayerCamera.transform;

        /// <summary>
        /// True while an external system (grapple, melee lunge) drives CharacterVelocity this frame.
        /// The controller then skips ground snapping, input acceleration, gravity and speed clamps,
        /// and only moves the capsule + resolves collisions.
        /// </summary>
        public bool IsMovementOverridden => m_MovementOverrideFrame == Time.frameCount;

        /// <summary>Call every frame (before this controller's Update) while driving velocity externally.</summary>
        public void RequestMovementOverride()
        {
            m_MovementOverrideFrame = Time.frameCount;
        }

        /// <summary>Leaves the ground immediately and prevents ground snapping for a short time.</summary>
        public void ForceUngrounded()
        {
            m_LastTimeJumped = Time.time;
            IsGrounded = false;
            m_GroundNormal = Vector3.up;
        }

        public float RotationMultiplier
        {
            get
            {
                if (m_WeaponsManager.IsAiming)
                {
                    return AimingRotationMultiplier;
                }

                return 1f;
            }
        }

        Health m_Health;
        PlayerInputHandler m_InputHandler;
        CharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;
        Actor m_Actor;
        Vector3 m_GroundNormal;
        Vector3 m_CharacterVelocity;
        Vector3 m_LatestImpactSpeed;
        float m_LastTimeJumped = 0f;
        float m_CameraVerticalAngle = 0f;
        float m_FootstepDistanceCounter;
        float m_TargetCharacterHeight;
        int m_MovementOverrideFrame = -1;
        float m_SlideStartTime;
        int m_AirJumpsLeft;
        int m_JumpConsumedFrame = -1;

        const float k_JumpGroundingPreventionTime = 0.2f;
        const float k_GroundCheckDistanceInAir = 0.07f;

        void Awake()
        {
            ActorsManager actorsManager = FindAnyObjectByType<ActorsManager>();
            if (actorsManager != null)
                actorsManager.SetPlayer(gameObject);
        }

        void Start()
        {
            // fetch components on the same gameObject
            m_Controller = GetComponent<CharacterController>();
            DebugUtility.HandleErrorIfNullGetComponent<CharacterController, PlayerCharacterController>(m_Controller,
                this, gameObject);

            m_InputHandler = GetComponent<PlayerInputHandler>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerInputHandler, PlayerCharacterController>(m_InputHandler,
                this, gameObject);

            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerWeaponsManager, PlayerCharacterController>(
                m_WeaponsManager, this, gameObject);

            m_Health = GetComponent<Health>();
            DebugUtility.HandleErrorIfNullGetComponent<Health, PlayerCharacterController>(m_Health, this, gameObject);

            m_Actor = GetComponent<Actor>();
            DebugUtility.HandleErrorIfNullGetComponent<Actor, PlayerCharacterController>(m_Actor, this, gameObject);

            m_Controller.enableOverlapRecovery = true;

            m_Health.OnDie += OnDie;

            // force the crouch state to false when starting
            SetCrouchingState(false, true);
            UpdateCharacterHeight(true);
        }

        void Update()
        {
            // check for Y kill
            if (!IsDead && transform.position.y < KillHeight)
            {
                m_Health.Kill();
            }

            HasJumpedThisFrame = false;

            bool wasGrounded = IsGrounded;
            if (IsMovementOverridden)
            {
                IsGrounded = false;
                m_GroundNormal = Vector3.up;
            }
            else
            {
                GroundCheck();
            }

            if (IsGrounded)
                m_AirJumpsLeft = m_MaxAirJumps;

            // landing
            if (IsGrounded && !wasGrounded)
            {
                // Fall damage
                float fallSpeed = -Mathf.Min(CharacterVelocity.y, m_LatestImpactSpeed.y);
                float fallSpeedRatio = (fallSpeed - MinSpeedForFallDamage) /
                                       (MaxSpeedForFallDamage - MinSpeedForFallDamage);
                if (RecievesFallDamage && fallSpeedRatio > 0f)
                {
                    float dmgFromFall = Mathf.Lerp(FallDamageAtMinSpeed, FallDamageAtMaxSpeed, fallSpeedRatio);
                    m_Health.TakeDamage(dmgFromFall, null);

                    // fall damage SFX
                    AudioSource.PlayOneShot(FallDamageSfx);
                }
                else
                {
                    // land SFX
                    AudioSource.PlayOneShot(LandSfx);
                }
            }

            // crouching
            if (m_InputHandler.GetCrouchInputDown())
            {
                if (IsSliding)
                    StopSlide();
                else if (CanStartSlide())
                    StartSlide();
                else
                    SetCrouchingState(!IsCrouching, false);
            }

            UpdateCharacterHeight(false);

            HandleCharacterMovement();
        }

        void OnDie()
        {
            IsDead = true;

            // Tell the weapons manager to switch to a non-existing weapon in order to lower the weapon
            m_WeaponsManager.SwitchToWeaponIndex(-1, true);

            EventManager.Broadcast(Events.PlayerDeathEvent);
        }

        void GroundCheck()
        {
            // Make sure that the ground check distance while already in air is very small, to prevent suddenly snapping to ground
            float chosenGroundCheckDistance =
                IsGrounded ? (m_Controller.skinWidth + GroundCheckDistance) : k_GroundCheckDistanceInAir;

            // reset values before the ground check
            IsGrounded = false;
            m_GroundNormal = Vector3.up;

            // only try to detect ground if it's been a short amount of time since last jump; otherwise we may snap to the ground instantly after we try jumping
            if (Time.time >= m_LastTimeJumped + k_JumpGroundingPreventionTime)
            {
                // if we're grounded, collect info about the ground normal with a downward capsule cast representing our character capsule
                if (Physics.CapsuleCast(GetCapsuleBottomHemisphere(), GetCapsuleTopHemisphere(m_Controller.height),
                    m_Controller.radius, Vector3.down, out RaycastHit hit, chosenGroundCheckDistance, GroundCheckLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    // storing the upward direction for the surface found
                    m_GroundNormal = hit.normal;

                    // Only consider this a valid ground hit if the ground normal goes in the same direction as the character up
                    // and if the slope angle is lower than the character controller's limit
                    if (Vector3.Dot(hit.normal, transform.up) > 0f &&
                        IsNormalUnderSlopeLimit(m_GroundNormal))
                    {
                        IsGrounded = true;

                        // handle snapping to the ground
                        if (hit.distance > m_Controller.skinWidth)
                        {
                            m_Controller.Move(Vector3.down * hit.distance);
                        }
                    }
                }
            }
        }

        void HandleCharacterMovement()
        {
            // horizontal character rotation
            {
                // rotate the transform with the input speed around its local Y axis
                transform.Rotate(
                    new Vector3(0f, (m_InputHandler.GetLookInputsHorizontal() * RotationSpeed * RotationMultiplier),
                        0f), Space.Self);
            }

            // vertical camera rotation
            {
                // add vertical inputs to the camera's vertical angle
                m_CameraVerticalAngle += m_InputHandler.GetLookInputsVertical() * RotationSpeed * RotationMultiplier;

                // limit the camera's vertical angle to min/max
                m_CameraVerticalAngle = Mathf.Clamp(m_CameraVerticalAngle, -89f, 89f);

                // apply the vertical angle as a local rotation to the camera pivot along its right axis (makes it pivot up and down)
                CameraPivot.localEulerAngles = new Vector3(m_CameraVerticalAngle, 0, 0);
            }

            // character movement handling
            bool isSprinting = m_InputHandler.GetSprintInputHeld();
            if (IsMovementOverridden && IsSliding)
            {
                StopSlide();
            }

            if (!IsMovementOverridden)
            {
                if (isSprinting && !IsSliding)
                {
                    isSprinting = SetCrouchingState(false, false);
                }

                float speedModifier = isSprinting ? SprintSpeedModifier : 1f;

                // converts move input to a worldspace vector based on our character's transform orientation
                Vector3 worldspaceMoveInput = transform.TransformVector(m_InputHandler.GetMoveInput());

                // handle grounded movement
                if (IsGrounded)
                {
                    // calculate the desired velocity from inputs, max speed, and current slope
                    Vector3 targetVelocity = worldspaceMoveInput * MaxSpeedOnGround * speedModifier;
                    // reduce speed if crouching by crouch speed ratio
                    if (IsCrouching)
                        targetVelocity *= MaxSpeedCrouchedRatio;
                    if (m_WeaponsManager.IsAiming)
                        targetVelocity *= m_AimingMoveSpeedMultiplier;
                    targetVelocity = GetDirectionReorientedOnSlope(targetVelocity.normalized, m_GroundNormal) *
                                     targetVelocity.magnitude;

                    if (IsSliding)
                    {
                        UpdateSlide(worldspaceMoveInput);
                    }
                    else
                    {
                        // smoothly interpolate between our current velocity and the target velocity based on acceleration speed
                        CharacterVelocity = Vector3.Lerp(CharacterVelocity, targetVelocity,
                            MovementSharpnessOnGround * Time.deltaTime);
                    }

                    // jumping
                    if (IsGrounded && m_InputHandler.GetJumpInputDown())
                    {
                        // force the crouch state to false
                        if (SetCrouchingState(false, false))
                        {
                            // slide-jump keeps the slide's horizontal momentum
                            IsSliding = false;

                            // start by canceling out the vertical component of our velocity
                            CharacterVelocity = new Vector3(CharacterVelocity.x, 0f, CharacterVelocity.z);

                            // then, add the jumpSpeed value upwards
                            CharacterVelocity += Vector3.up * JumpForce;

                            // play sound
                            AudioSource.PlayOneShot(JumpSfx);

                            // remember last time we jumped because we need to prevent snapping to ground for a short time
                            m_LastTimeJumped = Time.time;
                            HasJumpedThisFrame = true;

                            // Force grounding to false
                            IsGrounded = false;
                            m_GroundNormal = Vector3.up;
                        }
                    }

                    // footsteps sound
                    float chosenFootstepSfxFrequency =
                        (isSprinting ? FootstepSfxFrequencyWhileSprinting : FootstepSfxFrequency);
                    if (!IsSliding && m_FootstepDistanceCounter >= 1f / chosenFootstepSfxFrequency)
                    {
                        m_FootstepDistanceCounter = 0f;
                        AudioSource.PlayOneShot(FootstepSfx);
                    }

                    // keep track of distance traveled for footsteps sound
                    m_FootstepDistanceCounter += CharacterVelocity.magnitude * Time.deltaTime;
                }
                // handle air movement
                else
                {
                    // sliding off a ledge ends the slide (momentum is kept)
                    if (IsSliding)
                        StopSlide();

                    // double jump
                    if (m_AirJumpsLeft > 0 && m_JumpConsumedFrame != Time.frameCount &&
                        m_InputHandler.GetJumpInputDown())
                    {
                        DoAirJump(worldspaceMoveInput);
                    }

                    // add air acceleration
                    CharacterVelocity += worldspaceMoveInput * AccelerationSpeedInAir * Time.deltaTime;

                    // limit air speed to a maximum, but only horizontally. Speed above the max (grapple
                    // momentum) decays smoothly instead of being clamped instantly.
                    float verticalVelocity = CharacterVelocity.y;
                    Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                    float maxAirSpeed = MaxSpeedInAir * speedModifier;
                    float horizontalSpeed = horizontalVelocity.magnitude;
                    if (horizontalSpeed > maxAirSpeed)
                    {
                        float decayedSpeed = Mathf.Max(maxAirSpeed,
                            horizontalSpeed - m_AirMomentumDecay * Time.deltaTime);
                        horizontalVelocity = horizontalVelocity / horizontalSpeed * decayedSpeed;
                    }
                    CharacterVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

                    // apply the gravity to the velocity
                    CharacterVelocity += Vector3.down * GravityDownForce * Time.deltaTime;
                }
            }

            // apply the final calculated velocity value as a character movement
            Vector3 capsuleBottomBeforeMove = GetCapsuleBottomHemisphere();
            Vector3 capsuleTopBeforeMove = GetCapsuleTopHemisphere(m_Controller.height);
            m_Controller.Move(CharacterVelocity * Time.deltaTime);

            // detect obstructions to adjust velocity accordingly
            m_LatestImpactSpeed = Vector3.zero;
            if (Physics.CapsuleCast(capsuleBottomBeforeMove, capsuleTopBeforeMove, m_Controller.radius,
                CharacterVelocity.normalized, out RaycastHit hit, CharacterVelocity.magnitude * Time.deltaTime, -1,
                QueryTriggerInteraction.Ignore))
            {
                // We remember the last impact speed because the fall damage logic might need it
                m_LatestImpactSpeed = CharacterVelocity;

                CharacterVelocity = Vector3.ProjectOnPlane(CharacterVelocity, hit.normal);
            }
        }

        void DoAirJump(Vector3 worldspaceMoveInput)
        {
            m_AirJumpsLeft--;

            Vector3 horizontal = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
            Vector3 inputDir = Vector3.ProjectOnPlane(worldspaceMoveInput, Vector3.up);
            if (inputDir.sqrMagnitude > 0.01f)
            {
                // redirect momentum toward the input direction (Ghostrunner-style air control)
                float speed = Mathf.Max(horizontal.magnitude, m_AirJumpMinHorizontalSpeed);
                horizontal = inputDir.normalized * speed;
            }

            CharacterVelocity = horizontal + Vector3.up * m_AirJumpForce;
            m_LastTimeJumped = Time.time;
            HasJumpedThisFrame = true;
            if (AudioSource && JumpSfx)
                AudioSource.PlayOneShot(JumpSfx);
        }

        bool CanStartSlide()
        {
            if (!m_SlideEnabled || !IsGrounded || IsMovementOverridden)
                return false;

            float horizontalSpeed = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up).magnitude;
            return horizontalSpeed >= m_SlideMinStartSpeed;
        }

        void StartSlide()
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
            Vector3 dir = horizontal.sqrMagnitude > 0.01f ? horizontal.normalized : transform.forward;
            float speed = Mathf.Min(m_SlideMaxSpeed, horizontal.magnitude + m_SlideBoost);

            IsSliding = true;
            m_SlideStartTime = Time.time;
            SetCrouchingState(true, true);
            CharacterVelocity = GetDirectionReorientedOnSlope(dir, m_GroundNormal) * speed;
        }

        void StopSlide()
        {
            if (!IsSliding)
                return;

            IsSliding = false;
            // stand up if there is room, otherwise stay crouched
            SetCrouchingState(false, false);
        }

        void UpdateSlide(Vector3 worldspaceMoveInput)
        {
            float dt = Time.deltaTime;
            Vector3 along = Vector3.ProjectOnPlane(CharacterVelocity, m_GroundNormal);
            float speed = along.magnitude;
            Vector3 dir = speed > 0.01f ? along / speed : transform.forward;

            // gravity along the slope: accelerate downhill, decelerate uphill
            Vector3 slopeGravity = Vector3.ProjectOnPlane(Vector3.down * GravityDownForce, m_GroundNormal);
            Vector3 newVelocity = dir * speed + slopeGravity * dt;
            speed = newVelocity.magnitude;
            if (speed > 0.0001f)
                dir = newVelocity / speed;

            // friction
            speed = Mathf.Max(0f, speed - m_SlideFriction * dt);

            // light steering with WASD
            Vector3 steer = Vector3.ProjectOnPlane(worldspaceMoveInput, m_GroundNormal);
            if (steer.sqrMagnitude > 0.01f)
            {
                dir = Vector3.RotateTowards(dir, steer.normalized, m_SlideSteering * Mathf.Deg2Rad * dt, 0f);
            }

            speed = Mathf.Min(speed, m_SlideMaxSpeed);
            CharacterVelocity = dir * speed;

            if (speed < m_SlideEndSpeed || Time.time > m_SlideStartTime + m_SlideMaxDuration)
            {
                StopSlide();
            }
        }

        // Returns true if the slope angle represented by the given normal is under the slope angle limit of the character controller
        bool IsNormalUnderSlopeLimit(Vector3 normal)
        {
            return Vector3.Angle(transform.up, normal) <= m_Controller.slopeLimit;
        }

        // Gets the center point of the bottom hemisphere of the character controller capsule    
        Vector3 GetCapsuleBottomHemisphere()
        {
            return transform.position + (transform.up * m_Controller.radius);
        }

        // Gets the center point of the top hemisphere of the character controller capsule    
        Vector3 GetCapsuleTopHemisphere(float atHeight)
        {
            return transform.position + (transform.up * (atHeight - m_Controller.radius));
        }

        // Gets a reoriented direction that is tangent to a given slope
        public Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal)
        {
            Vector3 directionRight = Vector3.Cross(direction, transform.up);
            return Vector3.Cross(slopeNormal, directionRight).normalized;
        }

        void UpdateCharacterHeight(bool force)
        {
            // Update height instantly
            if (force)
            {
                m_Controller.height = m_TargetCharacterHeight;
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                CameraPivot.localPosition = Vector3.up * m_TargetCharacterHeight * CameraHeightRatio;
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
            // Update smooth height
            else if (m_Controller.height != m_TargetCharacterHeight)
            {
                // resize the capsule and adjust camera position
                m_Controller.height = Mathf.Lerp(m_Controller.height, m_TargetCharacterHeight,
                    CrouchingSharpness * Time.deltaTime);
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                CameraPivot.localPosition = Vector3.Lerp(CameraPivot.localPosition,
                    Vector3.up * m_TargetCharacterHeight * CameraHeightRatio, CrouchingSharpness * Time.deltaTime);
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
        }

        // returns false if there was an obstruction
        bool SetCrouchingState(bool crouched, bool ignoreObstructions)
        {
            // set appropriate heights
            if (crouched)
            {
                m_TargetCharacterHeight = CapsuleHeightCrouching;
            }
            else
            {
                // Detect obstructions
                if (!ignoreObstructions)
                {
                    Collider[] standingOverlaps = Physics.OverlapCapsule(
                        GetCapsuleBottomHemisphere(),
                        GetCapsuleTopHemisphere(CapsuleHeightStanding),
                        m_Controller.radius,
                        -1,
                        QueryTriggerInteraction.Ignore);
                    foreach (Collider c in standingOverlaps)
                    {
                        if (c != m_Controller)
                        {
                            return false;
                        }
                    }
                }

                m_TargetCharacterHeight = CapsuleHeightStanding;
            }

            if (OnStanceChanged != null)
            {
                OnStanceChanged.Invoke(crouched);
            }

            IsCrouching = crouched;
            return true;
        }
    }
}