using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Shows where the grapple will attach: a small diamond on the target point
    /// (accent color + bigger when snapped to a GrapplePoint block). Builds its own overlay canvas.
    /// </summary>
    public class GrappleReticle : MonoBehaviour
    {
        [SerializeField] private float m_Size = 16f;
        [SerializeField] private float m_AssistedSize = 26f;
        [SerializeField] private Color m_SurfaceColor = new Color(1f, 1f, 1f, 0.75f);
        [SerializeField] private Color m_AssistedColor = new Color(1f, 0.6f, 0.1f, 1f);
        [SerializeField] private Color m_EnemyColor = new Color(1f, 0.15f, 0.15f, 1f);
        [SerializeField] private float m_Sharpness = 25f;

        GrappleHook m_Grapple;
        Camera m_Camera;
        RectTransform m_Marker;
        Image m_Image;
        RectTransform m_CanvasRect;
        Canvas m_Canvas;

        void Awake()
        {
            var canvasGo = new GameObject("GrappleReticleCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 5;
            m_CanvasRect = (RectTransform)canvasGo.transform;

            var markerGo = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            markerGo.transform.SetParent(canvasGo.transform, false);
            m_Marker = (RectTransform)markerGo.transform;
            m_Marker.anchorMin = m_Marker.anchorMax = Vector2.zero;
            m_Marker.localRotation = Quaternion.Euler(0f, 0f, 45f);
            m_Image = markerGo.GetComponent<Image>();
            m_Image.raycastTarget = false;
            m_Image.enabled = false;
        }

        void LateUpdate()
        {
            if (m_Grapple == null)
            {
                m_Grapple = FindAnyObjectByType<GrappleHook>();
                if (m_Grapple == null)
                    return;
                var rig = m_Grapple.GetComponent<ThirdPersonCameraRig>();
                m_Camera = rig != null ? rig.Camera : Camera.main;
            }

            bool show = m_Grapple.HasTarget && !m_Grapple.IsActive && m_Camera != null;
            Vector3 screen = show ? m_Camera.WorldToScreenPoint(m_Grapple.TargetPoint) : Vector3.zero;
            if (screen.z <= 0f)
                show = false;

            m_Image.enabled = show;
            if (!show)
                return;

            // overlay canvas: screen pixels -> canvas units
            float scale = m_CanvasRect.localScale.x > 0f ? m_CanvasRect.localScale.x : 1f;
            m_Marker.anchoredPosition = new Vector2(screen.x, screen.y) / scale;

            bool highlighted = m_Grapple.TargetIsAssisted || m_Grapple.TargetIsEnemy;
            float targetSize = highlighted ? m_AssistedSize : m_Size;
            float size = Mathf.Lerp(m_Marker.sizeDelta.x, targetSize, 1f - Mathf.Exp(-m_Sharpness * Time.unscaledDeltaTime));
            m_Marker.sizeDelta = new Vector2(size, size);
            m_Image.color = m_Grapple.TargetIsEnemy ? m_EnemyColor
                : m_Grapple.TargetIsAssisted ? m_AssistedColor
                : m_SurfaceColor;
        }
    }
}
