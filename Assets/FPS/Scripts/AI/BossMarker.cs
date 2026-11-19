using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>Marks an enemy as the boss (drives the boss health bar).</summary>
    [RequireComponent(typeof(Health))]
    public class BossMarker : MonoBehaviour
    {
        [SerializeField] private string m_DisplayName = "WARDEN";

        public static BossMarker Current { get; private set; }

        public string DisplayName => m_DisplayName;
        public Health Health { get; private set; }
        public EnemyController Enemy { get; private set; }

        void Awake()
        {
            Health = GetComponent<Health>();
            Enemy = GetComponent<EnemyController>();
        }

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this)
                Current = null;
        }
    }
}
