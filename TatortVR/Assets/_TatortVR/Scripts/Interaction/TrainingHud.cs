using UnityEngine;
using UnityEngine.InputSystem;

namespace TatortVR
{
    /// <summary>
    /// Overlay fixed in the field of view with the work steps and the score, so the trainee does not have to
    /// walk back to the board. The X button of the left controller hides and shows it.
    /// </summary>
    public class TrainingHud : MonoBehaviour
    {
        [Tooltip("What is hidden and shown; the component itself stays active to keep listening for the button.")]
        [SerializeField] GameObject m_Panel;
        [SerializeField] string m_ToggleBinding = "<XRController>{LeftHand}/primaryButton";

        InputAction m_Toggle;

        public bool IsVisible => m_Panel != null && m_Panel.activeSelf;

        /// <summary>Counts how often the overlay was switched, for the tutorial.</summary>
        public int ToggleCount { get; private set; }

        void OnEnable()
        {
            m_Toggle = new InputAction("Overlay", InputActionType.Button, m_ToggleBinding);
            m_Toggle.performed += OnToggle;
            m_Toggle.Enable();
        }

        void OnDisable()
        {
            m_Toggle.performed -= OnToggle;
            m_Toggle.Dispose();
        }

        void OnToggle(InputAction.CallbackContext context) => Toggle();

        public void Toggle()
        {
            if (m_Panel == null)
                return;
            m_Panel.SetActive(!m_Panel.activeSelf);
            ToggleCount++;
        }
    }
}
