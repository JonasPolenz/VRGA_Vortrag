using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Evidence camera with a live viewfinder on its back. Pulling the trigger while holding it takes a photo of
    /// every <see cref="SceneTrace"/> and every currently lit <see cref="LatentTrace"/> that is inside the
    /// viewfinder image and not hidden behind something. <see cref="TrainingProgress"/> decides what the photo
    /// counts as.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PhotoCamera : MonoBehaviour
    {
        [SerializeField] Transform m_Lens;
        [SerializeField] Light m_Flash;
        [SerializeField] GameObject m_FlashGlow;
        [SerializeField] Text m_Display;
        [SerializeField] float m_Range = 6f;
        [SerializeField] float m_FieldOfView = 60f;
        [SerializeField] float m_Cooldown = 0.6f;
        [SerializeField] float m_FlashDuration = 0.1f;

        [Header("Viewfinder")]
        [Tooltip("Disabled camera at the lens; it is rendered by hand at a low rate to spare the headset.")]
        [SerializeField] Camera m_View;
        [Tooltip("Screen on the back of the camera that shows the viewfinder image.")]
        [SerializeField] Renderer m_Screen;
        [SerializeField] Vector2Int m_Resolution = new Vector2Int(320, 240);
        [SerializeField] float m_RefreshRate = 15f;
        [Tooltip("How long the photo that was just taken stays on the screen.")]
        [SerializeField] float m_ReviewTime = 1.2f;

        XRGrabInteractable m_Grab;
        RenderTexture m_Image;
        readonly List<SceneTrace> m_InView = new List<SceneTrace>();
        readonly List<LatentTrace> m_LatentInView = new List<LatentTrace>();
        readonly List<Door> m_DoorsInView = new List<Door>();
        int m_PhotoCount;
        float m_LastPhoto = float.NegativeInfinity;
        float m_FlashOffAt = -1f;
        float m_NextFrameAt;

        public int PhotoCount => m_PhotoCount;

        /// <summary>The viewfinder image, or null when the camera has no viewfinder.</summary>
        public RenderTexture Image => m_Image;

        bool IsHeldByHand => m_Grab.isSelected && !(m_Grab.firstInteractorSelecting is XRSocketInteractor);

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            SetFlash(false);
            UpdateDisplay();

            if (m_View == null)
                return;
            m_Image = new RenderTexture(m_Resolution.x, m_Resolution.y, 16) { name = "Sucherbild", antiAliasing = 1 };
            m_View.enabled = false;
            m_View.fieldOfView = m_FieldOfView;
            m_View.targetTexture = m_Image;
            if (m_Screen == null)
                return;
            // The screen material is dark in the editor; with the image on it the colour must not tint it.
            var screen = m_Screen.material;
            screen.mainTexture = m_Image;
            screen.color = Color.white;
        }

        void OnDestroy()
        {
            m_Grab.activated.RemoveListener(OnActivated);
            if (m_Image == null)
                return;
            m_View.targetTexture = null;
            m_Image.Release();
            Destroy(m_Image);
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (TakePhoto() && args.interactorObject is XRBaseInputInteractor hand)
                hand.SendHapticImpulse(0.5f, 0.1f);
        }

        public bool TakePhoto()
        {
            if (Time.time - m_LastPhoto < m_Cooldown)
                return false;
            m_LastPhoto = Time.time;
            m_PhotoCount++;
            SetFlash(true);
            m_FlashOffAt = Time.time + m_FlashDuration;

            CollectTracesInView();
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterPhoto(m_PhotoCount, m_InView, m_LatentInView, m_DoorsInView);
            UpdateDisplay();

            // The shot itself stays on the screen for a moment, lit by the flash.
            if (m_View != null)
            {
                m_View.Render();
                m_NextFrameAt = Time.time + m_ReviewTime;
            }

            return true;
        }

        void Update()
        {
            if (m_FlashOffAt < 0f || Time.time < m_FlashOffAt)
                return;
            m_FlashOffAt = -1f;
            SetFlash(false);
        }

        // After everything has moved for this frame, so the image matches where the camera is held.
        void LateUpdate()
        {
            if (m_View == null || Time.time < m_NextFrameAt || !IsHeldByHand)
                return;
            m_NextFrameAt = Time.time + 1f / m_RefreshRate;
            m_View.Render();
        }

        void CollectTracesInView()
        {
            m_InView.Clear();
            foreach (var trace in SceneTrace.AllTraces)
            {
                if (trace is EvidenceItem item && item.IsBagged)
                    continue;
                if (IsInFrame(trace.Center, trace.transform))
                    m_InView.Add(trace);
            }

            m_LatentInView.Clear();
            foreach (var trace in LatentTrace.All)
            {
                if (trace.IsVisible && IsInFrame(trace.transform.position, null))
                    m_LatentInView.Add(trace);
            }

            // Doors only matter as long as the state they were found in can still be recorded.
            m_DoorsInView.Clear();
            foreach (var door in Door.All)
            {
                if (!door.IsSettled && IsInFrame(door.Center, door.transform))
                    m_DoorsInView.Add(door);
            }
        }

        // Inside the picture and not hidden behind a wall or furniture. Traces lie on surfaces, so the ray
        // stops just short of them; hitting a collider of the photographed thing itself does not count as hidden.
        bool IsInFrame(Vector3 point, Transform owner)
        {
            var toPoint = point - m_Lens.position;
            var distance = toPoint.magnitude;
            if (distance > m_Range || !IsInPicture(point, toPoint))
                return false;
            if (!Physics.Raycast(m_Lens.position, toPoint / distance, out var hit, distance - 0.04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return owner != null && hit.collider.transform.IsChildOf(owner);
        }

        // What the viewfinder shows is what counts; without a viewfinder a cone around the lens axis is used.
        bool IsInPicture(Vector3 point, Vector3 toPoint)
        {
            if (m_View == null)
                return Vector3.Angle(m_Lens.forward, toPoint) <= m_FieldOfView / 2f;
            var viewport = m_View.WorldToViewportPoint(point);
            return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        }

        void SetFlash(bool on)
        {
            if (m_Flash != null)
                m_Flash.enabled = on;
            if (m_FlashGlow != null)
                m_FlashGlow.SetActive(on);
        }

        void UpdateDisplay()
        {
            if (m_Display != null)
                m_Display.text = $"Fotos: {m_PhotoCount}";
        }
    }
}
