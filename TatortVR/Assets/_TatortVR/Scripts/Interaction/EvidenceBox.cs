using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TatortVR
{
    /// <summary>
    /// Box for handing in sealed evidence bags. A sealed bag brought into the trigger is laid flat on the stack
    /// and entered in the evidence list.
    /// </summary>
    public class EvidenceBox : MonoBehaviour
    {
        [Tooltip("Bottom of the box; bags are stacked upwards from here.")]
        [SerializeField] Transform m_Floor;
        [SerializeField] float m_LayerHeight = 0.03f;
        [Tooltip("Optional board listing what has been handed in.")]
        [SerializeField] Text m_List;

        readonly StringBuilder m_Entries = new StringBuilder();
        EvidenceBag m_Pending;
        int m_Count;

        void Awake() => UpdateList();

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
            m_Entries.Append($"\n{bag.Label}: {bag.Content.DisplayName} ({bag.TypeName})");
            UpdateList();
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterDeposit(bag.Content.DisplayName, bag.Label);
        }

        void UpdateList()
        {
            if (m_List != null)
                m_List.text = m_Count == 0 ? "Asservatenliste\nnoch leer" : "Asservatenliste" + m_Entries;
        }
    }
}
