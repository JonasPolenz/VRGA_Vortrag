using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Numbered evidence marker. When let go it stands upright on the surface below and is assigned to the
    /// nearest unmarked <see cref="SceneTrace"/>. Picking it up again removes the assignment.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class EvidenceMarker : MonoBehaviour
    {
        [SerializeField] Text[] m_Labels = new Text[0];
        [SerializeField] float m_AssignRadius = 0.6f;

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Collider[] m_Colliders;
        bool m_PlacePending;

        public int Number { get; private set; }
        public SceneTrace MarkedTrace { get; private set; }
        public bool IsPlaced { get; private set; }

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
            m_Colliders = GetComponentsInChildren<Collider>();
            m_Grab.selectEntered.AddListener(OnGrabbed);
            m_Grab.selectExited.AddListener(OnReleased);
        }

        void OnDestroy()
        {
            m_Grab.selectEntered.RemoveListener(OnGrabbed);
            m_Grab.selectExited.RemoveListener(OnReleased);
        }

        public void SetNumber(int number)
        {
            Number = number;
            foreach (var label in m_Labels)
            {
                if (label != null)
                    label.text = number.ToString();
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
                return;
            IsPlaced = false;
            // A trace that is already photographed keeps its number.
            if (MarkedTrace != null && !MarkedTrace.IsDocumented)
                MarkedTrace.Marker = null;
            MarkedTrace = null;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (!(args.interactorObject is XRSocketInteractor))
                m_PlacePending = true;
        }

        // After the grab interactable has finished detaching.
        void LateUpdate()
        {
            if (!m_PlacePending)
                return;
            m_PlacePending = false;
            Place();
        }

        void Place()
        {
            // Stand upright on whatever is below; the pivot is at the bottom of the marker.
            foreach (var markerCollider in m_Colliders)
                markerCollider.enabled = false;
            if (Physics.Raycast(transform.position + Vector3.up * 0.05f, Vector3.down, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            foreach (var markerCollider in m_Colliders)
                markerCollider.enabled = true;
            if (m_Body != null)
                m_Body.isKinematic = true;
            IsPlaced = true;

            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.NoteSceneChange();
            AssignNearestTrace();
        }

        void AssignNearestTrace()
        {
            SceneTrace nearest = null;
            SceneTrace nearestMarked = null;
            var nearestDistance = m_AssignRadius;
            foreach (var trace in SceneTrace.AllTraces)
            {
                if (!trace.NeedsOwnDocumentation || !trace.IsInPlace)
                    continue;
                var distance = trace.MarkDistance(transform.position);
                if (distance >= m_AssignRadius)
                    continue;
                if (trace.Marker != null)
                {
                    nearestMarked = trace;
                }
                else if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = trace;
                }
            }

            var progress = TrainingProgress.Instance;
            if (nearest != null)
            {
                nearest.Marker = this;
                MarkedTrace = nearest;
                if (progress != null)
                    progress.RegisterMarker(Number, nearest);
            }
            else if (progress != null)
            {
                progress.ShowMessage(nearestMarked != null
                    ? $"{nearestMarked.DisplayName} hat schon Spurenmarker {nearestMarked.Marker.Number}"
                    : $"Spurenmarker {Number} steht bei keiner Spur");
            }
        }
    }
}
