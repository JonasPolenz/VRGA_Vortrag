using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Numbered evidence marker. When let go it stands upright on the surface below and is assigned to the
    /// nearest unmarked <see cref="EvidenceItem"/>. Picking it up again removes the assignment.
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
        public EvidenceItem MarkedItem { get; private set; }
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
            if (MarkedItem != null)
            {
                MarkedItem.Marker = null;
                MarkedItem = null;
            }
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
            AssignNearestEvidence();
        }

        void AssignNearestEvidence()
        {
            EvidenceItem nearest = null;
            var nearestDistance = m_AssignRadius;
            foreach (var item in EvidenceItem.All)
            {
                if (item.IsBagged || item.IsHeldByHand)
                    continue;
                var distance = Vector3.Distance(item.Center, transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = item;
                }
            }

            var progress = TrainingProgress.Instance;
            if (nearest != null && nearest.Marker == null)
            {
                nearest.Marker = this;
                MarkedItem = nearest;
                if (progress != null)
                    progress.RegisterMarker(Number, nearest);
            }
            else if (progress != null)
            {
                progress.ShowMessage(nearest != null
                    ? $"{nearest.DisplayName} hat schon Spurenmarker {nearest.Marker.Number}"
                    : $"Spurenmarker {Number} steht bei keinem Beweisstück");
            }
        }
    }
}
