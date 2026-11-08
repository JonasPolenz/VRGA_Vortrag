using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Evidence camera. Pulling the trigger while holding it takes a photo: every unsecured
    /// <see cref="EvidenceItem"/> and every currently lit <see cref="LatentTrace"/> inside the field of view
    /// and not hidden behind something counts as documented.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PhotoCamera : MonoBehaviour
    {
        [SerializeField] Transform m_Lens;
        [SerializeField] Light m_Flash;
        [SerializeField] GameObject m_FlashGlow;
        [SerializeField] Text m_Display;
        [SerializeField] float m_Range = 4f;
        [SerializeField] float m_FieldOfView = 50f;
        [SerializeField] float m_Cooldown = 0.6f;
        [SerializeField] float m_FlashDuration = 0.1f;

        XRGrabInteractable m_Grab;
        readonly List<EvidenceItem> m_InView = new List<EvidenceItem>();
        readonly List<LatentTrace> m_TracesInView = new List<LatentTrace>();
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

            CollectEvidenceInView();
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterPhoto(m_PhotoCount, m_InView, m_TracesInView);
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

        void CollectEvidenceInView()
        {
            m_InView.Clear();
            foreach (var item in EvidenceItem.All)
            {
                if (item.IsBagged)
                    continue;
                var toItem = item.Center - m_Lens.position;
                var distance = toItem.magnitude;
                if (distance > m_Range || Vector3.Angle(m_Lens.forward, toItem) > m_FieldOfView / 2f)
                    continue;
                // Hidden behind a wall or furniture?
                if (Physics.Raycast(m_Lens.position, toItem / distance, out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<EvidenceItem>() != item)
                    continue;
                m_InView.Add(item);
            }

            m_TracesInView.Clear();
            foreach (var trace in LatentTrace.All)
            {
                if (!trace.IsVisible)
                    continue;
                var toTrace = trace.transform.position - m_Lens.position;
                var distance = toTrace.magnitude;
                if (distance > m_Range || Vector3.Angle(m_Lens.forward, toTrace) > m_FieldOfView / 2f)
                    continue;
                // Traces sit on surfaces, so stop the ray just short of them.
                if (Physics.Raycast(m_Lens.position, toTrace / distance, distance - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                m_TracesInView.Add(trace);
            }
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
