using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Evidence camera. Pulling the trigger while holding it takes a photo of every <see cref="SceneTrace"/>
    /// and every currently lit <see cref="LatentTrace"/> inside the field of view that is not hidden behind
    /// something. <see cref="TrainingProgress"/> decides what the photo counts as.
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

        XRGrabInteractable m_Grab;
        readonly List<SceneTrace> m_InView = new List<SceneTrace>();
        readonly List<LatentTrace> m_LatentInView = new List<LatentTrace>();
        int m_PhotoCount;
        float m_LastPhoto = float.NegativeInfinity;
        float m_FlashOffAt = -1f;

        public int PhotoCount => m_PhotoCount;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            SetFlash(false);
            UpdateDisplay();
        }

        void OnDestroy() => m_Grab.activated.RemoveListener(OnActivated);

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
                progress.RegisterPhoto(m_PhotoCount, m_InView, m_LatentInView);
            UpdateDisplay();
            return true;
        }

        void Update()
        {
            if (m_FlashOffAt < 0f || Time.time < m_FlashOffAt)
                return;
            m_FlashOffAt = -1f;
            SetFlash(false);
        }

        void CollectTracesInView()
        {
            m_InView.Clear();
            foreach (var trace in SceneTrace.AllTraces)
            {
                if (trace is EvidenceItem item && item.IsBagged)
                    continue;
                if (IsInFrame(trace.Center, trace))
                    m_InView.Add(trace);
            }

            m_LatentInView.Clear();
            foreach (var trace in LatentTrace.All)
            {
                if (trace.IsVisible && IsInFrame(trace.transform.position, null))
                    m_LatentInView.Add(trace);
            }
        }

        // Inside the field of view and not hidden behind a wall or furniture. Traces lie on surfaces, so the
        // ray stops just short of them; hitting the trace's own collider does not count as hidden.
        bool IsInFrame(Vector3 point, SceneTrace owner)
        {
            var toPoint = point - m_Lens.position;
            var distance = toPoint.magnitude;
            if (distance > m_Range || Vector3.Angle(m_Lens.forward, toPoint) > m_FieldOfView / 2f)
                return false;
            if (!Physics.Raycast(m_Lens.position, toPoint / distance, out var hit, distance - 0.04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return owner != null && hit.collider.GetComponentInParent<SceneTrace>() == owner;
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
