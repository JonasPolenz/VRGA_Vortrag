using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Sterile swab. Held into a <see cref="BloodPool"/> it takes a sample and from then on is a piece of
    /// evidence itself: the disabled <see cref="EvidenceItem"/> on the same object is switched on.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable), typeof(EvidenceItem))]
    public class Swab : MonoBehaviour
    {
        [SerializeField] Renderer m_Tip;
        [SerializeField] Color m_UsedColor = new Color(0.45f, 0.03f, 0.03f);
        [SerializeField] string m_SampleName = "Blutprobe";

        XRGrabInteractable m_Grab;
        BloodPool m_Pending;

        public bool IsUsed { get; private set; }

        void Awake() => m_Grab = GetComponent<XRGrabInteractable>();

        void OnTriggerEnter(Collider other)
        {
            if (IsUsed || !m_Grab.isSelected || m_Grab.firstInteractorSelecting is XRSocketInteractor)
                return;
            var pool = other.GetComponentInParent<BloodPool>();
            if (pool != null)
                m_Pending = pool;
        }

        // Outside the physics callback.
        void Update()
        {
            if (m_Pending == null)
                return;
            var pool = m_Pending;
            m_Pending = null;
            if (IsUsed || !pool.TrySample())
                return;

            IsUsed = true;
            if (m_Tip != null)
            {
                var block = new MaterialPropertyBlock();
                m_Tip.GetPropertyBlock(block);
                block.SetColor("_BaseColor", m_UsedColor);
                m_Tip.SetPropertyBlock(block);
            }

            var sample = GetComponent<EvidenceItem>();
            sample.BecomeSample(pool, m_SampleName);
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.NoteEvidenceContact(sample);
        }
    }
}
