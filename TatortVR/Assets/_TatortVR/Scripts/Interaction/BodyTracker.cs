using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Estimates the trainee's body from the headset so items can sit at the hip or be held to the chest.
    /// The body stands below the head and only turns once the head has turned clearly away,
    /// so glancing down at the hip does not swing the holster around.
    /// </summary>
    public class BodyTracker : MonoBehaviour
    {
        [SerializeField] Transform m_Head;
        [SerializeField] Transform m_Floor;
        [SerializeField] Transform m_Hip;
        [Tooltip("Optional holster on the right hip, mirrored from the left one.")]
        [SerializeField] Transform m_RightHip;
        [SerializeField] Transform m_Chest;
        [Tooltip("Optional mount in front of the belly, like a camera on a neck strap.")]
        [SerializeField] Transform m_Front;
        [SerializeField] float m_FrontBelowEyes = 0.55f;
        [SerializeField] float m_FrontForward = 0.17f;
        [SerializeField] Vector3 m_HipOffset = new Vector3(-0.2f, 0f, 0.05f);
        [SerializeField, Range(0.3f, 0.8f)] float m_HipHeightRatio = 0.55f;
        [SerializeField] float m_ChestBelowEyes = 0.35f;
        [SerializeField] float m_NeckOffset = 0.08f;
        [SerializeField] float m_TurnThreshold = 40f;
        [SerializeField] float m_TurnSpeed = 240f;

        float m_Yaw;
        bool m_Turning;

        void OnEnable()
        {
            if (m_Head != null)
                m_Yaw = HeadYaw();
        }

        void LateUpdate()
        {
            if (m_Head == null)
                return;

            var headYaw = HeadYaw();
            var delta = Mathf.DeltaAngle(m_Yaw, headYaw);
            if (Mathf.Abs(delta) > m_TurnThreshold)
                m_Turning = true;
            if (m_Turning)
            {
                m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, headYaw, m_TurnSpeed * Time.deltaTime);
                m_Turning = Mathf.Abs(Mathf.DeltaAngle(m_Yaw, headYaw)) > 2f;
            }

            var rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            var floorY = m_Floor != null ? m_Floor.position.y : 0f;
            var eyeHeight = Mathf.Max(m_Head.position.y - floorY, 0.5f);
            var position = m_Head.position - rotation * Vector3.forward * m_NeckOffset;
            position.y = floorY;
            transform.SetPositionAndRotation(position, rotation);

            var hipHeight = Vector3.up * (eyeHeight * m_HipHeightRatio);
            if (m_Hip != null)
                m_Hip.localPosition = m_HipOffset + hipHeight;
            if (m_RightHip != null)
                m_RightHip.localPosition = Vector3.Scale(m_HipOffset, new Vector3(-1f, 1f, 1f)) + hipHeight;
            if (m_Chest != null)
                m_Chest.localPosition = new Vector3(0f, eyeHeight - m_ChestBelowEyes, 0.05f);
            if (m_Front != null)
                m_Front.localPosition = new Vector3(0f, eyeHeight - m_FrontBelowEyes, m_FrontForward);
        }

        float HeadYaw()
        {
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            // Looking straight down, the top of the head points the way the body faces.
            if (forward.sqrMagnitude < 0.04f)
                forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }
    }
}
