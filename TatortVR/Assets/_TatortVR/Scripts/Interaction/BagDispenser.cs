using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Keeps an empty evidence bag in the hip holster: one at the start and a fresh one whenever a bag gets sealed.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class BagDispenser : MonoBehaviour
    {
        [Tooltip("Inactive bag in the scene that is copied for every new bag.")]
        [SerializeField] EvidenceBag m_Template;

        XRSocketInteractor m_Holster;
        int m_Count;

        void Awake() => m_Holster = GetComponent<XRSocketInteractor>();

        void Start() => Refill();

        public void Refill()
        {
            if (m_Template == null || m_Holster.hasSelection)
                return;

            var attach = m_Holster.attachTransform != null ? m_Holster.attachTransform : transform;
            var bag = Instantiate(m_Template, attach.position, attach.rotation, m_Template.transform.parent);
            bag.name = $"Beweisbeutel {++m_Count}";
            bag.gameObject.SetActive(true);
            var grab = bag.GetComponent<XRGrabInteractable>();
            grab.interactionManager.SelectEnter((IXRSelectInteractor)m_Holster, (IXRSelectInteractable)grab);
        }
    }
}
