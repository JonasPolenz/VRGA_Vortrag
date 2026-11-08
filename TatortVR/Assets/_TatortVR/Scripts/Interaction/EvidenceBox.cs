using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Box for handing in sealed evidence bags. A sealed bag brought into the trigger is laid flat on the stack.
    /// </summary>
    public class EvidenceBox : MonoBehaviour
    {
        [Tooltip("Bottom of the box; bags are stacked upwards from here.")]
        [SerializeField] Transform m_Floor;
        [SerializeField] float m_LayerHeight = 0.03f;

        EvidenceBag m_Pending;
        int m_Count;

        void OnTriggerStay(Collider other)
        {
            var bag = other.GetComponentInParent<EvidenceBag>();
            if (bag != null && bag.IsSealed && !bag.IsDeposited)
                m_Pending = bag;
        }

        // Selection changes happen outside the physics callbacks.
        void Update()
        {
            if (m_Pending == null)
                return;
            var bag = m_Pending;
            m_Pending = null;
            if (bag.IsDeposited)
                return;

            // Bags hang from their top edge, so lying flat they extend 0.4 m backwards from the pivot.
            bag.Deposit(m_Floor, new Vector3(0f, ++m_Count * m_LayerHeight, 0.2f), Quaternion.Euler(90f, 0f, 0f));
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterDeposit(bag.Content.DisplayName, bag.Number);
        }
    }
}
