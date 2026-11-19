using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Procedural animation for the primitive humanoid player body (no Animator / clips needed).
    /// Reads movement state from PlayerCharacterController, PlayerAirDash and PlayerMeleeAttack and poses the limbs:
    /// idle, run, crouch, slide, jump, fall, double-jump flip, dash-aim coil and dash "superman" pose.
    /// Also leaves glowing afterimages during dashes and air jumps so the moves are easy to read.
    /// Rotation convention for limbs hanging down: -X swings forward, +X swings backward.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlayerCharacterController))]
    public class PlayerBodyAnimator : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Root of the body model, pivot at the hips. Flips and dash orientation rotate this.")]
        [SerializeField] private Transform m_Model;
        [SerializeField] private Transform m_Spine;
        [SerializeField] private Transform m_Head;
        [SerializeField] private Transform m_ShoulderL;
        [SerializeField] private Transform m_ShoulderR;
        [SerializeField] private Transform m_ElbowL;
        [SerializeField] private Transform m_ElbowR;
        [SerializeField] private Transform m_HipL;
        [SerializeField] private Transform m_HipR;
        [SerializeField] private Transform m_KneeL;
        [SerializeField] private Transform m_KneeR;

        [Header("Locomotion")]
        [Tooltip("Meters travelled per half stride (smaller = faster leg cycle)")]
        [SerializeField] private float m_StrideLength = 1.1f;
        [Tooltip("Horizontal speed at which the run cycle is at full amplitude")]
        [SerializeField] private float m_FullStrideSpeed = 9f;
        [SerializeField] private float m_LegSwing = 45f;
        [SerializeField] private float m_ArmSwing = 40f;
        [Tooltip("How fast limbs blend toward the target pose")]
        [SerializeField] private float m_PoseSharpness = 18f;

        [Header("Double Jump Flip")]
        [SerializeField] private float m_FlipDuration = 0.5f;
        [Tooltip("Full turns per air jump")]
        [SerializeField] private int m_FlipTurns = 1;

        [Header("Dash")]
        [Tooltip("How fast the body turns to face the dash direction")]
        [SerializeField] private float m_OrientSharpness = 25f;
        [Tooltip("Seconds the superman pose is held after the dash ends")]
        [SerializeField] private float m_DashPoseHold = 0.15f;

        [Header("Afterimages")]
        [Tooltip("Transparent material used for the dash / air jump afterimages. Leave empty to disable them.")]
        [SerializeField] private Material m_GhostMaterial;
        [SerializeField] private Color m_GhostColor = new Color(0.3f, 1f, 0.9f, 0.6f);
        [SerializeField] private float m_GhostInterval = 0.03f;
        [SerializeField] private float m_GhostLifetime = 0.35f;
        [Tooltip("Seconds of afterimages emitted at the start of an air jump")]
        [SerializeField] private float m_AirJumpGhostTime = 0.2f;

        struct Pose
        {
            public float HipsY, SpineX, SpineZ, HeadX;
            public Vector2 ShoulderL, ShoulderR; // x = pitch, y = roll
            public float ElbowL, ElbowR, HipL, HipR, KneeL, KneeR;

            public static Pose Lerp(Pose a, Pose b, float t)
            {
                return new Pose
                {
                    HipsY = Mathf.Lerp(a.HipsY, b.HipsY, t),
                    SpineX = Mathf.Lerp(a.SpineX, b.SpineX, t),
                    SpineZ = Mathf.Lerp(a.SpineZ, b.SpineZ, t),
                    HeadX = Mathf.Lerp(a.HeadX, b.HeadX, t),
                    ShoulderL = Vector2.Lerp(a.ShoulderL, b.ShoulderL, t),
                    ShoulderR = Vector2.Lerp(a.ShoulderR, b.ShoulderR, t),
                    ElbowL = Mathf.Lerp(a.ElbowL, b.ElbowL, t),
                    ElbowR = Mathf.Lerp(a.ElbowR, b.ElbowR, t),
                    HipL = Mathf.Lerp(a.HipL, b.HipL, t),
                    HipR = Mathf.Lerp(a.HipR, b.HipR, t),
                    KneeL = Mathf.Lerp(a.KneeL, b.KneeL, t),
                    KneeR = Mathf.Lerp(a.KneeR, b.KneeR, t),
                };
            }
        }

        struct GhostSnapshot
        {
            public float SpawnTime;
            public Matrix4x4[] Matrices;
        }

        const int k_MaxGhosts = 24;

        PlayerCharacterController m_Controller;
        PlayerAirDash m_AirDash;
        PlayerMeleeAttack m_Melee;

        Pose m_Current;
        Vector3 m_ModelBasePos;
        Quaternion m_ModelRot = Quaternion.identity;
        float m_Phase;
        int m_PrevAirJumps;
        bool m_PrevGrounded = true;
        float m_LandTime = -10f;
        float m_LandStrength;

        float m_FlipTime = -1f;
        Vector3 m_FlipAxis = Vector3.right;
        float m_LastDashTime = -10f;
        Vector3 m_LastDashDir = Vector3.forward;

        MeshRenderer[] m_Renderers;
        Mesh[] m_Meshes;
        readonly GhostSnapshot[] m_Ghosts = new GhostSnapshot[k_MaxGhosts];
        int m_NextGhost;
        float m_NextGhostTime;
        float m_GhostEmitUntil = -10f;
        MaterialPropertyBlock m_Mpb;
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            m_Controller = GetComponent<PlayerCharacterController>();
            m_AirDash = GetComponent<PlayerAirDash>();
            m_Melee = GetComponent<PlayerMeleeAttack>();
            m_PrevAirJumps = m_Controller.AirJumpsLeft;

            if (m_Model)
            {
                m_ModelBasePos = m_Model.localPosition;
                var filters = m_Model.GetComponentsInChildren<MeshFilter>();
                m_Renderers = new MeshRenderer[filters.Length];
                m_Meshes = new Mesh[filters.Length];
                for (int i = 0; i < filters.Length; i++)
                {
                    m_Renderers[i] = filters[i].GetComponent<MeshRenderer>();
                    m_Meshes[i] = filters[i].sharedMesh;
                }

                for (int i = 0; i < k_MaxGhosts; i++)
                    m_Ghosts[i] = new GhostSnapshot { SpawnTime = -100f, Matrices = new Matrix4x4[filters.Length] };
            }

            m_Mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (!m_Model)
                return;

            float dt = Time.deltaTime;
            bool grounded = m_Controller.IsGrounded;
            Vector3 velocity = m_Controller.CharacterVelocity;
            Vector3 localVel = transform.InverseTransformDirection(velocity);
            float horizontalSpeed = new Vector2(localVel.x, localVel.z).magnitude;

            bool dashing = m_AirDash != null && m_AirDash.IsDashing;
            bool dashAiming = m_AirDash != null && m_AirDash.IsAiming;
            if (dashing)
            {
                m_LastDashTime = Time.time;
                m_LastDashDir = m_AirDash.DashDirection;
                m_FlipTime = -1f;
            }
            bool dashPose = dashing || (!grounded && Time.time - m_LastDashTime < m_DashPoseHold);

            // air jump -> flip
            int airJumps = m_Controller.AirJumpsLeft;
            if (!grounded && airJumps < m_PrevAirJumps && !dashing)
                StartFlip(localVel);
            m_PrevAirJumps = airJumps;

            // landing dip
            if (grounded && !m_PrevGrounded)
            {
                m_LandTime = Time.time;
                m_LandStrength = Mathf.Clamp01(-velocity.y / 15f + 0.3f);
                m_FlipTime = -1f;
            }
            m_PrevGrounded = grounded;

            float flipT = m_FlipTime >= 0f ? (Time.time - m_FlipTime) / m_FlipDuration : -1f;
            if (flipT >= 1f)
            {
                m_FlipTime = -1f;
                flipT = -1f;
            }

            // ---------- target pose ----------
            Pose target;
            if (dashPose)
                target = DashPose();
            else if (dashAiming)
                target = CoilPose();
            else if (grounded)
                target = GroundPose(localVel, horizontalSpeed, dt);
            else
                target = AirPose(velocity.y);

            if (flipT >= 0f)
            {
                float tuck = Mathf.Clamp01(Mathf.Sin(Mathf.PI * flipT) * 1.8f);
                target = Pose.Lerp(target, TuckPose(), tuck);
            }

            if (m_Melee != null && m_Melee.IsAttacking && !dashPose)
            {
                target.ShoulderR = new Vector2(-95f, 25f);
                target.ElbowR = -20f;
                target.SpineX += 10f;
            }

            float landT = Time.time - m_LandTime;
            if (landT < 0.25f && !dashPose)
            {
                float dip = Mathf.Sin(landT / 0.25f * Mathf.PI) * m_LandStrength;
                target.HipsY -= 0.25f * dip;
                target.HipL -= 40f * dip;
                target.HipR -= 40f * dip;
                target.KneeL += 70f * dip;
                target.KneeR += 70f * dip;
                target.SpineX += 20f * dip;
            }

            float blend = 1f - Mathf.Exp(-m_PoseSharpness * dt);
            m_Current = Pose.Lerp(m_Current, target, dashPose ? 1f - Mathf.Exp(-m_OrientSharpness * dt) : blend);
            ApplyPose(m_Current);

            // ---------- whole-body orientation ----------
            if (flipT >= 0f)
            {
                float eased = 1f - Mathf.Pow(1f - flipT, 2.2f);
                m_ModelRot = Quaternion.AngleAxis(eased * 360f * m_FlipTurns, m_FlipAxis);
            }
            else
            {
                Quaternion targetRot = Quaternion.identity;
                if (dashPose)
                    targetRot = Quaternion.Inverse(transform.rotation) * DashWorldRotation(m_LastDashDir);
                m_ModelRot = Quaternion.Slerp(m_ModelRot, targetRot, 1f - Mathf.Exp(-m_OrientSharpness * dt));
            }
            m_Model.localRotation = m_ModelRot;

            // ---------- afterimages ----------
            UpdateGhosts(dashing || Time.time <= m_GhostEmitUntil);
        }

        void StartFlip(Vector3 localVel)
        {
            m_FlipTime = Time.time;
            m_GhostEmitUntil = Time.time + m_AirJumpGhostTime;

            // flip around the axis perpendicular to the movement: forward = front flip, sideways = side flip
            Vector2 h = new Vector2(localVel.x, localVel.z);
            if (h.sqrMagnitude < 1f || Mathf.Abs(h.y) >= Mathf.Abs(h.x))
                m_FlipAxis = h.y >= -1f ? Vector3.right : Vector3.left;
            else
                m_FlipAxis = h.x > 0f ? Vector3.back : Vector3.forward;
        }

        Quaternion DashWorldRotation(Vector3 dir)
        {
            // body "up" (head) points along the dash, chest faces down (superman)
            Vector3 chest = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (chest.sqrMagnitude < 0.01f)
                chest = Vector3.ProjectOnPlane(transform.forward, dir);
            return Quaternion.LookRotation(chest.normalized, dir);
        }

        // ---------- poses ----------

        Pose GroundPose(Vector3 localVel, float speed, float dt)
        {
            if (m_Controller.IsSliding)
            {
                return new Pose
                {
                    HipsY = -0.45f, SpineX = -30f, HeadX = 25f,
                    ShoulderL = new Vector2(20f, -45f), ShoulderR = new Vector2(20f, 45f),
                    ElbowL = -20f, ElbowR = -20f,
                    HipL = -85f, KneeL = 15f, HipR = -55f, KneeR = 80f,
                };
            }

            float run = Mathf.Clamp01(speed / m_FullStrideSpeed);
            m_Phase += speed * dt / m_StrideLength * Mathf.PI;
            float sw = Mathf.Sin(m_Phase);
            float cs = Mathf.Cos(m_Phase);
            float breathe = Mathf.Sin(Time.time * 2f) * (1f - run);

            Pose p = new Pose
            {
                HipsY = -0.06f * run * Mathf.Abs(cs),
                SpineX = 3f + 14f * run + breathe * 1.5f,
                SpineZ = -Mathf.Clamp(localVel.x * 1.5f, -12f, 12f),
                HeadX = -8f * run,
                ShoulderL = new Vector2(sw * m_ArmSwing * run, -8f - breathe),
                ShoulderR = new Vector2(-sw * m_ArmSwing * run, 8f + breathe),
                ElbowL = -15f - 55f * run,
                ElbowR = -15f - 55f * run,
                HipL = -sw * m_LegSwing * run,
                HipR = sw * m_LegSwing * run,
                KneeL = run * (10f + 65f * Mathf.Max(0f, cs)),
                KneeR = run * (10f + 65f * Mathf.Max(0f, -cs)),
            };

            if (m_Controller.IsCrouching)
            {
                p.HipsY -= 0.35f;
                p.SpineX += 25f;
                p.HipL -= 70f;
                p.HipR -= 70f;
                p.KneeL += 100f;
                p.KneeR += 100f;
            }

            return p;
        }

        Pose AirPose(float verticalSpeed)
        {
            var rising = new Pose
            {
                SpineX = 5f, HeadX = -10f,
                ShoulderL = new Vector2(-150f, -15f), ShoulderR = new Vector2(30f, 25f),
                ElbowL = -15f, ElbowR = -40f,
                HipL = -65f, KneeL = 95f, HipR = 20f, KneeR = 45f,
            };
            var falling = new Pose
            {
                SpineX = -5f, HeadX = 10f,
                ShoulderL = new Vector2(-30f, -75f), ShoulderR = new Vector2(-30f, 75f),
                ElbowL = -25f, ElbowR = -25f,
                HipL = -30f, KneeL = 45f, HipR = 10f, KneeR = 30f,
            };
            return Pose.Lerp(rising, falling, Mathf.InverseLerp(4f, -8f, verticalSpeed));
        }

        static Pose TuckPose()
        {
            return new Pose
            {
                HipsY = 0.1f, SpineX = 35f, HeadX = 25f,
                ShoulderL = new Vector2(-70f, -10f), ShoulderR = new Vector2(-70f, 10f),
                ElbowL = -110f, ElbowR = -110f,
                HipL = -115f, KneeL = 135f, HipR = -115f, KneeR = 135f,
            };
        }

        static Pose CoilPose()
        {
            return new Pose
            {
                HipsY = 0.1f, SpineX = 30f, HeadX = -20f,
                ShoulderL = new Vector2(-30f, -35f), ShoulderR = new Vector2(-30f, 35f),
                ElbowL = -95f, ElbowR = -95f,
                HipL = -85f, KneeL = 110f, HipR = -60f, KneeR = 120f,
            };
        }

        static Pose DashPose()
        {
            return new Pose
            {
                HeadX = -45f,
                ShoulderL = new Vector2(-170f, -12f), ShoulderR = new Vector2(-170f, 12f),
                ElbowL = -5f, ElbowR = -5f,
                HipL = 6f, KneeL = 12f, HipR = 2f, KneeR = 6f,
            };
        }

        void ApplyPose(Pose p)
        {
            m_Model.localPosition = m_ModelBasePos + Vector3.up * p.HipsY;
            if (m_Spine) m_Spine.localRotation = Quaternion.Euler(p.SpineX, 0f, p.SpineZ);
            if (m_Head) m_Head.localRotation = Quaternion.Euler(p.HeadX, 0f, 0f);
            if (m_ShoulderL) m_ShoulderL.localRotation = Quaternion.Euler(p.ShoulderL.x, 0f, p.ShoulderL.y);
            if (m_ShoulderR) m_ShoulderR.localRotation = Quaternion.Euler(p.ShoulderR.x, 0f, p.ShoulderR.y);
            if (m_ElbowL) m_ElbowL.localRotation = Quaternion.Euler(p.ElbowL, 0f, 0f);
            if (m_ElbowR) m_ElbowR.localRotation = Quaternion.Euler(p.ElbowR, 0f, 0f);
            if (m_HipL) m_HipL.localRotation = Quaternion.Euler(p.HipL, 0f, 0f);
            if (m_HipR) m_HipR.localRotation = Quaternion.Euler(p.HipR, 0f, 0f);
            if (m_KneeL) m_KneeL.localRotation = Quaternion.Euler(p.KneeL, 0f, 0f);
            if (m_KneeR) m_KneeR.localRotation = Quaternion.Euler(p.KneeR, 0f, 0f);
        }

        // ---------- afterimages ----------

        void UpdateGhosts(bool emit)
        {
            if (m_GhostMaterial == null || m_Meshes == null || m_Meshes.Length == 0)
                return;

            if (emit && Time.time >= m_NextGhostTime)
            {
                m_NextGhostTime = Time.time + m_GhostInterval;
                ref GhostSnapshot g = ref m_Ghosts[m_NextGhost];
                g.SpawnTime = Time.time;
                for (int i = 0; i < m_Renderers.Length; i++)
                    g.Matrices[i] = m_Renderers[i].transform.localToWorldMatrix;
                m_NextGhost = (m_NextGhost + 1) % k_MaxGhosts;
            }

            for (int gi = 0; gi < k_MaxGhosts; gi++)
            {
                float age = Time.time - m_Ghosts[gi].SpawnTime;
                if (age < 0f || age >= m_GhostLifetime)
                    continue;

                Color c = m_GhostColor;
                c.a *= 1f - age / m_GhostLifetime;
                m_Mpb.SetColor(k_BaseColor, c);
                Matrix4x4[] matrices = m_Ghosts[gi].Matrices;
                for (int i = 0; i < m_Meshes.Length; i++)
                {
                    if (m_Renderers[i].enabled && m_Renderers[i].gameObject.activeInHierarchy)
                        Graphics.DrawMesh(m_Meshes[i], matrices[i], m_GhostMaterial, gameObject.layer, null, 0, m_Mpb,
                            false, false);
                }
            }
        }
    }
}
