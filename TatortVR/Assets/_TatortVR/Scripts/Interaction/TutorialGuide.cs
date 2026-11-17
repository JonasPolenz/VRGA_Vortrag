using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Onboarding at the start: one instruction at a time in the field of view, each ticked off as soon as the
    /// trainee has actually done it (walk, turn, teleport, grab, trigger, overlay). It runs once per app start;
    /// restarting the training does not repeat it, and a button skips it.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        [SerializeField] GameObject m_Panel;
        [SerializeField] Text m_Text;
        [Tooltip("The XR Origin; walking, turning and teleporting are read from how it moves.")]
        [SerializeField] Transform m_Rig;
        [SerializeField] PhotoCamera m_Camera;
        [SerializeField] TrainingHud m_Hud;
        [SerializeField] XRSimpleInteractable m_SkipButton;
        [SerializeField] float m_WalkDistance = 1.5f;
        [SerializeField] float m_TurnAngle = 40f;
        [Tooltip("Moving further than this within one frame is a teleport, not walking.")]
        [SerializeField] float m_TeleportDistance = 0.6f;
        [SerializeField] float m_ClosingTime = 10f;

        enum Step { LookAround, Walk, Turn, Teleport, Grab, Photo, Overlay, Closing, Done }

        static bool s_Completed;

        Step m_Step;
        float m_StepStartedAt;
        Vector3 m_LastPosition;
        float m_LastYaw;
        float m_Walked;
        float m_Turned;
        bool m_Teleported;
        int m_PhotosAtStart;
        int m_TogglesAtStart;

        public bool IsRunning => m_Step != Step.Done;

        // Statics survive when the editor enters Play mode without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Completed = false;

        void Start()
        {
            if (m_SkipButton != null)
                m_SkipButton.selectEntered.AddListener(OnSkipPressed);
            if (s_Completed || m_Rig == null)
            {
                Finish();
                return;
            }

            Begin(Step.LookAround);
        }

        void OnDestroy()
        {
            if (m_SkipButton != null)
                m_SkipButton.selectEntered.RemoveListener(OnSkipPressed);
        }

        void OnSkipPressed(SelectEnterEventArgs args) => Skip();

        public void Skip()
        {
            if (IsRunning)
                Finish();
        }

        void Update()
        {
            if (!IsRunning)
                return;
            TrackMovement();
            if (IsStepDone())
                Begin(m_Step + 1);
        }

        void TrackMovement()
        {
            var position = m_Rig.position;
            var moved = position - m_LastPosition;
            moved.y = 0f;
            if (moved.magnitude > m_TeleportDistance)
                m_Teleported = true;
            else
                m_Walked += moved.magnitude;
            m_LastPosition = position;

            var yaw = m_Rig.eulerAngles.y;
            m_Turned += Mathf.Abs(Mathf.DeltaAngle(m_LastYaw, yaw));
            m_LastYaw = yaw;
        }

        bool IsStepDone()
        {
            var age = Time.time - m_StepStartedAt;
            switch (m_Step)
            {
                case Step.LookAround: return age >= 5f;
                case Step.Walk: return m_Walked >= m_WalkDistance;
                case Step.Turn: return m_Turned >= m_TurnAngle;
                case Step.Teleport: return m_Teleported;
                case Step.Grab: return m_Camera == null || IsHeld(m_Camera);
                case Step.Photo: return m_Camera == null || m_Camera.PhotoCount > m_PhotosAtStart;
                case Step.Overlay: return m_Hud == null || m_Hud.ToggleCount > m_TogglesAtStart;
                case Step.Closing: return age >= m_ClosingTime;
                default: return false;
            }
        }

        static bool IsHeld(PhotoCamera camera)
        {
            var grab = camera.GetComponent<XRGrabInteractable>();
            return grab.isSelected && !(grab.firstInteractorSelecting is XRSocketInteractor);
        }

        void Begin(Step step)
        {
            if (step >= Step.Done)
            {
                Finish();
                return;
            }

            m_Step = step;
            m_StepStartedAt = Time.time;
            m_LastPosition = m_Rig.position;
            m_LastYaw = m_Rig.eulerAngles.y;
            m_Walked = 0f;
            m_Turned = 0f;
            m_Teleported = false;
            m_PhotosAtStart = m_Camera != null ? m_Camera.PhotoCount : 0;
            m_TogglesAtStart = m_Hud != null ? m_Hud.ToggleCount : 0;

            if (m_Panel != null)
                m_Panel.SetActive(true);
            if (m_Text != null)
                m_Text.text = $"Einführung {(int)step + 1}/{(int)Step.Done}\n{Instruction(step)}";
        }

        static string Instruction(Step step)
        {
            switch (step)
            {
                case Step.LookAround: return "Willkommen. Sieh dich um: links der Ausrüstungstisch, rechts der Einsatzauftrag.";
                case Step.Walk: return "Gehen: linken Stick bewegen. Geh ein paar Schritte.";
                case Step.Turn: return "Drehen: rechten Stick nach links oder rechts kippen.";
                case Step.Teleport: return "Teleportieren: rechten Stick nach vorn drücken, zielen, loslassen.";
                case Step.Grab: return "Greifen: Grip-Taste (Mittelfinger). Nimm die Kamera vom Tisch.";
                case Step.Photo: return "Benutzen: Trigger (Zeigefinger). Mach ein Probefoto, das Bild siehst du auf der Kamera.";
                case Step.Overlay: return "Links im Blick stehen Ablauf und Punkte. Die X-Taste links blendet sie aus und ein.";
                default: return "Losgelassen hängt die Kamera am Brustgurt. Lies den Einsatzauftrag und leg die Schutzausrüstung an, bevor du die Absperrung passierst.";
            }
        }

        void Finish()
        {
            m_Step = Step.Done;
            s_Completed = true;
            if (m_Panel != null)
                m_Panel.SetActive(false);
            // The time on the evaluation should not include the introduction.
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RestartClock();
        }
    }
}
