using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Blood on the floor: a pool and the drops around it. It is marked and photographed like evidence, sampled
    /// with a <see cref="Swab"/> and must not be stepped into (see <see cref="EvidenceStepGuard"/>). The
    /// transform sits at the centre of the pool and must not be scaled.
    /// </summary>
    public class BloodPool : SceneTrace
    {
        [Tooltip("Half extents of the pool along the local X and Z axes.")]
        [SerializeField] Vector2 m_Radii = new Vector2(0.38f, 0.27f);
        [Tooltip("Local positions of single drops around the pool, e.g. a trail towards the door. They are part of this trace.")]
        [SerializeField] Vector3[] m_Drops = new Vector3[0];
        [SerializeField] float m_DropRadius = 0.04f;
        [SerializeField] int m_NotDocumentedPenalty = 10;

        static readonly List<BloodPool> s_Pools = new List<BloodPool>();

        public static IReadOnlyList<BloodPool> All => s_Pools;

        public bool IsSampled { get; private set; }
        public override bool IsInPlace => true;
        public override Vector3 Center => transform.position;

        protected override void OnEnable()
        {
            base.OnEnable();
            s_Pools.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            s_Pools.Remove(this);
        }

        /// <summary>Horizontal distance from <paramref name="point"/> to the nearest blood, pool or drop; negative inside.</summary>
        public float DistanceToEdge(Vector3 point) => DistanceToEdge(point, out _);

        /// <summary>As above; <paramref name="isDrop"/> tells whether the nearest blood is a single drop.</summary>
        public float DistanceToEdge(Vector3 point, out bool isDrop)
        {
            var local = transform.InverseTransformPoint(point);
            var flat = new Vector2(local.x, local.z);
            var normalized = new Vector2(local.x / m_Radii.x, local.z / m_Radii.y).magnitude;
            var nearest = normalized < 0.001f ? -Mathf.Min(m_Radii.x, m_Radii.y) : flat.magnitude * (1f - 1f / normalized);
            isDrop = false;
            foreach (var drop in m_Drops)
            {
                var distance = Vector2.Distance(flat, new Vector2(drop.x, drop.z)) - m_DropRadius;
                if (distance >= nearest)
                    continue;
                nearest = distance;
                isDrop = true;
            }

            return nearest;
        }

        public override float MarkDistance(Vector3 point) => Mathf.Max(0f, DistanceToEdge(point));

        /// <summary>Takes a sample. The trace should be marked and photographed first.</summary>
        public bool TrySample()
        {
            var progress = TrainingProgress.Instance;
            if (IsSampled)
            {
                if (progress != null)
                    progress.ShowMessage($"{DisplayName} ist schon beprobt");
                return false;
            }

            IsSampled = true;
            if (progress != null)
            {
                if (!IsDocumented)
                    progress.ReportMistake($"{DisplayName} vor Spurenmarker und Detailfoto beprobt", m_NotDocumentedPenalty);
                progress.NoteSceneChange();
                progress.RegisterSample(this);
            }

            return true;
        }
    }
}
