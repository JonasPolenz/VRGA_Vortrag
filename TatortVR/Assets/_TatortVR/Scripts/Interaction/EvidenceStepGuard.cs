using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TatortVR
{
    /// <summary>
    /// Watches where the trainee stands. Coming close to evidence or blood on the floor shows a warning,
    /// standing on it (walking or teleporting) costs points. After stepping into blood the shoes leave
    /// footprints for a few steps. Only counts while standing upright, because bending down to pick an item
    /// up puts the head above it while the feet stay behind.
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
        [Tooltip("Head height relative to the standing height below which the trainee counts as bending down.")]
        [SerializeField, Range(0.5f, 1f)] float m_UprightRatio = 0.8f;
        [Tooltip("A head height only counts as standing once it has been held this long; lifting the headset over the head while putting it on does not.")]
        [SerializeField] float m_StandingHoldTime = 3f;
        [Tooltip("Above this head height the trainee always counts as upright, however tall the standing height was measured.")]
        [SerializeField] float m_AlwaysUprightHeight = 1.4f;
        [SerializeField] int m_Penalty = 15;
        [Header("Blood")]
        [SerializeField] int m_BloodPenalty = 15;
        [Tooltip("How far a foot reaches beyond the point below the head.")]
        [SerializeField] float m_FootReach = 0.1f;
        [SerializeField] Material m_FootprintMaterial;
        [SerializeField] int m_FootprintCount = 8;
        [SerializeField] float m_StrideLength = 0.6f;

        class State
        {
            public bool Warned;
            public bool SteppedOn;
        }

        readonly Dictionary<Object, State> m_States = new Dictionary<Object, State>();
        float m_StandingHeight = 1.2f;
        float m_RaisedSince = -1f;
        float m_RaisedMin;
        int m_FootprintsLeft;
        Vector3 m_LastPrint;
        Transform m_PrintRoot;

        public int FootprintsLeft => m_FootprintsLeft;

        // LateUpdate: after HeadCollision has kept the head out of walls and closed doors.
        void LateUpdate()
        {
            if (m_Head == null && Camera.main != null)
                m_Head = Camera.main.transform;
            if (m_Head == null)
                return;

            var floorY = m_Floor != null ? m_Floor.position.y : 0f;
            var headHeight = m_Head.position.y - floorY;
            MeasureStandingHeight(headHeight);
            var upright = headHeight >= Mathf.Min(m_StandingHeight * m_UprightRatio, m_AlwaysUprightHeight);
            var progress = TrainingProgress.Instance;
            if (progress == null)
                return;

            foreach (var item in EvidenceItem.All)
            {
                if (item.IsBagged || item.IsHeldByHand)
                    continue;
                var center = item.Center;
                if (center.y - floorY > m_MaxItemHeight)
                    continue;
                var offset = center - m_Head.position;
                offset.y = 0f;
                Check(item, offset.magnitude - m_StepRadius, upright, progress,
                    $"Vorsicht: {item.DisplayName} am Boden!", $"Auf {item.DisplayName} getreten", m_Penalty, false);
            }

            foreach (var pool in BloodPool.All)
            {
                // The drops around a pool count like the pool itself.
                var distance = pool.DistanceToEdge(m_Head.position, out var isDrop) - m_FootReach;
                Check(pool, distance, upright, progress, "Vorsicht: Blut am Boden!",
                    isDrop ? $"In Blutstropfen bei der {pool.DisplayName} getreten" : $"In die {pool.DisplayName} getreten", m_BloodPenalty, true);
            }

            LeaveFootprints(upright);
        }

        // The standing height is the greatest head height that was held for a while. Taking the plain maximum
        // would let one moment with the headset held overhead make everyone count as bending down afterwards.
        void MeasureStandingHeight(float headHeight)
        {
            if (headHeight <= m_StandingHeight)
            {
                m_RaisedSince = -1f;
                return;
            }

            if (m_RaisedSince < 0f)
            {
                m_RaisedSince = Time.time;
                m_RaisedMin = headHeight;
                return;
            }

            m_RaisedMin = Mathf.Min(m_RaisedMin, headHeight);
            if (Time.time - m_RaisedSince < m_StandingHoldTime)
                return;
            m_StandingHeight = m_RaisedMin;
            m_RaisedSince = -1f;
        }

        // 'distance' is how far the feet are from touching the trace; zero or less means standing on it.
        void Check(Object trace, float distance, bool upright, TrainingProgress progress, string warning, string mistake, int penalty, bool isBlood)
        {
            if (!m_States.TryGetValue(trace, out var state))
                m_States[trace] = state = new State();

            if (distance >= m_WarnRadius - m_StepRadius)
            {
                // Walked away: the next approach warns and counts again.
                state.Warned = false;
                state.SteppedOn = false;
                return;
            }

            if (!upright)
                return;
            if (distance < 0f && !state.SteppedOn)
            {
                state.SteppedOn = true;
                progress.ReportMistake(mistake, penalty);
                if (isBlood)
                {
                    m_FootprintsLeft = m_FootprintCount;
                    m_LastPrint = m_Head.position;
                    progress.ShowMessage("Blut an den Schuhen: Spuren werden verschleppt", warning: true);
                }
            }
            else if (!state.Warned && !state.SteppedOn)
            {
                state.Warned = true;
                progress.ShowMessage(warning, warning: true);
            }
        }

        void LeaveFootprints(bool upright)
        {
            if (m_FootprintsLeft <= 0 || !upright || m_FootprintMaterial == null)
                return;
            var step = m_Head.position - m_LastPrint;
            step.y = 0f;
            if (step.magnitude < m_StrideLength)
                return;
            m_LastPrint = m_Head.position;

            var direction = step.normalized;
            var side = Vector3.Cross(Vector3.up, direction) * (m_FootprintsLeft % 2 == 0 ? 0.09f : -0.09f);
            if (!Physics.Raycast(m_Head.position + side, Vector3.down, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;

            if (m_PrintRoot == null)
                m_PrintRoot = new GameObject("Verschleppte Blutspuren").transform;
            var print = GameObject.CreatePrimitive(PrimitiveType.Quad);
            print.name = "Fussabdruck";
            Destroy(print.GetComponent<Collider>());
            var printRenderer = print.GetComponent<Renderer>();
            printRenderer.sharedMaterial = m_FootprintMaterial;
            printRenderer.shadowCastingMode = ShadowCastingMode.Off;
            // Each print is a little fainter, shown as a smaller mark.
            var fade = Mathf.Lerp(0.45f, 1f, m_FootprintsLeft / (float)m_FootprintCount);
            print.transform.SetParent(m_PrintRoot, false);
            print.transform.SetPositionAndRotation(hit.point + Vector3.up * 0.003f, Quaternion.LookRotation(Vector3.down, direction));
            print.transform.localScale = new Vector3(0.1f * fade, 0.27f * fade, 1f);
            m_FootprintsLeft--;
        }
    }
}
