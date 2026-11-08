using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Area of the crime scene. Entering it with the head for the first time without the full protective
    /// equipment counts as a mistake, because it contaminates traces.
    /// </summary>
    public class CrimeSceneZone : MonoBehaviour
    {
        [SerializeField] string m_AreaName = "Tatort";
        [Tooltip("Size of the area in local space, centred on this transform.")]
        [SerializeField] Vector3 m_Size = new Vector3(12f, 2.6f, 10f);

        Transform m_Head;
        bool m_Entered;

        public bool HasBeenEntered => m_Entered;

        void Update()
        {
            if (m_Entered)
                return;
            if (m_Head == null)
            {
                if (Camera.main == null)
                    return;
                m_Head = Camera.main.transform;
            }

            var local = transform.InverseTransformPoint(m_Head.position);
            if (Mathf.Abs(local.x) > m_Size.x / 2f || Mathf.Abs(local.y) > m_Size.y / 2f || Mathf.Abs(local.z) > m_Size.z / 2f)
                return;

            m_Entered = true;
            var progress = TrainingProgress.Instance;
            if (progress == null)
                return;

            var missing = new List<string>();
            if (!progress.IsWorn(TrainingProgress.Equipment.Anzug))
                missing.Add("Schutzanzug");
            if (!progress.IsWorn(TrainingProgress.Equipment.Handschuhe))
                missing.Add("Handschuhe");
            if (missing.Count > 0)
                progress.ReportMistake($"{m_AreaName} ohne {string.Join(" und ", missing)} betreten");
            else
                progress.ShowMessage($"{m_AreaName} betreten");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, m_Size);
        }
    }
}
