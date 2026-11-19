using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Owns gameplay time-scale effects so they don't fight each other:
    ///  - hit-stop: very short freeze on impactful hits
    ///  - slow motion: held while a system requests it (e.g. air-dash aiming)
    /// Final timeScale = slowMotion * hitStop. Never touches the time scale while the game is paused (timeScale == 0).
    /// Self-creating singleton.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop s_Instance;

        float m_HitStopUntilUnscaled;
        float m_HitStopScale = 1f;
        float m_SlowMotionScale = 1f;
        bool m_IsDrivingTimeScale;

        static HitStop Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new GameObject("HitStop").AddComponent<HitStop>();
                return s_Instance;
            }
        }

        public static bool IsSlowMotionActive => s_Instance != null && s_Instance.m_SlowMotionScale < 1f;

        /// <summary>Slows time to <paramref name="timeScale"/> for <paramref name="duration"/> real-time seconds.</summary>
        public static void Request(float duration, float timeScale = 0.05f)
        {
            if (duration <= 0f || Time.timeScale == 0f)
                return;

            HitStop instance = Instance;
            bool active = Time.unscaledTime < instance.m_HitStopUntilUnscaled;
            instance.m_HitStopScale = active ? Mathf.Min(instance.m_HitStopScale, timeScale) : timeScale;
            instance.m_HitStopUntilUnscaled = Mathf.Max(instance.m_HitStopUntilUnscaled, Time.unscaledTime + duration);
            instance.Apply();
        }

        /// <summary>Sets the slow-motion scale (1 = off). Call SetSlowMotion(1) to release.</summary>
        public static void SetSlowMotion(float timeScale)
        {
            if (s_Instance == null && timeScale >= 1f)
                return;

            Instance.m_SlowMotionScale = Mathf.Clamp(timeScale, 0.01f, 1f);
            Instance.Apply();
        }

        void Update()
        {
            Apply();
        }

        void Apply()
        {
            // the pause menu owns timeScale while paused; it restores 1 itself on resume
            if (Time.timeScale == 0f)
                return;

            float hitStop = Time.unscaledTime < m_HitStopUntilUnscaled ? m_HitStopScale : 1f;
            float target = m_SlowMotionScale * hitStop;

            if (target < 1f)
            {
                Time.timeScale = target;
                m_IsDrivingTimeScale = true;
            }
            else if (m_IsDrivingTimeScale)
            {
                Time.timeScale = 1f;
                m_IsDrivingTimeScale = false;
            }
        }

        void OnDestroy()
        {
            if (s_Instance != this)
                return;

            if (m_IsDrivingTimeScale && Time.timeScale != 0f)
                Time.timeScale = 1f;
            s_Instance = null;
        }
    }
}
