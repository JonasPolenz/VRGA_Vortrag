using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Blood on the floor. It is marked and photographed like evidence, sampled with a <see cref="Swab"/>
    /// and must not be stepped into (see <see cref="EvidenceStepGuard"/>). The transform sits at the centre
    /// of the pool and must not be scaled.
    /// </summary>
    public class BloodPool : SceneTrace
    {
        [Tooltip("Half extents of the pool along the local X and Z axes.")]
        [SerializeField] Vector2 m_Radii = new Vector2(0.38f, 0.27f);
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

        /// <summary>Horizontal distance from <paramref name="point"/> to the edge of the pool; negative inside.</summary>
        public float DistanceToEdge(Vector3 point)
        {
            var local = transform.InverseTransformPoint(point);
            var flat = new Vector2(local.x, local.z);
            var normalized = new Vector2(local.x / m_Radii.x, local.z / m_Radii.y).magnitude;
            if (normalized < 0.001f)
                return -Mathf.Min(m_Radii.x, m_Radii.y);
            return flat.magnitude * (1f - 1f / normalized);
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
