using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Trace that stays where it is and is only marked and photographed: the body, tool marks on a window,
    /// the knife block. Some of them must not be touched at all; for those the object needs trigger colliders
    /// on itself, which the hand zones of the controllers run into.
    /// </summary>
    public class FixedTrace : SceneTrace
    {
        [Tooltip("Half size of the trace around the transform, in local space. A marker counts by how far it stands from this box, seen from above.")]
        [SerializeField] Vector3 m_Extents = new Vector3(0.1f, 0.1f, 0.1f);

        [Header("Touching")]
        [Tooltip("Touching it with a hand is a mistake, e.g. the body before the forensic pathologist has seen it.")]
        [SerializeField] bool m_DoNotTouch;
        [SerializeField] string m_TouchReason = "Lage nicht verändern";
        [SerializeField] int m_TouchPenalty = 15;

        bool m_Touched;

        public override bool IsInPlace => true;
        public override Vector3 Center => transform.position;

        // Height does not matter: a marker on the floor below a trace on a window frame belongs to it.
        public override float MarkDistance(Vector3 point)
        {
            var local = transform.InverseTransformPoint(point);
            var outside = new Vector2(Mathf.Max(0f, Mathf.Abs(local.x) - m_Extents.x), Mathf.Max(0f, Mathf.Abs(local.z) - m_Extents.z));
            return outside.magnitude;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!m_DoNotTouch || m_Touched || !other.TryGetComponent<WearZone>(out var zone) || zone.Type != WearZone.ZoneType.Hand)
                return;
            m_Touched = true;
            var progress = TrainingProgress.Instance;
            if (progress == null)
                return;
            progress.ReportMistake($"{DisplayName} berührt ({m_TouchReason})", m_TouchPenalty);
            progress.NoteSceneChange();
            progress.NoteGloveContact(this, DisplayName);
        }
    }
}
