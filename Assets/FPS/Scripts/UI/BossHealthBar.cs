using TMPro;
using Unity.FPS.AI;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Screen-top boss health bar. Builds its own overlay canvas at runtime (no prefab wiring needed).
    /// Appears when the boss is engaged (sees the player, was damaged, or the player is close).
    /// </summary>
    public class BossHealthBar : MonoBehaviour
    {
        [SerializeField] private float m_ShowDistance = 45f;
        [SerializeField] private Vector2 m_BarSize = new Vector2(700f, 18f);
        [SerializeField] private float m_TopMargin = 165f;
        [SerializeField] private Color m_FillColor = new Color(0.85f, 0.15f, 0.12f, 1f);
        [SerializeField] private Color m_LagColor = new Color(1f, 0.85f, 0.4f, 1f);
        [SerializeField] private Color m_BackgroundColor = new Color(0f, 0f, 0f, 0.6f);

        [Tooltip("How fast the delayed 'damage taken' bar catches up")]
        [SerializeField] private float m_LagSpeed = 0.6f;

        CanvasGroup m_Group;
        RectTransform m_Fill;
        RectTransform m_Lag;
        TextMeshProUGUI m_Label;
        BossMarker m_Boss;
        bool m_Engaged;
        float m_LagRatio = 1f;

        void Awake()
        {
            BuildUI();
            m_Group.alpha = 0f;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("BossHealthBarCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Group = canvasGo.GetComponent<CanvasGroup>();
            m_Group.blocksRaycasts = false;
            m_Group.interactable = false;

            RectTransform background = CreateImage("Background", canvasGo.transform, m_BackgroundColor);
            background.anchorMin = background.anchorMax = new Vector2(0.5f, 1f);
            background.pivot = new Vector2(0.5f, 1f);
            background.sizeDelta = m_BarSize + new Vector2(6f, 6f);
            background.anchoredPosition = new Vector2(0f, -m_TopMargin);

            m_Lag = CreateImage("Lag", background, m_LagColor);
            StretchLeft(m_Lag);
            m_Fill = CreateImage("Fill", background, m_FillColor);
            StretchLeft(m_Fill);

            var labelGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(background, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(0f, 34f);
            labelRect.anchoredPosition = new Vector2(0f, 4f);
            m_Label = labelGo.GetComponent<TextMeshProUGUI>();
            m_Label.alignment = TextAlignmentOptions.Bottom;
            m_Label.fontSize = 26f;
            m_Label.fontStyle = FontStyles.Bold;
            m_Label.color = Color.white;
            m_Label.raycastTarget = false;
        }

        static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static void StretchLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
        }

        void Update()
        {
            if (m_Boss == null)
            {
                m_Boss = BossMarker.Current;
                m_Engaged = false;
                m_LagRatio = 1f;
                if (m_Boss != null)
                {
                    m_Label.text = m_Boss.DisplayName;
                    m_Boss.Health.OnDamaged += OnBossDamaged;
                }
            }

            if (m_Boss == null || m_Boss.Health == null)
            {
                m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, 0f, Time.unscaledDeltaTime * 2f);
                return;
            }

            if (!m_Engaged)
            {
                GameObject player = FindPlayer();
                bool close = player != null &&
                             Vector3.Distance(player.transform.position, m_Boss.transform.position) < m_ShowDistance;
                bool seesPlayer = m_Boss.Enemy != null && m_Boss.Enemy.DetectionModule != null &&
                                  m_Boss.Enemy.IsSeeingTarget;
                m_Engaged = close || seesPlayer;
            }

            float ratio = Mathf.Clamp01(m_Boss.Health.GetRatio());
            m_LagRatio = Mathf.Max(ratio, Mathf.MoveTowards(m_LagRatio, ratio, m_LagSpeed * Time.deltaTime));
            SetRatio(m_Fill, ratio);
            SetRatio(m_Lag, m_LagRatio);

            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, m_Engaged ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        }

        static void SetRatio(RectTransform rect, float ratio)
        {
            rect.anchorMax = new Vector2(ratio, 1f);
        }

        void OnBossDamaged(float damage, GameObject source)
        {
            m_Engaged = true;
        }

        ActorsManager m_ActorsManager;

        GameObject FindPlayer()
        {
            if (m_ActorsManager == null)
                m_ActorsManager = FindAnyObjectByType<ActorsManager>();
            return m_ActorsManager != null ? m_ActorsManager.Player : null;
        }

        void OnDestroy()
        {
            if (m_Boss != null && m_Boss.Health != null)
                m_Boss.Health.OnDamaged -= OnBossDamaged;
        }
    }
}
