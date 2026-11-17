using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>
    /// Hinged door. Sits on the hinge; pressing grip while pointing at the leaf (near or far) swings it open or
    /// shut. A closed door blocks walking, teleporting and the head. Whether a door was found open or shut is
    /// part of the scene, so it has to be photographed before it is moved. Door handles carry traces, so
    /// touching one without gloves is a mistake; with gloves it does not force a glove change.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Door : MonoBehaviour
    {
        [SerializeField] string m_DisplayName = "Tür";
        [Tooltip("Swing angle around the hinge; the sign sets the side the door opens to.")]
        [SerializeField] float m_OpenAngle = 95f;
        [SerializeField] bool m_StartsOpen;
        [SerializeField] float m_Speed = 160f;
        [SerializeField] float m_MaxReach = 2.5f;
        [SerializeField] int m_NoGlovesPenalty = 10;
        [SerializeField] int m_NotDocumentedPenalty = 5;

        static readonly List<Door> s_Doors = new List<Door>();

        XRSimpleInteractable m_Interactable;
        Quaternion m_ClosedRotation;
        float m_Angle;
        float m_TargetAngle;
        bool m_ReportedGloves;
        bool m_HintShown;

        public static IReadOnlyList<Door> All => s_Doors;

        public string DisplayName => m_DisplayName;
        public bool IsOpen => !Mathf.Approximately(m_TargetAngle, 0f);
        public string StateName => IsOpen ? "offen" : "geschlossen";

        /// <summary>The state it was found in has been photographed.</summary>
        public bool IsDocumented { get; private set; }

        /// <summary>Moved before it was photographed; the state it was found in can no longer be documented.</summary>
        public bool WasMovedUndocumented { get; private set; }

        /// <summary>Nothing left to do for this door, one way or the other.</summary>
        public bool IsSettled => IsDocumented || WasMovedUndocumented;

        /// <summary>Middle of the door leaf.</summary>
        public Vector3 Center => m_Interactable != null && m_Interactable.colliders.Count > 0 ? m_Interactable.colliders[0].bounds.center : transform.position;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_Interactable.selectEntered.AddListener(OnPressed);
            m_Interactable.hoverEntered.AddListener(OnHovered);
            m_ClosedRotation = transform.localRotation;
            if (m_StartsOpen)
            {
                m_Angle = m_TargetAngle = m_OpenAngle;
                Apply();
            }
        }

        void OnEnable() => s_Doors.Add(this);
        void OnDisable() => s_Doors.Remove(this);

        void OnDestroy()
        {
            m_Interactable.selectEntered.RemoveListener(OnPressed);
            m_Interactable.hoverEntered.RemoveListener(OnHovered);
        }

        /// <summary>Photo of the door as it was found. Does not count once the door has been moved.</summary>
        public bool TryDocument()
        {
            if (IsSettled)
                return false;
            IsDocumented = true;
            return true;
        }

        // Shown once per door, the first time a hand or ray points at it.
        void OnHovered(HoverEnterEventArgs args)
        {
            if (m_HintShown || IsSettled)
                return;
            m_HintShown = true;
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.ShowMessage($"{m_DisplayName} ({StateName}): erst fotografieren, dann mit Grip bewegen");
        }

        void OnPressed(SelectEnterEventArgs args)
        {
            var progress = TrainingProgress.Instance;
            var hand = args.interactorObject.transform.position;
            var nearest = m_Interactable.colliders.Count > 0 ? m_Interactable.colliders[0].ClosestPoint(hand) : transform.position;
            if (Vector3.Distance(hand, nearest) > m_MaxReach)
            {
                if (progress != null)
                    progress.ShowMessage($"{m_DisplayName}: zu weit entfernt");
                return;
            }

            if (!IsSettled)
            {
                WasMovedUndocumented = true;
                if (progress != null)
                    progress.ReportMistake($"{m_DisplayName} bewegt, bevor ihr Zustand ({StateName}) fotografiert war", m_NotDocumentedPenalty);
            }

            if (progress != null && !progress.IsWorn(TrainingProgress.Equipment.Handschuhe) && !m_ReportedGloves)
            {
                m_ReportedGloves = true;
                progress.ReportMistake($"{m_DisplayName} ohne Handschuhe angefasst (Türgriff ist Spurenträger)", m_NoGlovesPenalty);
            }

            Toggle();
        }

        public void Toggle() => m_TargetAngle = IsOpen ? 0f : m_OpenAngle;

        void Update()
        {
            if (Mathf.Approximately(m_Angle, m_TargetAngle))
                return;
            m_Angle = Mathf.MoveTowards(m_Angle, m_TargetAngle, m_Speed * Time.deltaTime);
            Apply();
        }

        void Apply() => transform.localRotation = m_ClosedRotation * Quaternion.Euler(0f, m_Angle, 0f);
    }
}
