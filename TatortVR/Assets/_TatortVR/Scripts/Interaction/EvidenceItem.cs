using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// An object at the scene that should be photographed where it was found, then picked up with gloves
    /// and sealed in an <see cref="EvidenceBag"/>. Skipping a step is reported as a mistake.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class EvidenceItem : MonoBehaviour
    {
        [SerializeField] string m_DisplayName = "Beweisstück";
        [Tooltip("Scale inside the bag, so longer items fit.")]
        [SerializeField] float m_BaggedScale = 0.6f;
        [Tooltip("Rotation inside the bag; the default stands items whose long side is their X axis upright.")]
        [SerializeField] Vector3 m_BaggedRotation = new Vector3(0f, 0f, 90f);
        [SerializeField] int m_NoGlovesPenalty = 10;
        [SerializeField] int m_NotPhotographedPenalty = 10;

        static readonly List<EvidenceItem> s_All = new List<EvidenceItem>();

        XRGrabInteractable m_Grab;
        Renderer[] m_Renderers;
        bool m_ReportedGloves;
        bool m_ReportedPhoto;

        public static IReadOnlyList<EvidenceItem> All => s_All;

        public string DisplayName => m_DisplayName;
        public bool IsDocumented { get; private set; }
        public bool HasBeenMoved { get; private set; }
        public bool IsBagged { get; private set; }
        public bool IsHeldByHand => m_Grab.isSelected && !(m_Grab.firstInteractorSelecting is XRSocketInteractor);

        public Vector3 Center
        {
            get
            {
                if (m_Renderers.Length == 0)
                    return transform.position;
                var bounds = m_Renderers[0].bounds;
                for (var i = 1; i < m_Renderers.Length; i++)
                    bounds.Encapsulate(m_Renderers[i].bounds);
                return bounds.center;
            }
        }

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Renderers = GetComponentsInChildren<Renderer>();
            m_Grab.selectEntered.AddListener(OnGrabbed);
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);
        void OnDestroy() => m_Grab.selectEntered.RemoveListener(OnGrabbed);

        /// <summary>Marks the item as photographed in place. Fails once it has been picked up.</summary>
        public bool TryDocument()
        {
            if (IsDocumented || HasBeenMoved || IsBagged)
                return false;
            IsDocumented = true;
            return true;
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
                return;

            var progress = TrainingProgress.Instance;
            if (progress != null)
            {
                var problems = new List<string>();
                var penalty = 0;
                if (!m_ReportedGloves && !progress.IsWorn(TrainingProgress.Equipment.Handschuhe))
                {
                    m_ReportedGloves = true;
                    problems.Add("ohne Handschuhe angefasst");
                    penalty += m_NoGlovesPenalty;
                }

                if (!m_ReportedPhoto && !IsDocumented && !HasBeenMoved)
                {
                    m_ReportedPhoto = true;
                    problems.Add("vor dem Fotografieren bewegt");
                    penalty += m_NotPhotographedPenalty;
                }

                if (problems.Count > 0)
                    progress.ReportMistake($"{m_DisplayName} {string.Join(" und ", problems)}", penalty);
            }

            HasBeenMoved = true;
        }

        /// <summary>Releases the item from the hand and fixes it inside <paramref name="container"/>.</summary>
        public void PutInto(Transform container, Vector3 localPosition)
        {
            IsBagged = true;
            if (m_Grab.isSelected)
                m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
            m_Grab.enabled = false;

            if (TryGetComponent<Rigidbody>(out var body))
                body.isKinematic = true;
            foreach (var itemCollider in GetComponentsInChildren<Collider>())
                itemCollider.enabled = false;

            var scale = transform.lossyScale * m_BaggedScale;
            transform.SetParent(container, false);
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.Euler(m_BaggedRotation);
            transform.localScale = scale;
        }
    }
}
