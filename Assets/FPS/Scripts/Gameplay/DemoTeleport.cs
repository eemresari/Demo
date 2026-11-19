using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Demo helper: press a key (F1 by default) to teleport the player to a target (e.g. the boss arena entrance).
    /// </summary>
    public class DemoTeleport : MonoBehaviour
    {
        [SerializeField] private Key m_Key = Key.F1;
        [SerializeField] private Transform m_Target;

        PlayerCharacterController m_Player;

        void Update()
        {
            if (m_Target == null || Keyboard.current == null || !Keyboard.current[m_Key].wasPressedThisFrame)
                return;

            if (m_Player == null)
                m_Player = FindAnyObjectByType<PlayerCharacterController>();
            if (m_Player == null || m_Player.IsDead)
                return;

            var controller = m_Player.GetComponent<CharacterController>();
            controller.enabled = false;
            m_Player.transform.SetPositionAndRotation(m_Target.position,
                Quaternion.Euler(0f, m_Target.eulerAngles.y, 0f));
            controller.enabled = true;
            m_Player.CharacterVelocity = Vector3.zero;
        }
    }
}
