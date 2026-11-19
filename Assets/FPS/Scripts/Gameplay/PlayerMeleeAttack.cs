using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Sword melee on the Attack button: 3-hit combo, lunge toward the nearest enemy in front,
    /// damage scaled by player speed (grapple into a strike!), hit-stop and camera shake.
    /// The sword visual is a placeholder pivot that is rotated through an arc by code.
    /// Runs before PlayerCharacterController so the lunge can take over the velocity.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerInputHandler))]
    public class PlayerMeleeAttack : MonoBehaviour
    {
        [System.Serializable]
        public struct ComboStep
        {
            public float Damage;

            [Tooltip("Time before the hit lands")]
            public float Windup;

            [Tooltip("Time after the hit before the next action")]
            public float Recovery;

            public float HitStop;
            public float Shake;

            [Tooltip("Sword pivot local euler angles at swing start / end")]
            public Vector3 ArcStart;

            public Vector3 ArcEnd;
        }

        [Header("Combo")]
        [SerializeField] private ComboStep[] m_Combo =
        {
            new ComboStep
            {
                Damage = 25f, Windup = 0.07f, Recovery = 0.18f, HitStop = 0.045f, Shake = 0.2f,
                ArcStart = new Vector3(10f, -100f, -20f), ArcEnd = new Vector3(10f, 65f, -20f)
            },
            new ComboStep
            {
                Damage = 30f, Windup = 0.07f, Recovery = 0.18f, HitStop = 0.045f, Shake = 0.2f,
                ArcStart = new Vector3(10f, 75f, 20f), ArcEnd = new Vector3(10f, -90f, 20f)
            },
            new ComboStep
            {
                Damage = 55f, Windup = 0.1f, Recovery = 0.32f, HitStop = 0.09f, Shake = 0.45f,
                ArcStart = new Vector3(-100f, 0f, 0f), ArcEnd = new Vector3(65f, 0f, 0f)
            },
        };

        [Tooltip("Time after a swing ends during which the next click continues the combo")]
        [SerializeField] private float m_ComboWindow = 0.35f;

        [Tooltip("Clicks during the swing are buffered for the next combo step")]
        [SerializeField] private bool m_BufferInput = true;

        [Header("Hit Detection")]
        [SerializeField] private float m_HitRadius = 1.7f;

        [Tooltip("Distance in front of the chest where the hit sphere is centered")]
        [SerializeField] private float m_HitForwardOffset = 1.6f;

        [SerializeField] private LayerMask m_HitLayers = -1;

        [Header("Lunge")]
        [Tooltip("Enemies within this distance and cone get lunged at")]
        [SerializeField] private float m_LungeRange = 8f;

        [SerializeField] private float m_LungeConeAngle = 45f;
        [SerializeField] private float m_LungeSpeed = 24f;

        [Tooltip("Lunge stops this far from the target's center")]
        [SerializeField] private float m_LungeStopDistance = 1.6f;

        [Tooltip("Small forward step on the ground when there is no target")]
        [SerializeField] private float m_StepSpeed = 7f;

        [Header("Momentum")]
        [Tooltip("Damage multiplier = 1 + speed / this. At this speed damage is doubled.")]
        [SerializeField] private float m_SpeedForDoubleDamage = 30f;

        [SerializeField] private float m_MaxMomentumMultiplier = 2.5f;

        [Header("Visuals & Audio")]
        [Tooltip("Pivot that holds the sword mesh; rotated through each arc")]
        [SerializeField] private Transform m_SwordPivot;

        [SerializeField] private Vector3 m_SwordRestEuler = new Vector3(35f, 25f, -15f);
        [SerializeField] private TrailRenderer m_SwordTrail;
        [SerializeField] private GameObject m_HitVfx;
        [SerializeField] private float m_HitVfxLifetime = 1.5f;
        [SerializeField] private AudioClip m_SwingSfx;
        [SerializeField] private AudioClip m_HitSfx;

        public bool IsAttacking => m_State != State.Idle;

        enum State
        {
            Idle,
            Windup,
            Recovery,
        }

        PlayerCharacterController m_Controller;
        PlayerInputHandler m_Input;
        PlayerWeaponsManager m_WeaponsManager;
        ThirdPersonCameraRig m_CameraRig;
        GrappleHook m_Grapple;
        CharacterController m_CharacterController;
        Actor m_Actor;
        Health m_Health;
        AudioSource m_AudioSource;
        ActorsManager m_ActorsManager;

        State m_State;
        int m_ComboIndex;
        float m_StateTime;
        float m_LastSwingEndTime = -10f;
        bool m_Buffered;
        Actor m_LungeTarget;
        bool m_Lunging;
        float m_SpeedAtSwingStart;
        readonly Collider[] m_Overlaps = new Collider[64];
        readonly HashSet<Health> m_HitThisSwing = new HashSet<Health>();

        Vector3 Chest => transform.TransformPoint(m_CharacterController.center + Vector3.up * 0.3f);

        void Awake()
        {
            m_Controller = GetComponent<PlayerCharacterController>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            m_CameraRig = GetComponent<ThirdPersonCameraRig>();
            m_Grapple = GetComponent<GrappleHook>();
            m_CharacterController = GetComponent<CharacterController>();
            m_Actor = GetComponent<Actor>();
            m_Health = GetComponent<Health>();
            m_AudioSource = GetComponent<AudioSource>();
            m_ActorsManager = FindAnyObjectByType<ActorsManager>();

            if (m_SwordTrail)
                m_SwordTrail.emitting = false;
            if (m_SwordPivot)
                m_SwordPivot.localRotation = Quaternion.Euler(m_SwordRestEuler);
        }

        void Update()
        {
            if (m_Controller.IsDead)
            {
                m_State = State.Idle;
                SetShootingBlocked(false);
                return;
            }

            bool attackPressed = m_Input.GetMeleeInputDown();
            m_StateTime += Time.deltaTime;

            switch (m_State)
            {
                case State.Idle:
                    if (attackPressed)
                    {
                        bool continueCombo = Time.time <= m_LastSwingEndTime + m_ComboWindow;
                        StartSwing(continueCombo ? (m_ComboIndex + 1) % m_Combo.Length : 0);
                    }

                    break;

                case State.Windup:
                {
                    ComboStep step = m_Combo[m_ComboIndex];
                    if (attackPressed && m_BufferInput)
                        m_Buffered = true;

                    UpdateLunge();
                    AnimateSword(Mathf.Clamp01(m_StateTime / step.Windup) * 0.6f);

                    if (m_StateTime >= step.Windup)
                    {
                        EndLunge();
                        DoHit(step);
                        m_State = State.Recovery;
                        m_StateTime = 0f;
                    }

                    break;
                }

                case State.Recovery:
                {
                    ComboStep step = m_Combo[m_ComboIndex];
                    if (attackPressed && m_BufferInput)
                        m_Buffered = true;

                    float t = step.Recovery > 0f ? Mathf.Clamp01(m_StateTime / step.Recovery) : 1f;
                    // fast follow-through for the first part of recovery, then return to rest
                    if (t < 0.35f)
                        AnimateSword(0.6f + 0.4f * (t / 0.35f));
                    else
                        ReturnSwordToRest((t - 0.35f) / 0.65f);

                    if (m_StateTime >= step.Recovery)
                    {
                        m_State = State.Idle;
                        m_LastSwingEndTime = Time.time;
                        if (m_SwordTrail)
                            m_SwordTrail.emitting = false;

                        if (m_Buffered)
                            StartSwing((m_ComboIndex + 1) % m_Combo.Length);
                    }

                    break;
                }
            }

            SetShootingBlocked(m_State != State.Idle);
        }

        void SetShootingBlocked(bool blocked)
        {
            if (m_WeaponsManager)
                m_WeaponsManager.IsShootingBlocked = blocked;
        }

        void StartSwing(int comboIndex)
        {
            m_ComboIndex = Mathf.Clamp(comboIndex, 0, m_Combo.Length - 1);
            m_State = State.Windup;
            m_StateTime = 0f;
            m_Buffered = false;
            m_HitThisSwing.Clear();
            m_SpeedAtSwingStart = m_Controller.CharacterVelocity.magnitude;

            if (m_SwordTrail)
            {
                m_SwordTrail.Clear();
                m_SwordTrail.emitting = true;
            }

            if (m_SwingSfx && m_AudioSource)
                m_AudioSource.PlayOneShot(m_SwingSfx);

            // grapple has priority over the lunge (keeps swing / zip momentum)
            bool grappling = m_Grapple != null && m_Grapple.IsActive;
            m_LungeTarget = grappling ? null : FindLungeTarget();
            m_Lunging = !grappling && (m_LungeTarget != null || m_Controller.IsGrounded);
            AnimateSword(0f);
        }

        Actor FindLungeTarget()
        {
            if (m_ActorsManager == null || m_Actor == null)
                return null;

            Transform cam = m_CameraRig != null ? m_CameraRig.Camera.transform : transform;
            Vector3 lookDir = cam.forward;
            Vector3 chest = Chest;

            Actor best = null;
            float bestScore = float.MaxValue;
            foreach (Actor actor in m_ActorsManager.Actors)
            {
                if (actor == null || actor == m_Actor || actor.Affiliation == m_Actor.Affiliation ||
                    actor.AimPoint == null)
                    continue;

                Vector3 toTarget = actor.AimPoint.position - chest;
                float distance = toTarget.magnitude;
                if (distance > m_LungeRange)
                    continue;

                float angle = Vector3.Angle(lookDir, toTarget);
                if (angle > m_LungeConeAngle)
                    continue;

                float score = distance * (1f + angle / m_LungeConeAngle);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = actor;
                }
            }

            return best;
        }

        void UpdateLunge()
        {
            if (!m_Lunging)
                return;

            m_Controller.RequestMovementOverride();
            Vector3 velocity;
            if (m_LungeTarget != null && m_LungeTarget.AimPoint != null)
            {
                Vector3 toTarget = m_LungeTarget.AimPoint.position - Chest;
                float distance = toTarget.magnitude;
                if (distance <= m_LungeStopDistance)
                {
                    velocity = Vector3.zero;
                }
                else
                {
                    // keep grapple / fall momentum if we were already faster than the lunge
                    float speed = Mathf.Max(m_LungeSpeed, m_SpeedAtSwingStart);
                    speed = Mathf.Min(speed, (distance - m_LungeStopDistance) / Mathf.Max(Time.deltaTime, 0.0001f));
                    velocity = toTarget / distance * speed;
                }
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                velocity = forward * m_StepSpeed;
            }

            m_Controller.CharacterVelocity = velocity;
        }

        void EndLunge()
        {
            if (!m_Lunging)
                return;

            m_Lunging = false;
            // stick to the target instead of flying past it
            if (m_LungeTarget != null)
                m_Controller.CharacterVelocity *= 0.2f;
        }

        void DoHit(ComboStep step)
        {
            Transform cam = m_CameraRig != null ? m_CameraRig.Camera.transform : transform;
            Vector3 chest = Chest;
            Vector3 hitDir = m_LungeTarget != null && m_LungeTarget.AimPoint != null
                ? (m_LungeTarget.AimPoint.position - chest).normalized
                : cam.forward;
            Vector3 center = chest + hitDir * m_HitForwardOffset;

            float speed = Mathf.Max(m_SpeedAtSwingStart, m_Controller.CharacterVelocity.magnitude);
            float momentumMultiplier = Mathf.Min(m_MaxMomentumMultiplier, 1f + speed / m_SpeedForDoubleDamage);
            float damage = step.Damage * momentumMultiplier;

            int count = Physics.OverlapSphereNonAlloc(center, m_HitRadius, m_Overlaps, m_HitLayers,
                QueryTriggerInteraction.Collide);

            bool hitSomething = false;
            Vector3 vfxPoint = center;
            for (int i = 0; i < count; i++)
            {
                Collider col = m_Overlaps[i];
                Damageable damageable = col.GetComponent<Damageable>();
                if (damageable == null)
                    damageable = col.GetComponentInParent<Damageable>();
                if (damageable == null || damageable.Health == null || damageable.Health == m_Health)
                    continue;

                Actor targetActor = damageable.Health.GetComponentInParent<Actor>();
                if (targetActor != null && m_Actor != null && targetActor.Affiliation == m_Actor.Affiliation)
                    continue;

                if (!m_HitThisSwing.Add(damageable.Health))
                    continue;

                damageable.InflictDamage(damage, false, gameObject);
                if (!hitSomething)
                    vfxPoint = col.ClosestPoint(chest);
                hitSomething = true;
            }

            if (!hitSomething)
                return;

            float momentumBonus = momentumMultiplier - 1f;
            HitStop.Request(step.HitStop * (1f + momentumBonus));
            if (m_CameraRig)
                m_CameraRig.AddShake(step.Shake * (1f + momentumBonus));

            if (m_HitVfx)
            {
                GameObject vfx = Instantiate(m_HitVfx, vfxPoint, Quaternion.LookRotation(-hitDir));
                Destroy(vfx, m_HitVfxLifetime);
            }

            if (m_HitSfx)
                AudioUtility.CreateSFX(m_HitSfx, vfxPoint, AudioUtility.AudioGroups.Impact, 1f, 3f);
        }

        void AnimateSword(float t)
        {
            if (!m_SwordPivot)
                return;

            ComboStep step = m_Combo[m_ComboIndex];
            // ease-out for a snappy swing
            float eased = 1f - (1f - t) * (1f - t);
            m_SwordPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(step.ArcStart),
                Quaternion.Euler(step.ArcEnd), eased);
        }

        void ReturnSwordToRest(float t)
        {
            if (!m_SwordPivot)
                return;

            ComboStep step = m_Combo[m_ComboIndex];
            m_SwordPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(step.ArcEnd),
                Quaternion.Euler(m_SwordRestEuler), Mathf.Clamp01(t));
        }

        void OnDrawGizmosSelected()
        {
            if (!m_CharacterController)
                m_CharacterController = GetComponent<CharacterController>();
            if (!m_CharacterController)
                return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
            Vector3 chest = transform.TransformPoint(m_CharacterController.center + Vector3.up * 0.3f);
            Gizmos.DrawWireSphere(chest + transform.forward * m_HitForwardOffset, m_HitRadius);
        }
    }
}
