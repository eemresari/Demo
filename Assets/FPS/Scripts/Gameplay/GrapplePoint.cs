using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Marks a block as a grapple target. The grapple hook pulls the player onto the top of the block.
    /// </summary>
    public class GrapplePoint : MonoBehaviour
    {
        static readonly List<GrapplePoint> s_All = new List<GrapplePoint>();
        public static IReadOnlyList<GrapplePoint> All => s_All;

        [Tooltip("Anchor (where the rope attaches) in local space. (0,0,0) = block center")]
        [SerializeField] private Vector3 m_LocalAnchorOffset = Vector3.zero;

        Collider m_Collider;

        /// <summary>Where the rope attaches.</summary>
        public Vector3 AnchorPosition => transform.TransformPoint(m_LocalAnchorOffset);

        /// <summary>Center of the top surface (where the player lands). Falls back to the anchor.</summary>
        public Vector3 TopSurfaceCenter
        {
            get
            {
                if (m_Collider == null)
                    m_Collider = GetComponent<Collider>();
                if (m_Collider == null)
                    return AnchorPosition;

                Bounds b = m_Collider.bounds;
                return new Vector3(b.center.x, b.max.y, b.center.z);
            }
        }

        /// <summary>World-space bounds of the block (collider), or a 1 m box around the anchor.</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (m_Collider == null)
                    m_Collider = GetComponent<Collider>();
                return m_Collider != null ? m_Collider.bounds : new Bounds(AnchorPosition, Vector3.one);
            }
        }

        /// <summary>Horizontal half-size of the block top (used to plan the approach over the edge).</summary>
        public float TopHalfExtent
        {
            get
            {
                if (m_Collider == null)
                    m_Collider = GetComponent<Collider>();
                if (m_Collider == null)
                    return 0.5f;

                Bounds b = m_Collider.bounds;
                return Mathf.Max(b.extents.x, b.extents.z);
            }
        }

        void OnEnable()
        {
            s_All.Add(this);
        }

        void OnDisable()
        {
            s_All.Remove(this);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0f, 0.8f);
            Gizmos.DrawWireSphere(TopSurfaceCenter, 0.5f);
        }
    }
}
