using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Belt pouch with numbered evidence markers: as soon as one is taken out, the next number appears.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class MarkerDispenser : MonoBehaviour
    {
        [Tooltip("Inactive marker in the scene that is copied for every new number.")]
        [SerializeField] EvidenceMarker m_Template;
        [SerializeField] int m_MaxMarkers = 9;

        XRSocketInteractor m_Pouch;
        int m_Count;
        bool m_RefillPending;

        void Awake()
        {
            m_Pouch = GetComponent<XRSocketInteractor>();
            m_Pouch.selectExited.AddListener(OnTaken);
        }

        void OnDestroy() => m_Pouch.selectExited.RemoveListener(OnTaken);

        void Start() => Refill();

        // Selecting from inside the exit event would interfere with the hand taking the marker.
        void OnTaken(SelectExitEventArgs args) => m_RefillPending = true;

        void Update()
        {
            if (!m_RefillPending)
                return;
            m_RefillPending = false;
            Refill();
        }

        void Refill()
        {
            if (m_Template == null || m_Pouch.hasSelection || m_Count >= m_MaxMarkers)
                return;

            var attach = m_Pouch.attachTransform != null ? m_Pouch.attachTransform : transform;
            var marker = Instantiate(m_Template, attach.position, attach.rotation, m_Template.transform.parent);
            marker.name = $"Spurenmarker {++m_Count}";
            marker.SetNumber(m_Count);
            marker.gameObject.SetActive(true);
            var grab = marker.GetComponent<XRGrabInteractable>();
            grab.interactionManager.SelectEnter((IXRSelectInteractor)m_Pouch, (IXRSelectInteractable)grab);
        }
    }
}
