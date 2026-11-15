using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Box of consumables such as disposable gloves: as soon as the item on top is taken, the next one appears.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class ItemDispenser : MonoBehaviour
    {
        [Tooltip("Inactive object in the scene that is copied for every new item.")]
        [SerializeField] XRGrabInteractable m_Template;
        [SerializeField] string m_ItemName = "Teil";
        [SerializeField] int m_MaxItems = 30;

        XRSocketInteractor m_Socket;
        int m_Count;
        bool m_RefillPending;

        void Awake()
        {
            m_Socket = GetComponent<XRSocketInteractor>();
            m_Socket.selectExited.AddListener(OnTaken);
        }

        void OnDestroy() => m_Socket.selectExited.RemoveListener(OnTaken);

        void Start() => Refill();

        // Selecting from inside the exit event would interfere with the hand taking the item.
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
            if (m_Template == null || m_Socket.hasSelection || m_Count >= m_MaxItems)
                return;

            var attach = m_Socket.attachTransform != null ? m_Socket.attachTransform : transform;
            var item = Instantiate(m_Template, attach.position, attach.rotation, m_Template.transform.parent);
            item.name = $"{m_ItemName} {++m_Count}";
            item.gameObject.SetActive(true);
            item.interactionManager.SelectEnter((IXRSelectInteractor)m_Socket, (IXRSelectInteractable)item);
        }
    }
}
