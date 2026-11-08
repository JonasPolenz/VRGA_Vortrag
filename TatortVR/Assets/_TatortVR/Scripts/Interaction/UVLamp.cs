using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Forensic UV lamp. The trigger switches it on and off; while on, every <see cref="LatentTrace"/>
    /// inside the light cone and not hidden behind something lights up. It switches off when let go.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class UVLamp : MonoBehaviour
    {
        [SerializeField] Transform m_Lens;
        [SerializeField] Light m_Light;
        [SerializeField] GameObject m_Glow;
        [SerializeField] float m_Range = 3f;
        [SerializeField] float m_ConeAngle = 40f;

        const float k_LitDuration = 0.15f;

        XRGrabInteractable m_Grab;
        bool m_ExplainedOnce;

        public bool IsOn { get; private set; }

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            m_Grab.selectExited.AddListener(OnReleased);
            SetOn(false);
        }

        void OnDestroy()
        {
            m_Grab.activated.RemoveListener(OnActivated);
            m_Grab.selectExited.RemoveListener(OnReleased);
        }

        void OnActivated(ActivateEventArgs args)
        {
            SetOn(!IsOn);
            if (args.interactorObject is XRBaseInputInteractor hand)
                hand.SendHapticImpulse(0.2f, 0.05f);
        }

        void OnReleased(SelectExitEventArgs args) => SetOn(false);

        public void SetOn(bool on)
        {
            IsOn = on;
            if (m_Light != null)
                m_Light.enabled = on;
            if (m_Glow != null)
                m_Glow.SetActive(on);

            if (on && !m_ExplainedOnce)
            {
                m_ExplainedOnce = true;
                var progress = TrainingProgress.Instance;
                if (progress != null)
                    progress.ShowMessage("UV-Lampe an: verborgene Spuren leuchten im Lichtkegel");
            }
        }

        void Update()
        {
            if (!IsOn)
                return;
            foreach (var trace in LatentTrace.All)
            {
                var toTrace = trace.transform.position - m_Lens.position;
                var distance = toTrace.magnitude;
                if (distance > m_Range || Vector3.Angle(m_Lens.forward, toTrace) > m_ConeAngle / 2f)
                    continue;
                // Traces sit on surfaces, so stop the ray just short of them.
                if (Physics.Raycast(m_Lens.position, toTrace / distance, distance - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                trace.Illuminate(k_LitDuration);
            }
        }
    }
}
