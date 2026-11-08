using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Puts a tool back into its holster shortly after the hand lets go of it, so it cannot get lost.
    /// Disable the component to let the object stay where it is dropped.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class HolsterReturn : MonoBehaviour
    {
        [SerializeField] XRSocketInteractor m_Holster;
        [SerializeField] float m_Delay = 0.75f;

        XRGrabInteractable m_Grab;
        float m_ReturnAt = -1f;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.selectExited.AddListener(OnReleased);
        }

        void OnDestroy() => m_Grab.selectExited.RemoveListener(OnReleased);

        void OnReleased(SelectExitEventArgs args)
        {
            if (enabled && !(args.interactorObject is XRSocketInteractor))
                m_ReturnAt = Time.time + m_Delay;
        }

        void Update()
        {
            if (m_ReturnAt < 0f || Time.time < m_ReturnAt)
                return;
            m_ReturnAt = -1f;
            if (!m_Grab.isSelected && m_Holster != null && m_Holster.isActiveAndEnabled && !m_Holster.hasSelection)
                m_Grab.interactionManager.SelectEnter((IXRSelectInteractor)m_Holster, (IXRSelectInteractable)m_Grab);
        }
    }
}
