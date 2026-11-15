using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Evidence packaging for exactly one item. Taken from the hip holster, the trigger switches between
    /// plastic bag, paper bag (biological traces must stay dry and breathe) and shielding bag (phones).
    /// A held <see cref="EvidenceItem"/> brought into it is sealed inside and the label takes over the item's
    /// marker number. Sealed bags belong in the <see cref="EvidenceBox"/>; the holster then gets a fresh one.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class EvidenceBag : MonoBehaviour
    {
        public enum Packaging
        {
            Kunststoff,
            Papier,
            Abschirmung,
        }

        [SerializeField] BagDispenser m_Dispenser;
        [SerializeField] Transform m_Contents;
        [SerializeField] Text[] m_Labels = new Text[0];
        [SerializeField] Renderer m_Seal;
        [SerializeField] Color m_SealedColor = new Color(0.1f, 0.6f, 0.2f);
        [Tooltip("One visual per packaging type, in the order of the Packaging enum.")]
        [SerializeField] GameObject[] m_Visuals = new GameObject[0];
        [Tooltip("Place written on the label.")]
        [SerializeField] string m_Location = "Musterweg 7";
        [SerializeField] int m_WrongPackagingPenalty = 10;

        const float k_HintInterval = 3f;

        XRGrabInteractable m_Grab;
        EvidenceItem m_Pending;
        float m_NextHintAt;

        public EvidenceItem Content { get; private set; }
        public int Number { get; private set; }
        public Packaging Type { get; private set; }
        public bool IsSealed => Content != null;
        public bool IsDeposited { get; private set; }
        public string TypeName => Type == Packaging.Papier ? "Papiertüte" : Type == Packaging.Abschirmung ? "Abschirmbeutel" : "Kunststoffbeutel";
        public string Label => Number > 0 ? $"Spur {Number}" : "Spur ohne Nr.";

        bool IsHeldByHand => m_Grab.isSelected && !(m_Grab.firstInteractorSelecting is XRSocketInteractor);

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            SetType(Packaging.Kunststoff);
        }

        void OnDestroy() => m_Grab.activated.RemoveListener(OnActivated);

        void OnActivated(ActivateEventArgs args) => ToggleType();

        /// <summary>Switches an empty bag to the next packaging type.</summary>
        public void ToggleType()
        {
            if (!IsSealed)
                SetType((Packaging)(((int)Type + 1) % System.Enum.GetValues(typeof(Packaging)).Length));
        }

        void SetType(Packaging type)
        {
            Type = type;
            for (var i = 0; i < m_Visuals.Length; i++)
            {
                if (m_Visuals[i] != null)
                    m_Visuals[i].SetActive(i == (int)type);
            }

            UpdateLabels();
        }

        void OnTriggerStay(Collider other)
        {
            if (IsSealed || m_Pending != null)
                return;
            var item = other.GetComponentInParent<EvidenceItem>();
            if (item == null || !item.isActiveAndEnabled || item.IsBagged || !item.IsHeldByHand)
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
                    progress.ShowMessage("Verpackung zuerst von der Hüfte nehmen");
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
            Number = item.Marker != null ? item.Marker.Number : 0;
            if (Type != Packaging.Kunststoff)
            {
                // Paper and shielding bags are opaque; bulky items would otherwise stick out through the flat model.
                foreach (var itemRenderer in item.GetComponentsInChildren<Renderer>())
                    itemRenderer.enabled = false;
            }

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
            var progress = TrainingProgress.Instance;
            if (progress != null)
            {
                var wrong = item.Packaging == EvidenceItem.PackagingRule.Papier && Type != Packaging.Papier
                    || item.Packaging == EvidenceItem.PackagingRule.Kunststoff && Type != Packaging.Kunststoff
                    || item.Packaging == EvidenceItem.PackagingRule.Abschirmung && Type != Packaging.Abschirmung;
                if (wrong)
                    progress.ReportMistake($"{item.DisplayName} in {TypeName} verpackt. {item.PackagingHint}", m_WrongPackagingPenalty);
                progress.RegisterBagged(item, Label, TypeName);
            }

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
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }

            foreach (var bagCollider in GetComponentsInChildren<Collider>())
                bagCollider.enabled = false;

            transform.SetParent(container, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }

        // A sealed bag carries what an evidence label needs: number, content, place, date and time.
        void UpdateLabels()
        {
            var title = TypeName.ToUpperInvariant();
            var text = IsSealed
                ? $"{title}\n{Label}: {Content.DisplayName}\n{m_Location}\n{System.DateTime.Now:dd.MM.yyyy HH:mm}"
                : $"{title}\nleer\nTrigger: wechseln";
            foreach (var label in m_Labels)
            {
                if (label != null)
                    label.text = text;
            }
        }
    }
}
