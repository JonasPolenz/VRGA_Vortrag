using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TatortVR
{
    /// <summary>
    /// Board that ticks off the training steps as <see cref="TrainingProgress"/> changes
    /// and lists the mistakes and latest log entries.
    /// </summary>
    public class TrainingChecklist : MonoBehaviour
    {
        [SerializeField] TrainingProgress m_Progress;
        [SerializeField] Text m_Text;
        [SerializeField] int m_LogLines = 4;

        void OnEnable()
        {
            if (m_Progress == null)
                m_Progress = TrainingProgress.Instance;
            if (m_Progress != null)
                m_Progress.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (m_Progress != null)
                m_Progress.Changed -= Refresh;
        }

        /// <summary>Rewrites the board; also called by the scene builder so the board is filled in the editor.</summary>
        public void Refresh()
        {
            if (m_Text == null || m_Progress == null)
                return;

            var p = m_Progress;
            var total = p.EvidenceTotal;
            // Plain '\n': AppendLine would add '\r' on Windows, which UI Text draws as a glyph.
            var text = new StringBuilder("Ablauf\n");
            Line(text, $"{Box(p.IsWorn(TrainingProgress.Equipment.Anzug))} Schutzanzug anziehen (an den Körper halten)");
            Line(text, $"{Box(p.IsWorn(TrainingProgress.Equipment.Handschuhe))} Handschuhe anziehen (an die andere Hand)");
            Line(text, $"{Box(Done(p.PhotographedCount, total))} Beweismittel am Fundort fotografieren ({p.PhotographedCount}/{total})");
            Line(text, $"{Box(Done(p.BaggedCount, total))} Einzeln eintüten, Beutel an der linken Hüfte ({p.BaggedCount}/{total})");
            Line(text, $"{Box(Done(p.DepositedCount, total))} Beutel in die Asservatenkiste legen ({p.DepositedCount}/{total})");
            Line(text, "");
            Line(text, p.IsComplete
                ? $"Abgeschlossen in {TrainingProgress.FormatTime(p.CompletionTime)}, Fehler: {p.MistakeCount}"
                : $"Fehler bisher: {p.MistakeCount}");
            foreach (var entry in p.Log.Skip(Mathf.Max(0, p.Log.Count - m_LogLines)))
                Line(text, $"{TrainingProgress.FormatTime(entry.Time)}  {entry.Text}");
            m_Text.text = text.ToString().TrimEnd('\n');
        }

        static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

        static bool Done(int count, int total) => total > 0 && count >= total;

        static string Box(bool done) => done ? "[x]" : "[  ]";
    }
}
