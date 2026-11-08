using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TatortVR
{
    /// <summary>
    /// Short notice in front of the trainee. It follows the head lazily instead of being locked to it,
    /// which is easier on the eyes in VR, and fades out after a few seconds. Notices arriving while another
    /// one is shown wait in a queue, so warnings are not overwritten.
    /// </summary>
    public class TrainingMessageDisplay : MonoBehaviour
    {
        [SerializeField] Text m_Text;
        [SerializeField] Image m_Background;
        [SerializeField] CanvasGroup m_Group;
        [SerializeField] Transform m_Head;
        [SerializeField] float m_Distance = 1.1f;
        [SerializeField] float m_HeightOffset = -0.2f;
        [SerializeField] float m_Duration = 3f;
        [SerializeField] float m_FollowSpeed = 4f;
        [SerializeField] Color m_InfoColor = new Color(0.1f, 0.35f, 0.15f, 0.92f);
        [SerializeField] Color m_WarningColor = new Color(0.55f, 0.12f, 0.05f, 0.92f);

        [Tooltip("How long a notice stays at least before a queued one replaces it.")]
        [SerializeField] float m_MinDuration = 1.5f;
        [SerializeField] int m_MaxQueued = 4;

        const float k_FadeTime = 0.3f;

        readonly Queue<(string Text, bool Warning)> m_Queue = new Queue<(string, bool)>();
        float m_ShownAt = float.NegativeInfinity;

        void Awake()
        {
            if (m_Group != null)
                m_Group.alpha = 0f;
        }

        public void Show(string text, bool warning = false)
        {
            if (Time.time - m_ShownAt >= m_Duration)
            {
                Display(text, warning);
                return;
            }

            if (m_Text.text != text && m_Queue.Count < m_MaxQueued)
                m_Queue.Enqueue((text, warning));
        }

        void Display(string text, bool warning)
        {
            m_Text.text = text;
            if (m_Background != null)
                m_Background.color = warning ? m_WarningColor : m_InfoColor;
            m_ShownAt = Time.time;
            if (TryGetTargetPose(out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        void LateUpdate()
        {
            if (m_Queue.Count > 0 && Time.time - m_ShownAt >= m_MinDuration)
            {
                var next = m_Queue.Dequeue();
                Display(next.Text, next.Warning);
            }

            var age = Time.time - m_ShownAt;
            var visible = age < m_Duration;
            if (m_Group != null)
                m_Group.alpha = visible ? Mathf.Clamp01(Mathf.Min(age, m_Duration - age) / k_FadeTime) : 0f;
            if (!visible || !TryGetTargetPose(out var position, out var rotation))
                return;

            var t = 1f - Mathf.Exp(-m_FollowSpeed * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, t), Quaternion.Slerp(transform.rotation, rotation, t));
        }

        bool TryGetTargetPose(out Vector3 position, out Quaternion rotation)
        {
            if (m_Head == null && Camera.main != null)
                m_Head = Camera.main.transform;
            if (m_Head == null)
            {
                position = default;
                rotation = default;
                return false;
            }

            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
            forward.Normalize();
            position = m_Head.position + forward * m_Distance + Vector3.up * m_HeightOffset;
            rotation = Quaternion.LookRotation(position - m_Head.position);
            return true;
        }
    }
}
