using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>
    /// Box for handing in sealed evidence bags. A sealed bag brought into the trigger is laid flat on the stack
    /// and entered in the evidence list. The box can be carried to where the work is done, but putting it
    /// down on a trace is a mistake.
    /// </summary>
    public class EvidenceBox : MonoBehaviour
    {
        [Tooltip("Bottom of the box; bags are stacked upwards from here.")]
        [SerializeField] Transform m_Floor;
        [SerializeField] float m_LayerHeight = 0.03f;
        [Tooltip("Optional board listing what has been handed in.")]
        [SerializeField] Text m_List;

        [Header("Carrying")]
        [Tooltip("The grabbable box this slot belongs to; empty for a box that cannot be moved.")]
        [SerializeField] XRGrabInteractable m_Grab;
        [Tooltip("Width and depth of the box bottom.")]
        [SerializeField] Vector2 m_Footprint = new Vector2(0.6f, 0.46f);
        [SerializeField] int m_OnTracePenalty = 10;

        const float k_RestingSpeed = 0.1f;

        readonly StringBuilder m_Entries = new StringBuilder();
        readonly HashSet<Object> m_Reported = new HashSet<Object>();
        EvidenceBag m_Pending;
        Rigidbody m_Body;
        bool m_WasResting;
        int m_Count;

        void Awake()
        {
            if (m_Grab != null)
                m_Body = m_Grab.GetComponent<Rigidbody>();
            UpdateList();
        }

        void OnTriggerStay(Collider other)
        {
            var bag = other.GetComponentInParent<EvidenceBag>();
            if (bag != null && bag.IsSealed && !bag.IsDeposited)
                m_Pending = bag;
        }

        // Selection changes happen outside the physics callbacks.
        void Update()
        {
            CheckWhereItStands();
            if (m_Pending == null)
                return;
            var bag = m_Pending;
            m_Pending = null;
            if (bag.IsDeposited)
                return;

            // Bags hang from their top edge, so lying flat they extend 0.4 m backwards from the pivot.
            bag.Deposit(m_Floor, new Vector3(0f, ++m_Count * m_LayerHeight, 0.2f), Quaternion.Euler(90f, 0f, 0f));
            m_Entries.Append($"\n{bag.Label}: {bag.Content.DisplayName} ({bag.TypeName})");
            UpdateList();
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterDeposit(bag.Content.DisplayName, bag.Label);
        }

        // Looked at once each time the box comes to rest after being carried or dropped.
        void CheckWhereItStands()
        {
            if (m_Body == null)
                return;
            var resting = !m_Grab.isSelected && m_Body.linearVelocity.sqrMagnitude < k_RestingSpeed * k_RestingSpeed;
            if (resting && !m_WasResting)
                ReportCoveredTraces();
            m_WasResting = resting;
        }

        void ReportCoveredTraces()
        {
            var progress = TrainingProgress.Instance;
            if (progress == null)
                return;

            var box = m_Grab.transform;
            foreach (var pool in BloodPool.All)
            {
                var nearFloor = Mathf.Abs(box.position.y - pool.transform.position.y) < 0.25f;
                if (nearFloor && pool.DistanceToEdge(box.position) < Mathf.Min(m_Footprint.x, m_Footprint.y) / 2f)
                    Report(progress, pool, pool.DisplayName);
            }

            foreach (var item in EvidenceItem.All)
            {
                if (!item.IsBagged && !item.IsHeldByHand && Covers(box, item.Center))
                    Report(progress, item, item.DisplayName);
            }

            foreach (var trace in LatentTrace.All)
            {
                // A latent trace can be several prints spread around its centre.
                foreach (Transform print in trace.transform)
                {
                    if (!Covers(box, print.position))
                        continue;
                    Report(progress, trace, trace.DisplayName);
                    break;
                }
            }
        }

        // Under the bottom of the box, with a little margin around it.
        bool Covers(Transform box, Vector3 point)
        {
            const float margin = 0.03f;
            var local = box.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= m_Footprint.x / 2f + margin && Mathf.Abs(local.z) <= m_Footprint.y / 2f + margin
                && local.y > -0.25f && local.y < 0.2f;
        }

        void Report(TrainingProgress progress, Object trace, string traceName)
        {
            if (m_Reported.Add(trace))
                progress.ReportMistake($"Asservatenkiste auf {traceName} abgestellt", m_OnTracePenalty);
        }

        void UpdateList()
        {
            if (m_List != null)
                m_List.text = m_Count == 0 ? "Asservatenliste\nnoch leer" : "Asservatenliste" + m_Entries;
        }
    }
}
