using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>
    /// Keeps the head out of walls, closed doors and windows. Stick movement is already stopped by the
    /// character controller, but really walking or leaning in the room moves the headset straight through
    /// geometry. When that happens the rig is pushed back, so solid things stay solid.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HeadCollision : MonoBehaviour
    {
        [SerializeField] Transform m_Head;
        [Tooltip("The XR Origin that is moved back when the head runs into something.")]
        [SerializeField] Transform m_Origin;
        [SerializeField] float m_Radius = 0.09f;
        [Tooltip("Head movement larger than this within one frame is a teleport, not walking.")]
        [SerializeField] float m_TeleportDistance = 0.6f;
        [SerializeField] LayerMask m_Mask = 1;

        const float k_Skin = 0.01f;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        Vector3 m_LastValid;
        bool m_HasValid;

        public bool WasBlocked { get; private set; }

        void OnEnable() => m_HasValid = false;

        // Runs before the other LateUpdates (execution order), so rules that look at the head position
        // see it after it has been pushed out of a wall.
        void LateUpdate()
        {
            if (m_Head == null || m_Origin == null)
                return;

            var head = m_Head.position;
            WasBlocked = false;
            if (!m_HasValid)
            {
                m_LastValid = head;
                m_HasValid = true;
                return;
            }

            // Only horizontal movement is checked; ducking under or bending over furniture is fine.
            var start = new Vector3(m_LastValid.x, head.y, m_LastValid.z);
            var delta = head - start;
            var distance = delta.magnitude;
            if (distance < 0.0005f || distance > m_TeleportDistance)
            {
                m_LastValid = head;
                return;
            }

            var direction = delta / distance;
            var count = Physics.SphereCastNonAlloc(start, m_Radius, direction, s_Hits, distance + k_Skin, m_Mask, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                // Distance zero means the sphere already overlapped at the start; nothing sensible to push out of.
                if (s_Hits[i].distance > 0f && s_Hits[i].distance < nearest && IsSolid(s_Hits[i].collider))
                    nearest = s_Hits[i].distance;
            }

            if (!float.IsPositiveInfinity(nearest))
            {
                var allowed = start + direction * Mathf.Max(0f, nearest - k_Skin);
                m_Origin.position += allowed - head;
                Physics.SyncTransforms();
                head = allowed;
                WasBlocked = true;
            }

            m_LastValid = head;
        }

        // The player's own body and anything that can be carried do not block the head.
        bool IsSolid(Collider hit) =>
            !hit.transform.IsChildOf(m_Origin) && hit.GetComponentInParent<XRGrabInteractable>() == null;
    }
}
