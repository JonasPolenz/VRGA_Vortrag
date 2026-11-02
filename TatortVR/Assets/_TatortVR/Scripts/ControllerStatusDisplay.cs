using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;

namespace TatortVR
{
    /// <summary>
    /// Shows which headset and controllers the Input System currently sees, so controller detection
    /// and button input can be checked from inside the headset.
    /// </summary>
    public class ControllerStatusDisplay : MonoBehaviour
    {
        [SerializeField] Text m_StatusText;
        [SerializeField] float m_RefreshInterval = 0.2f;

        readonly StringBuilder m_Builder = new StringBuilder();
        float m_NextRefresh;

        void Update()
        {
            if (m_StatusText == null || Time.unscaledTime < m_NextRefresh)
                return;
            m_NextRefresh = Time.unscaledTime + m_RefreshInterval;

            m_Builder.Clear();
            var runtime = OpenXRRuntime.name;
            m_Builder.AppendLine($"OpenXR: {(string.IsNullOrEmpty(runtime) ? "nicht aktiv" : $"{runtime} {OpenXRRuntime.version}")}");
            var hmd = InputSystem.GetDevice<XRHMD>();
            m_Builder.AppendLine($"Brille: {(hmd != null ? hmd.displayName : "nicht erkannt")}");
            m_Builder.AppendLine();
            AppendController("Links", XRController.leftHand);
            AppendController("Rechts", XRController.rightHand);
            m_StatusText.text = m_Builder.ToString();
        }

        void AppendController(string label, XRController controller)
        {
            if (controller == null)
            {
                m_Builder.AppendLine($"{label}: nicht erkannt");
                m_Builder.AppendLine();
                return;
            }

            var tracked = controller.isTracked.isPressed ? "getrackt" : "kein Tracking";
            var trigger = controller.TryGetChildControl<AxisControl>("trigger")?.ReadValue() ?? 0f;
            var grip = controller.TryGetChildControl<AxisControl>("grip")?.ReadValue() ?? 0f;
            var stick = controller.TryGetChildControl<Vector2Control>("thumbstick")?.ReadValue() ?? Vector2.zero;
            m_Builder.AppendLine($"{label}: {controller.displayName} ({tracked})");
            m_Builder.AppendLine($"   Trigger {trigger:0.00}   Grip {grip:0.00}   Stick {stick.x:+0.0;-0.0} / {stick.y:+0.0;-0.0}");
            m_Builder.AppendLine();
        }
    }
}
