using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>
    /// Hinged door. Sits on the hinge; pressing grip while pointing at the leaf (near or far) swings it open or
    /// shut. A closed door blocks walking, teleporting and the head. Door handles carry traces, so touching one
    /// without gloves is a mistake.
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

        XRSimpleInteractable m_Interactable;
        Quaternion m_ClosedRotation;
        float m_Angle;
        float m_TargetAngle;
        bool m_ReportedGloves;

        public bool IsOpen => !Mathf.Approximately(m_TargetAngle, 0f);

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_Interactable.selectEntered.AddListener(OnPressed);
            m_ClosedRotation = transform.localRotation;
            if (m_StartsOpen)
            {
                m_Angle = m_TargetAngle = m_OpenAngle;
                Apply();
            }
        }

        void OnDestroy() => m_Interactable.selectEntered.RemoveListener(OnPressed);

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

            if (progress != null && !progress.IsWorn(TrainingProgress.Equipment.Handschuhe))
            {
                if (!m_ReportedGloves)
                {
                    m_ReportedGloves = true;
                    progress.ReportMistake($"{m_DisplayName} ohne Handschuhe angefasst (Türgriff ist Spurenträger)", m_NoGlovesPenalty);
                }
            }
            else if (progress != null)
            {
                // The handle may carry DNA of whoever used it last; these gloves must not touch evidence afterwards.
                progress.NoteGloveContact(this, m_DisplayName);
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
