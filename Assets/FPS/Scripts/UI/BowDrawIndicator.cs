using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Small draw-strength bar under the crosshair while the bow is drawn.
    /// Grey = not drawn enough to shoot, white = ready, orange = full draw. A tick marks the minimum draw.
    /// Builds its own overlay canvas.
    /// </summary>
    public class BowDrawIndicator : MonoBehaviour
    {
        [SerializeField] private Vector2 m_Size = new Vector2(90f, 6f);
        [SerializeField] private float m_OffsetBelowCenter = 42f;
        [SerializeField] private Color m_NotReadyColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        [SerializeField] private Color m_ReadyColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color m_FullColor = new Color(1f, 0.6f, 0.1f, 1f);
        [SerializeField] private Color m_BackgroundColor = new Color(0f, 0f, 0f, 0.5f);

        PlayerWeaponsManager m_WeaponsManager;
        CanvasGroup m_Group;
        RectTransform m_Fill;
        RectTransform m_MinTick;
        Image m_FillImage;

        void Awake()
        {
            var canvasGo = new GameObject("BowDrawCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Group = canvasGo.GetComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            m_Group.blocksRaycasts = false;

            RectTransform bg = CreateImage("Background", canvasGo.transform, m_BackgroundColor);
            bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);
            bg.sizeDelta = m_Size + new Vector2(4f, 4f);
            bg.anchoredPosition = new Vector2(0f, -m_OffsetBelowCenter);

            m_Fill = CreateImage("Fill", bg, m_NotReadyColor);
            m_Fill.anchorMin = new Vector2(0f, 0f);
            m_Fill.anchorMax = new Vector2(0f, 1f);
            m_Fill.pivot = new Vector2(0f, 0.5f);
            // no horizontal offsets: a negative width at low charge would draw flipped
            m_Fill.offsetMin = new Vector2(0f, 2f);
            m_Fill.offsetMax = new Vector2(0f, -2f);
            m_FillImage = m_Fill.GetComponent<Image>();

            m_MinTick = CreateImage("MinTick", bg, Color.white);
            m_MinTick.anchorMin = new Vector2(0f, 0f);
            m_MinTick.anchorMax = new Vector2(0f, 1f);
            m_MinTick.sizeDelta = new Vector2(2f, 6f);
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

        void LateUpdate()
        {
            if (m_WeaponsManager == null)
            {
                m_WeaponsManager = FindAnyObjectByType<PlayerWeaponsManager>();
                if (m_WeaponsManager == null)
                    return;
            }

            WeaponController weapon = m_WeaponsManager.GetActiveWeapon();
            bool show = weapon != null && weapon.ShootType == WeaponShootType.Charge && weapon.IsCharging;
            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 12f);
            if (!show)
                return;

            float charge = weapon.CurrentCharge;
            m_Fill.anchorMax = new Vector2(charge, 1f);
            m_MinTick.anchorMin = new Vector2(weapon.MinChargeToShoot, 0f);
            m_MinTick.anchorMax = new Vector2(weapon.MinChargeToShoot, 1f);
            m_MinTick.gameObject.SetActive(weapon.MinChargeToShoot > 0f);

            m_FillImage.color = charge >= 0.99f ? m_FullColor
                : weapon.IsChargeReadyToShoot ? m_ReadyColor
                : m_NotReadyColor;
        }
    }
}
