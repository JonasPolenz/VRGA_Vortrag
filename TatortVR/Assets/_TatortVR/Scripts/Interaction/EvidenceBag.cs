using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Evidence bag for exactly one item. Taken from the hip holster, a held <see cref="EvidenceItem"/>
    /// brought into it is sealed inside and the bag is numbered. Sealed bags belong in the
    /// <see cref="EvidenceBox"/>; the holster then gets a fresh bag.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class EvidenceBag : MonoBehaviour
    {
        [SerializeField] BagDispenser m_Dispenser;
        [SerializeField] Transform m_Contents;
        [SerializeField] Text[] m_Labels = new Text[0];
        [SerializeField] Renderer m_Seal;
        [SerializeField] Color m_SealedColor = new Color(0.1f, 0.6f, 0.2f);

        const float k_HintInterval = 3f;

        XRGrabInteractable m_Grab;
        EvidenceItem m_Pending;
        float m_NextHintAt;

        public EvidenceItem Content { get; private set; }
        public int Number { get; private set; }
        public bool IsSealed => Content != null;
        public bool IsDeposited { get; private set; }

        bool IsHeldByHand => m_Grab.isSelected && !(m_Grab.firstInteractorSelecting is XRSocketInteractor);

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            UpdateLabels();
        }

        void OnTriggerStay(Collider other)
        {
            if (IsSealed || m_Pending != null)
                return;
            var item = other.GetComponentInParent<EvidenceItem>();
            if (item == null || item.IsBagged || !item.IsHeldByHand)
                return;

            if (IsHeldByHand)
            {
                m_Pending = item;
            }
            else if (Time.time >= m_NextHintAt)
            {
                m_NextHintAt = Time.time + k_HintInterval;
                var progress = TrainingProgress.Instance;
                if (progress != null)
                    progress.ShowMessage("Beutel zuerst von der Hüfte nehmen");
            }
        }

        // Selection changes happen outside the physics callbacks.
        void Update()
        {
            if (m_Pending == null)
                return;
            var item = m_Pending;
            m_Pending = null;
            if (!IsSealed && !item.IsBagged)
                Seal(item);
        }

        void Seal(EvidenceItem item)
        {
            Content = item;
            item.PutInto(m_Contents, Vector3.zero);
            var progress = TrainingProgress.Instance;
            Number = progress != null ? progress.NextBagNumber() : 1;

            // A sealed bag no longer fits the hip holster and stays where it is put down.
            m_Grab.interactionLayers = InteractionLayerMask.GetMask("Default");
            if (TryGetComponent<HolsterReturn>(out var holsterReturn))
                holsterReturn.enabled = false;

            if (m_Seal != null)
            {
                var block = new MaterialPropertyBlock();
                m_Seal.GetPropertyBlock(block);
                block.SetColor("_BaseColor", m_SealedColor);
                m_Seal.SetPropertyBlock(block);
            }

            UpdateLabels();
            if (progress != null)
                progress.RegisterBagged(item, Number);
            if (m_Dispenser != null)
                m_Dispenser.Refill();
        }

        /// <summary>Releases the bag from the hand and lays it into <paramref name="container"/>.</summary>
        public void Deposit(Transform container, Vector3 localPosition, Quaternion localRotation)
        {
            IsDeposited = true;
            if (m_Grab.isSelected)
                m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
            m_Grab.enabled = false;
            if (TryGetComponent<Rigidbody>(out var body))
                body.isKinematic = true;
            foreach (var bagCollider in GetComponentsInChildren<Collider>())
                bagCollider.enabled = false;

            transform.SetParent(container, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }

        void UpdateLabels()
        {
            var text = IsSealed
                ? $"BEWEISMITTEL Nr. {Number}\n{Content.DisplayName}\nversiegelt"
                : "BEWEISMITTEL\nleer";
            foreach (var label in m_Labels)
            {
                if (label != null)
                    label.text = text;
            }
        }
    }
}
