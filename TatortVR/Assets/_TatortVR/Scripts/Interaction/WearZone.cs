using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Trigger volume on a hand, at the chest or at the face. Holding a matching <see cref="WearableItem"/>
    /// inside it for a moment puts the item on.
    /// </summary>
    public class WearZone : MonoBehaviour
    {
        public enum ZoneType
        {
            Hand,
            Body,
            Head,
        }

        [SerializeField] ZoneType m_Type;
        [Tooltip("Controller this hand zone belongs to. Items held by this hand are ignored, gloves go on with the other hand.")]
        [SerializeField] Transform m_HandRoot;
        [SerializeField] float m_Dwell = 0.2f;

        WearableItem m_Candidate;
        float m_CandidateSince;
        bool m_PutOnPending;

        public ZoneType Type => m_Type;

        void OnTriggerStay(Collider other)
        {
            var item = other.GetComponentInParent<WearableItem>();
            if (item == null || !Accepts(item))
                return;

            if (item != m_Candidate)
            {
                m_Candidate = item;
                m_CandidateSince = Time.time;
            }
            else if (Time.time - m_CandidateSince >= m_Dwell)
            {
                m_PutOnPending = true;
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (m_Candidate != null && other.GetComponentInParent<WearableItem>() == m_Candidate)
                m_Candidate = null;
        }

        // Selection changes happen outside the physics callbacks.
        void Update()
        {
            if (!m_PutOnPending)
                return;
            m_PutOnPending = false;
            if (m_Candidate != null && Accepts(m_Candidate))
                m_Candidate.PutOn();
            m_Candidate = null;
        }

        static ZoneType ZoneFor(TrainingProgress.Equipment equipment)
        {
            switch (equipment)
            {
                case TrainingProgress.Equipment.Handschuhe: return ZoneType.Hand;
                case TrainingProgress.Equipment.Mundschutz: return ZoneType.Head;
                default: return ZoneType.Body;
            }
        }

        bool Accepts(WearableItem item)
        {
            if (!item.isActiveAndEnabled || !item.IsHeld || ZoneFor(item.Equipment) != m_Type)
                return false;
            return m_Type != ZoneType.Hand || m_HandRoot == null || !item.Holder.IsChildOf(m_HandRoot);
        }
    }
}
