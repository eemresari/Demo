using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>Pulls the bow string back and shows a nocked arrow while the bow is being drawn.</summary>
    public class BowDrawVisual : MonoBehaviour
    {
        [SerializeField] private WeaponController m_Weapon;
        [SerializeField] private Transform m_String;

        [Tooltip("Optional arrow shown on the string while drawing")]
        [SerializeField] private Transform m_NockedArrow;

        [Tooltip("How far back (local -Z) the string goes at full draw")]
        [SerializeField] private float m_MaxPullDistance = 0.4f;

        [Tooltip("String thickness multiplier along X/Z at full draw (makes the tension readable)")]
        [SerializeField] private float m_FullDrawThickness = 2f;

        Vector3 m_StringRestPosition;
        Vector3 m_StringRestScale;
        Vector3 m_ArrowOffsetFromString;

        void Awake()
        {
            if (m_Weapon == null)
                m_Weapon = GetComponentInParent<WeaponController>();
            if (m_String != null)
            {
                m_StringRestPosition = m_String.localPosition;
                m_StringRestScale = m_String.localScale;
            }

            if (m_NockedArrow != null && m_String != null)
                m_ArrowOffsetFromString = m_NockedArrow.localPosition - m_String.localPosition;
        }

        void LateUpdate()
        {
            if (m_Weapon == null || m_String == null)
                return;

            float charge = m_Weapon.IsCharging ? m_Weapon.CurrentCharge : 0f;
            float eased = 1f - (1f - charge) * (1f - charge);

            m_String.localPosition = m_StringRestPosition + Vector3.back * (m_MaxPullDistance * eased);
            float thickness = Mathf.Lerp(1f, m_FullDrawThickness, charge);
            m_String.localScale = new Vector3(m_StringRestScale.x * thickness, m_StringRestScale.y,
                m_StringRestScale.z * thickness);

            if (m_NockedArrow != null)
            {
                m_NockedArrow.gameObject.SetActive(m_Weapon.IsCharging);
                m_NockedArrow.localPosition = m_String.localPosition + m_ArrowOffsetFromString;
            }
        }
    }
}
