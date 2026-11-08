using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Watches where the trainee stands. Coming close to evidence lying on the floor shows a warning,
    /// standing on it (walking or teleporting) costs points. Only counts while standing upright, because
    /// bending down to pick an item up puts the head above it while the feet stay behind.
    /// </summary>
    public class EvidenceStepGuard : MonoBehaviour
    {
        [SerializeField] Transform m_Head;
        [Tooltip("Floor reference, usually the XR Origin.")]
        [SerializeField] Transform m_Floor;
        [SerializeField] float m_StepRadius = 0.22f;
        [SerializeField] float m_WarnRadius = 0.5f;
        [Tooltip("Evidence higher than this above the floor (on furniture) cannot be stepped on.")]
        [SerializeField] float m_MaxItemHeight = 0.3f;
        [Tooltip("Head height relative to the tallest seen so far below which the trainee counts as bending down.")]
        [SerializeField, Range(0.5f, 1f)] float m_UprightRatio = 0.8f;
        [SerializeField] int m_Penalty = 15;

        class ItemState
        {
            public bool Warned;
            public bool SteppedOn;
        }

        readonly Dictionary<EvidenceItem, ItemState> m_States = new Dictionary<EvidenceItem, ItemState>();
        float m_StandingHeight = 1.2f;

        void Update()
        {
            if (m_Head == null && Camera.main != null)
                m_Head = Camera.main.transform;
            if (m_Head == null)
                return;

            var floorY = m_Floor != null ? m_Floor.position.y : 0f;
            var headHeight = m_Head.position.y - floorY;
            m_StandingHeight = Mathf.Max(m_StandingHeight, headHeight);
            var upright = headHeight >= m_StandingHeight * m_UprightRatio;

            foreach (var item in EvidenceItem.All)
            {
                if (item.IsBagged || item.IsHeldByHand)
                    continue;
                var center = item.Center;
                if (center.y - floorY > m_MaxItemHeight)
                    continue;

                if (!m_States.TryGetValue(item, out var state))
                    m_States[item] = state = new ItemState();

                var offset = center - m_Head.position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance >= m_WarnRadius)
                {
                    // Walked away: the next approach warns and counts again.
                    state.Warned = false;
                    state.SteppedOn = false;
                    continue;
                }

                if (!upright)
                    continue;
                var progress = TrainingProgress.Instance;
                if (progress == null)
                    continue;

                if (distance < m_StepRadius && !state.SteppedOn)
                {
                    state.SteppedOn = true;
                    progress.ReportMistake($"Auf {item.DisplayName} getreten", m_Penalty);
                }
                else if (!state.Warned && !state.SteppedOn)
                {
                    state.Warned = true;
                    progress.ShowMessage($"Vorsicht: {item.DisplayName} am Boden!", warning: true);
                }
            }
        }
    }
}
