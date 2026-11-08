using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>
    /// Protective equipment that is "put on" by holding it to the matching <see cref="WearZone"/>:
    /// gloves to the other hand, the suit to the chest.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class WearableItem : MonoBehaviour
    {
        [SerializeField] TrainingProgress.Equipment m_Equipment;

        XRGrabInteractable m_Grab;

        public TrainingProgress.Equipment Equipment => m_Equipment;
        public bool IsHeld => m_Grab.isSelected;
        public Transform Holder => m_Grab.isSelected ? m_Grab.firstInteractorSelecting.transform : null;

        void Awake() => m_Grab = GetComponent<XRGrabInteractable>();

        public void PutOn()
        {
            if (m_Grab.isSelected)
                m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
            gameObject.SetActive(false);

            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.Wear(m_Equipment);
        }
    }
}
