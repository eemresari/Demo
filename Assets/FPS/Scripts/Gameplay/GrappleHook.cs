using System;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Grapple (one press):
    ///  - on a GrapplePoint block: pulls the player along an arc over the block's near edge and lands them on top of it
    ///  - on an enemy: pulls the player right in front of the enemy (follows it if it moves), ready for a sword strike
    /// Press Jump during the pull to let go early with a hop.
    /// Runs before PlayerCharacterController so it can take over the velocity for the frame.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerInputHandler), typeof(ThirdPersonCameraRig))]
    public class GrappleHook : MonoBehaviour
    {
        public enum GrappleState
        {
            Idle,
            Firing,
            Pulling,
        }

        public enum TargetKind
        {
            None,
            Block,
            Enemy,
        }

        [Header("Targeting")]
        [SerializeField] private float m_MaxRange = 38f;

        [Tooltip("Targets within this angle of the crosshair can be grappled (closest to center wins)")]
        [SerializeField] private float m_AimAssistAngle = 15f;

        [SerializeField] private float m_MinRange = 3f;

        [Tooltip("Allow grappling to enemies")]
        [SerializeField] private bool m_CanTargetEnemies = true;

        [SerializeField] private LayerMask m_LineOfSightLayers = -1;
        [SerializeField] private float m_Cooldown = 0.25f;

        [Header("Pull")]
        [Tooltip("Time for the hook to fly to the target before the pull starts")]
        [SerializeField] private float m_HookTravelTime = 0.06f;

        [Tooltip("Average pull speed (m/s). The pull is fastest at the start and slows down on arrival.")]
        [SerializeField] private float m_PullSpeed = 30f;

        [SerializeField] private float m_MinPullDuration = 0.2f;

        [Tooltip("0 = constant speed, 1 = strong ease-out (fast start, soft landing)")]
        [Range(0f, 1f)] [SerializeField] private float m_EaseOut = 0.6f;

        [Tooltip("Height above the block's near edge that the arc passes through")]
        [SerializeField] private float m_EdgeClearance = 2f;

        [Tooltip("How far outside the block's near edge the arc's control point is (lets you come up from below the block)")]
        [SerializeField] private float m_EdgeOutset = 1.5f;

        [Tooltip("Lands the player this far above the block top (small gap avoids snagging)")]
        [SerializeField] private float m_LandingGap = 0.05f;

        [Tooltip("Pull is cancelled if blocked for this long")]
        [SerializeField] private float m_StuckTime = 0.15f;

        [Tooltip("Upward hop when cancelling the pull with Jump")]
        [SerializeField] private float m_JumpCancelUpBoost = 7f;

        [Header("Enemy Pull")]
        [Tooltip("Gap kept between the player and the enemy's body on arrival")]
        [SerializeField] private float m_EnemyStopDistance = 1.2f;

        [SerializeField] private float m_EnemyArcHeight = 1f;

        [Header("Visuals")]
        [SerializeField] private LineRenderer m_Rope;

        [Tooltip("Where the rope leaves the player (hand). Defaults to the camera pivot.")]
        [SerializeField] private Transform m_RopeOrigin;

        [Header("Feedback")]
        [SerializeField] private AudioClip m_AttachSfx;
        [SerializeField] private float m_AttachShake = 0.2f;

        public GrappleState State { get; private set; }
        public bool IsActive => State != GrappleState.Idle;
        public bool IsPulling => State == GrappleState.Pulling;

        /// <summary>Is there a valid target (only computed while idle).</summary>
        public bool HasTarget => CurrentTargetKind != TargetKind.None;

        public TargetKind CurrentTargetKind { get; private set; }
        public Vector3 TargetPoint { get; private set; }
        public bool TargetIsEnemy => CurrentTargetKind == TargetKind.Enemy;

        /// <summary>Kept for UI compatibility: true when targeting a block.</summary>
        public bool TargetIsAssisted => CurrentTargetKind == TargetKind.Block;

        /// <summary>Raised when a pull ends (arrival, jump cancel, blocked).</summary>
        public event Action OnPullFinished;

        PlayerCharacterController m_Controller;
        PlayerInputHandler m_Input;
        ThirdPersonCameraRig m_CameraRig;
        CharacterController m_CharacterController;
        AudioSource m_AudioSource;
        Actor m_Actor;
        ActorsManager m_ActorsManager;

        // current target candidates (idle)
        GrapplePoint m_TargetBlock;
        Actor m_TargetEnemy;

        // locked target (firing / pulling)
        TargetKind m_LockedKind;
        GrapplePoint m_LockedBlock;
        Actor m_LockedEnemy;
        float m_LockedEnemyRadius;
        Vector3 m_LastEnemyPoint;

        float m_StateStartTime;
        float m_LastDetachTime = -10f;
        float m_PullDuration;
        Vector3 m_P0;
        Vector3 m_P1;
        Vector3 m_ApproachDirection;
        float m_StuckTimer;
        Vector3 m_LastPosition;
        float m_ExpectedSpeed;

        Vector3 PlayerCenter => transform.TransformPoint(m_CharacterController.center);
        float CenterHeight => m_CharacterController.center.y;

        void Awake()
        {
            m_Controller = GetComponent<PlayerCharacterController>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_CameraRig = GetComponent<ThirdPersonCameraRig>();
            m_CharacterController = GetComponent<CharacterController>();
            m_AudioSource = GetComponent<AudioSource>();
            m_Actor = GetComponent<Actor>();
            m_ActorsManager = FindAnyObjectByType<ActorsManager>();

            if (m_Rope != null)
                m_Rope.enabled = false;
        }

        void Update()
        {
            if (m_Controller.IsDead)
            {
                if (IsActive)
                    Detach();
                CurrentTargetKind = TargetKind.None;
                return;
            }

            switch (State)
            {
                case GrappleState.Idle:
                    UpdateTargeting();
                    if (m_Input.GetGrappleInputDown() && HasTarget && Time.time >= m_LastDetachTime + m_Cooldown)
                    {
                        Fire();
                    }

                    break;

                case GrappleState.Firing:
                    if (!IsLockedTargetValid())
                    {
                        Detach();
                        break;
                    }

                    if (Time.time >= m_StateStartTime + m_HookTravelTime)
                    {
                        StartPull();
                        UpdatePull();
                    }

                    break;

                case GrappleState.Pulling:
                    if (m_Input.GetJumpInputDown())
                    {
                        m_Controller.ConsumeJumpInputThisFrame();
                        Vector3 v = m_Controller.CharacterVelocity * 0.5f;
                        v.y = Mathf.Max(v.y, 0f) + m_JumpCancelUpBoost;
                        FinishPull(v);
                        break;
                    }

                    UpdatePull();
                    break;
            }
        }

        void LateUpdate()
        {
            UpdateRopeVisual();
        }

        // ---------------------------------------------------------------- targeting

        void UpdateTargeting()
        {
            CurrentTargetKind = TargetKind.None;
            m_TargetBlock = null;
            m_TargetEnemy = null;

            Camera cam = m_CameraRig.Camera;
            if (cam == null)
                return;

            Vector3 camPos = cam.transform.position;
            Vector3 camForward = cam.transform.forward;
            Vector3 playerCenter = PlayerCenter;
            Vector3 rayOrigin = m_CameraRig.GetCrosshairRayOrigin();
            float bestAngle = m_AimAssistAngle;

            var points = GrapplePoint.All;
            for (int i = 0; i < points.Count; i++)
            {
                GrapplePoint gp = points[i];
                Vector3 aim = gp.AnchorPosition;
                float distance = Vector3.Distance(playerCenter, aim);
                if (distance > m_MaxRange || distance < m_MinRange)
                    continue;

                float angle = Vector3.Angle(camForward, aim - camPos);
                if (angle >= bestAngle || !HasLineOfSight(rayOrigin, aim, gp.transform))
                    continue;

                bestAngle = angle;
                CurrentTargetKind = TargetKind.Block;
                TargetPoint = aim;
                m_TargetBlock = gp;
                m_TargetEnemy = null;
            }

            if (!m_CanTargetEnemies || m_ActorsManager == null || m_Actor == null)
                return;

            foreach (Actor actor in m_ActorsManager.Actors)
            {
                if (actor == null || actor == m_Actor || actor.Affiliation == m_Actor.Affiliation ||
                    actor.AimPoint == null)
                    continue;

                Vector3 aim = actor.AimPoint.position;
                float distance = Vector3.Distance(playerCenter, aim);
                if (distance > m_MaxRange || distance < m_MinRange)
                    continue;

                float angle = Vector3.Angle(camForward, aim - camPos);
                if (angle >= bestAngle || !HasLineOfSight(rayOrigin, aim, actor.transform))
                    continue;

                bestAngle = angle;
                CurrentTargetKind = TargetKind.Enemy;
                TargetPoint = aim;
                m_TargetEnemy = actor;
                m_TargetBlock = null;
            }
        }

        bool HasLineOfSight(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.01f)
                return true;

            RaycastHit[] hits = Physics.RaycastAll(from, dir / distance, distance, m_LineOfSightLayers,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (m_CameraRig.IsOwnCollider(hit.collider))
                    continue;
                if (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))
                    continue;
                // tolerate hits right next to the target point
                if (distance - hit.distance < 0.75f)
                    continue;
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------- pull

        bool IsLockedTargetValid()
        {
            if (m_LockedKind == TargetKind.Block)
                return m_LockedBlock != null;
            return m_LockedEnemy != null;
        }

        void Fire()
        {
            m_LockedKind = CurrentTargetKind;
            m_LockedBlock = m_TargetBlock;
            m_LockedEnemy = m_TargetEnemy;
            m_LockedEnemyRadius = m_LockedEnemy != null ? EstimateRadius(m_LockedEnemy) : 0f;
            if (m_LockedEnemy != null)
                m_LastEnemyPoint = m_LockedEnemy.AimPoint.position;

            CurrentTargetKind = TargetKind.None;
            State = GrappleState.Firing;
            m_StateStartTime = Time.time;

            if (m_AttachSfx && m_AudioSource)
                m_AudioSource.PlayOneShot(m_AttachSfx);
        }

        static float EstimateRadius(Actor actor)
        {
            float radius = 0.5f;
            foreach (Collider c in actor.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger)
                    continue;
                radius = Mathf.Max(radius, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z));
            }

            return radius;
        }

        // end point of the path for the player's capsule center
        Vector3 ComputeEndPoint()
        {
            if (m_LockedKind == TargetKind.Block)
            {
                return m_LockedBlock.TopSurfaceCenter + Vector3.up * (CenterHeight + m_LandingGap);
            }

            if (m_LockedEnemy != null && m_LockedEnemy.AimPoint != null)
                m_LastEnemyPoint = m_LockedEnemy.AimPoint.position;

            Vector3 stop = m_LastEnemyPoint - m_ApproachDirection * (m_LockedEnemyRadius + m_EnemyStopDistance);
            stop.y = Mathf.Max(stop.y - 0.2f, m_LastEnemyPoint.y - 1f);
            return stop;
        }

        void StartPull()
        {
            State = GrappleState.Pulling;
            m_StateStartTime = Time.time;
            m_StuckTimer = 0f;
            m_ArrivePending = false;
            m_LastPosition = transform.position;
            m_P0 = PlayerCenter;

            Vector3 targetPos = m_LockedKind == TargetKind.Block ? m_LockedBlock.TopSurfaceCenter : m_LastEnemyPoint;
            Vector3 flat = Vector3.ProjectOnPlane(targetPos - m_P0, Vector3.up);
            m_ApproachDirection = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;

            Vector3 end = ComputeEndPoint();
            float length;
            if (m_LockedKind == TargetKind.Block)
            {
                BuildBlockPath(end);
                length = m_PathCumulative[m_PathCount - 1];
            }
            else
            {
                m_P1 = (m_P0 + end) * 0.5f + Vector3.up * m_EnemyArcHeight;
                length = Vector3.Distance(m_P0, m_P1) + Vector3.Distance(m_P1, end);
            }

            m_PullDuration = Mathf.Max(m_MinPullDuration, length / Mathf.Max(1f, m_PullSpeed));
            m_ExpectedSpeed = length / m_PullDuration;

            m_Controller.ForceUngrounded();
            m_CameraRig.AddShake(m_AttachShake);
        }

        // Block path = polyline whose segments all stay outside the block:
        //   [under the block: step sideways out from under it] -> [climb beside it, above the top edge] -> land on top center
        void BuildBlockPath(Vector3 end)
        {
            Bounds bounds = m_LockedBlock.WorldBounds;
            Vector3 top = m_LockedBlock.TopSurfaceCenter;
            float outDistance = m_LockedBlock.TopHalfExtent + m_CharacterController.radius + m_EdgeOutset;
            Vector3 outside = top - m_ApproachDirection * outDistance;

            m_PathCount = 0;
            AddPathPoint(m_P0);

            if (m_P0.y < end.y)
            {
                Vector3 fromCenter = Vector3.ProjectOnPlane(m_P0 - top, Vector3.up);
                bool underneath = fromCenter.magnitude < outDistance &&
                                  m_P0.y + CenterHeight < bounds.min.y + 0.1f;
                if (underneath)
                    AddPathPoint(new Vector3(outside.x, m_P0.y, outside.z));

                AddPathPoint(new Vector3(outside.x, top.y + CenterHeight + m_EdgeClearance, outside.z));
            }

            AddPathPoint(end);
        }

        void AddPathPoint(Vector3 point)
        {
            m_Path[m_PathCount] = point;
            m_PathCumulative[m_PathCount] = m_PathCount == 0
                ? 0f
                : m_PathCumulative[m_PathCount - 1] + Vector3.Distance(m_Path[m_PathCount - 1], point);
            m_PathCount++;
        }

        Vector3 EvaluatePath(float t)
        {
            float total = m_PathCumulative[m_PathCount - 1];
            float s = Mathf.Clamp01(t) * total;
            for (int i = 1; i < m_PathCount; i++)
            {
                if (s <= m_PathCumulative[i] || i == m_PathCount - 1)
                {
                    float segment = m_PathCumulative[i] - m_PathCumulative[i - 1];
                    float k = segment > 0.0001f ? (s - m_PathCumulative[i - 1]) / segment : 1f;
                    return Vector3.Lerp(m_Path[i - 1], m_Path[i], Mathf.Clamp01(k));
                }
            }

            return m_Path[m_PathCount - 1];
        }

        float EvaluateProgress(float u)
        {
            u = Mathf.Clamp01(u);
            float easeOut = 1f - (1f - u) * (1f - u);
            return Mathf.Lerp(u, easeOut, m_EaseOut);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float it = 1f - t;
            return it * it * a + 2f * it * t * b + t * t * c;
        }

        void UpdatePull()
        {
            if (!IsLockedTargetValid() && m_LockedKind == TargetKind.Block)
            {
                FinishPull(Vector3.zero);
                return;
            }

            // last frame moved us onto the end point: stop dead (standing on the block / in front of the enemy)
            if (m_ArrivePending)
            {
                FinishPull(Vector3.zero);
                return;
            }

            m_Controller.RequestMovementOverride();
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            float u = (Time.time - m_StateStartTime + dt) / m_PullDuration;
            Vector3 desired = m_LockedKind == TargetKind.Block
                ? EvaluatePath(EvaluateProgress(u))
                : Bezier(m_P0, m_P1, ComputeEndPoint(), EvaluateProgress(u));
            m_Controller.CharacterVelocity = (desired - PlayerCenter) / dt;

            if (u >= 1f)
                m_ArrivePending = true;

            // blocked by geometry
            float movedSpeed = (transform.position - m_LastPosition).magnitude / dt;
            m_StuckTimer = movedSpeed < m_ExpectedSpeed * 0.15f && Time.time > m_StateStartTime + 0.1f
                ? m_StuckTimer + dt
                : 0f;
            m_LastPosition = transform.position;

            if (m_StuckTimer > m_StuckTime)
            {
                FinishPull(Vector3.zero);
            }
        }

        bool m_ArrivePending;
        readonly Vector3[] m_Path = new Vector3[4];
        readonly float[] m_PathCumulative = new float[4];
        int m_PathCount;

        void FinishPull(Vector3 finalVelocity)
        {
            m_Controller.CharacterVelocity = finalVelocity;
            m_Controller.RefillAirJumps();
            Detach();
            OnPullFinished?.Invoke();
        }

        void Detach()
        {
            State = GrappleState.Idle;
            m_LockedBlock = null;
            m_LockedEnemy = null;
            m_LockedKind = TargetKind.None;
            m_ArrivePending = false;
            m_LastDetachTime = Time.time;
        }

        // ---------------------------------------------------------------- visuals

        Vector3 RopeEndPoint()
        {
            if (m_LockedKind == TargetKind.Block && m_LockedBlock != null)
                return m_LockedBlock.TopSurfaceCenter;
            if (m_LockedEnemy != null && m_LockedEnemy.AimPoint != null)
                return m_LockedEnemy.AimPoint.position;
            return m_LastEnemyPoint;
        }

        void UpdateRopeVisual()
        {
            if (m_Rope == null)
                return;

            if (!IsActive)
            {
                m_Rope.enabled = false;
                return;
            }

            Vector3 origin = m_RopeOrigin != null ? m_RopeOrigin.position : m_Controller.CameraPivot.position;
            Vector3 end = RopeEndPoint();
            float t = State == GrappleState.Firing && m_HookTravelTime > 0f
                ? Mathf.Clamp01((Time.time - m_StateStartTime) / m_HookTravelTime)
                : 1f;

            m_Rope.enabled = true;
            m_Rope.positionCount = 2;
            m_Rope.SetPosition(0, origin);
            m_Rope.SetPosition(1, Vector3.Lerp(origin, end, t));
        }
    }
}
