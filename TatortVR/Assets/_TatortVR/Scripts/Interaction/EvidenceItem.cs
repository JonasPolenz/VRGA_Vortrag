using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// An object at the scene that is marked and photographed where it was found, then picked up with fresh
    /// gloves and sealed in the right kind of <see cref="EvidenceBag"/>. Skipping a step is reported as a mistake.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class EvidenceItem : SceneTrace
    {
        public enum PackagingRule
        {
            Beliebig,
            Papier,
            Kunststoff,
            Abschirmung,
        }

        [Tooltip("Scale inside the bag, so longer items fit.")]
        [SerializeField] float m_BaggedScale = 0.6f;
        [Tooltip("Rotation inside the bag; the default stands items whose long side is their X axis upright.")]
        [SerializeField] Vector3 m_BaggedRotation = new Vector3(0f, 0f, 90f);
        [SerializeField] PackagingRule m_Packaging;
        [Tooltip("Shown when the item is sealed in the wrong packaging.")]
        [SerializeField] string m_PackagingHint = "";
        [SerializeField] int m_NoGlovesPenalty = 10;
        [SerializeField] int m_NotDocumentedPenalty = 10;

        static readonly List<EvidenceItem> s_Items = new List<EvidenceItem>();

        XRGrabInteractable m_Grab;
        Renderer[] m_Renderers;
        bool m_ReportedGloves;

        public static IReadOnlyList<EvidenceItem> All => s_Items;

        public PackagingRule Packaging => m_Packaging;
        public string PackagingHint => m_PackagingHint;
        public bool HasBeenMoved { get; private set; }
        public bool IsBagged { get; private set; }
        public bool IsSample { get; private set; }
        public bool IsHeldByHand => m_Grab.isSelected && !(m_Grab.firstInteractorSelecting is XRSocketInteractor);

        public override bool IsInPlace => !HasBeenMoved && !IsBagged;
        public override bool NeedsOwnDocumentation => !IsSample;

        public override Vector3 Center
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

        protected override void OnEnable()
        {
            base.OnEnable();
            s_Items.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            s_Items.Remove(this);
        }

        void OnDestroy() => m_Grab.selectEntered.RemoveListener(OnGrabbed);

        void OnGrabbed(SelectEnterEventArgs args)
        {
            // A swab only becomes evidence once it has taken a sample.
            if (!isActiveAndEnabled || args.interactorObject is XRSocketInteractor)
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

                if (NeedsOwnDocumentation && !IsDocumented && !HasBeenMoved)
                {
                    problems.Add(Marker == null ? "ohne Spurenmarker und Detailfoto bewegt" : "vor dem Detailfoto bewegt");
                    penalty += m_NotDocumentedPenalty;
                }

                if (problems.Count > 0)
                    progress.ReportMistake($"{DisplayName} {string.Join(" und ", problems)}", penalty);
                progress.NoteSceneChange();
                progress.NoteEvidenceContact(this);
            }

            HasBeenMoved = true;
        }

        /// <summary>Turns a used swab into evidence that shares the marker and photo of the trace it was taken from.</summary>
        public void BecomeSample(SceneTrace source, string displayName)
        {
            m_DisplayName = displayName;
            m_SoilsGloves = source.SoilsGloves;
            IsSample = true;
            HasBeenMoved = true;
            IsDocumented = source.IsDocumented;
            Marker = source.Marker;
            enabled = true;
        }

        /// <summary>Releases the item from the hand and fixes it inside <paramref name="container"/>.</summary>
        public void PutInto(Transform container, Vector3 localPosition)
        {
            IsBagged = true;
            if (m_Grab.isSelected)
                m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
            m_Grab.enabled = false;

            if (TryGetComponent<Rigidbody>(out var body))
            {
                body.isKinematic = true;
                // An interpolated body keeps its own world pose and would stay behind in mid-air when the bag moves.
                body.interpolation = RigidbodyInterpolation.None;
                body.detectCollisions = false;
            }

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
