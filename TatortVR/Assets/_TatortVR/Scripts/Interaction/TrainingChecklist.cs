using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TatortVR
{
    /// <summary>
    /// Board that ticks off the work steps as <see cref="TrainingProgress"/> changes and lists the score and the
    /// latest log entries. Once the training is complete it shows the evaluation with every mistake.
    /// </summary>
    public class TrainingChecklist : MonoBehaviour
    {
        [SerializeField] TrainingProgress m_Progress;
        [SerializeField] Text m_Text;
        [SerializeField] int m_LogLines = 3;
        [SerializeField] int m_MaxMistakeLines = 12;
        [Tooltip("Short lines without the log, for the overlay in the field of view.")]
        [SerializeField] bool m_Compact;

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

            // Plain '\n': AppendLine would add '\r' on Windows, which UI Text draws as a glyph.
            var text = new StringBuilder();
            if (m_Compact)
                WriteCompact(text, m_Progress);
            else if (m_Progress.IsComplete)
                WriteEvaluation(text, m_Progress);
            else
                WriteChecklist(text, m_Progress);
            m_Text.text = text.ToString().TrimEnd('\n');
        }

        void WriteCompact(StringBuilder text, TrainingProgress p)
        {
            if (p.IsComplete)
            {
                Line(text, "Auswertung");
                Line(text, $"Zeit {TrainingProgress.FormatTime(p.CompletionTime)}");
                Line(text, $"Punkte {p.Score}/{p.MaxScore}");
                Line(text, p.Grade);
                Line(text, $"Fehler {p.MistakeCount}, Details an der Tafel");
                return;
            }

            Line(text, $"{Box(Done(p.EquipmentWorn, p.EquipmentTotal))} Schutzausrüstung {p.EquipmentWorn}/{p.EquipmentTotal}");
            Line(text, $"{Box(p.OverviewDone)} Übersichtsfoto");
            Line(text, $"{Box(Done(p.MarkedCount, p.TraceTotal))} Marker {p.MarkedCount}/{p.TraceTotal}");
            Line(text, $"{Box(Done(p.PhotographedCount, p.TraceTotal))} Detailfotos {p.PhotographedCount}/{p.TraceTotal}");
            Line(text, $"{Box(p.SampleCount > 0)} Blutprobe");
            Line(text, $"{Box(Done(p.BaggedCount, p.EvidenceTotal))} Verpackt {p.BaggedCount}/{p.EvidenceTotal}");
            Line(text, $"{Box(Done(p.DepositedCount, p.EvidenceTotal))} In der Kiste {p.DepositedCount}/{p.EvidenceTotal}");
            Line(text, $"{Box(Done(p.LatentDocumentedCount, p.LatentTotal))} UV-Spuren {p.LatentDocumentedCount}/{p.LatentTotal}");
            var gloves = !p.IsWorn(TrainingProgress.Equipment.Handschuhe) ? "keine" : p.GlovesUsed ? "wechseln!" : "frisch";
            Line(text, $"Punkte {p.Score}   Fehler {p.MistakeCount}");
            Line(text, $"Handschuhe: {gloves}");
        }

        void WriteChecklist(StringBuilder text, TrainingProgress p)
        {
            Line(text, "Ablauf");
            Line(text, $"{Box(Done(p.EquipmentWorn, p.EquipmentTotal))} Schutzausrüstung: Anzug, Mundschutz, Handschuhe ({p.EquipmentWorn}/{p.EquipmentTotal})");
            Line(text, $"{Box(p.OverviewDone)} Übersichtsfoto, bevor etwas verändert wird");
            Line(text, $"{Box(Done(p.MarkedCount, p.TraceTotal))} Spurenmarker neben jede Spur ({p.MarkedCount}/{p.TraceTotal})");
            Line(text, $"{Box(Done(p.PhotographedCount, p.TraceTotal))} Detailfoto mit Marker ({p.PhotographedCount}/{p.TraceTotal})");
            Line(text, $"{Box(p.SampleCount > 0)} Blutprobe mit Tupfer nehmen");
            Line(text, $"{Box(Done(p.BaggedCount, p.EvidenceTotal))} Einzeln und richtig verpacken ({p.BaggedCount}/{p.EvidenceTotal})");
            Line(text, $"{Box(Done(p.DepositedCount, p.EvidenceTotal))} In die Asservatenkiste legen, sie ist tragbar ({p.DepositedCount}/{p.EvidenceTotal})");
            Line(text, $"{Box(Done(p.LatentDocumentedCount, p.LatentTotal))} UV-Spuren finden und fotografieren ({p.LatentDocumentedCount}/{p.LatentTotal})");
            Line(text, "");
            var gloves = !p.IsWorn(TrainingProgress.Equipment.Handschuhe) ? "keine" : p.GlovesUsed ? "benutzt, wechseln" : "frisch";
            Line(text, $"Punkte: {p.Score}/{p.MaxScore}   Fehler: {p.MistakeCount}   Handschuhe: {gloves}");
            foreach (var entry in p.Log.Skip(Mathf.Max(0, p.Log.Count - m_LogLines)))
                Line(text, $"{TrainingProgress.FormatTime(entry.Time)}  {entry.Text}");
        }

        void WriteEvaluation(StringBuilder text, TrainingProgress p)
        {
            Line(text, "Auswertung");
            Line(text, $"Abgeschlossen in {TrainingProgress.FormatTime(p.CompletionTime)}");
            Line(text, $"Punkte: {p.Score}/{p.MaxScore} ({p.Grade})");
            Line(text, "");
            var mistakes = p.Log.Where(entry => entry.IsMistake).ToList();
            if (mistakes.Count == 0)
            {
                Line(text, "Keine Fehler. Saubere Spurensicherung!");
                return;
            }

            Line(text, $"{mistakes.Count} Fehler:");
            foreach (var entry in mistakes.Take(m_MaxMistakeLines))
                Line(text, $"{TrainingProgress.FormatTime(entry.Time)}  {entry.Text.Replace("Fehler ", "")}");
            if (mistakes.Count > m_MaxMistakeLines)
                Line(text, $"... und {mistakes.Count - m_MaxMistakeLines} weitere");
        }

        static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

        static bool Done(int count, int total) => total > 0 && count >= total;

        static string Box(bool done) => done ? "[x]" : "[  ]";
    }
}
