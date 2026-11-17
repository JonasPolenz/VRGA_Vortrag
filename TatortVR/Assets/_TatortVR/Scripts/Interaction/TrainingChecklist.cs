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
        [Tooltip("The overlay shows the latest deduction cut to this many characters.")]
        [SerializeField] int m_MaxMistakeLength = 62;

        const string k_WarningColor = "#ff8a6a";

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
            Line(text, $"{Box(p.OverviewDone)} Übersicht: {p.OverviewTraces}+ Spuren in 1 Foto");
            Line(text, $"{Box(Done(p.DoorsDocumented, p.DoorTotal))} Türen {p.DoorsDocumented}/{p.DoorTotal}");
            Line(text, $"{Box(Done(p.MarkedCount, p.TraceTotal))} Marker {p.MarkedCount}/{p.TraceTotal}");
            Line(text, $"{Box(Done(p.PhotographedCount, p.TraceTotal))} Detailfotos {p.PhotographedCount}/{p.TraceTotal}");
            Line(text, $"{Box(p.SampleCount > 0)} Blutprobe");
            Line(text, $"{Box(Done(p.BaggedCount, p.EvidenceTotal))} Verpackt {p.BaggedCount}/{p.EvidenceTotal}");
            Line(text, $"{Box(Done(p.DepositedCount, p.EvidenceTotal))} In der Kiste {p.DepositedCount}/{p.EvidenceTotal}");
            Line(text, $"{Box(Done(p.LatentDocumentedCount, p.LatentTotal))} UV-Spuren {p.LatentDocumentedCount}/{p.LatentTotal}");
            var gloves = !p.IsWorn(TrainingProgress.Equipment.Handschuhe) ? "keine"
                : p.GlovesNeedChange ? Colored(k_WarningColor, "BLUTIG, WECHSELN!") : "sauber";
            Line(text, $"Handschuhe: {gloves}");
            // The score counts down from the maximum; the deductions are shown next to it.
            Line(text, p.Penalties == 0
                ? $"Punkte {p.Score}/{p.MaxScore}, kein Abzug"
                : $"Punkte {p.Score}/{p.MaxScore}  {Colored(k_WarningColor, $"Abzug -{p.Penalties} ({p.MistakeCount} Fehler)")}");
            if (p.LastMistake != null)
                Line(text, Colored(k_WarningColor, Shorten($"Zuletzt {p.LastMistake}", m_MaxMistakeLength)));
        }

        void WriteChecklist(StringBuilder text, TrainingProgress p)
        {
            Line(text, "Ablauf");
            Line(text, $"{Box(Done(p.EquipmentWorn, p.EquipmentTotal))} Schutzausrüstung: Anzug, Mundschutz, Handschuhe ({p.EquipmentWorn}/{p.EquipmentTotal})");
            Line(text, $"{Box(p.OverviewDone)} Übersichtsfoto vor jeder Veränderung: mind. {p.OverviewTraces} Spuren in einem Bild");
            Line(text, $"{Box(Done(p.DoorsDocumented, p.DoorTotal))} Jede Tür fotografieren, bevor sie bewegt wird ({p.DoorsDocumented}/{p.DoorTotal})");
            Line(text, $"{Box(Done(p.MarkedCount, p.TraceTotal))} Spurenmarker neben jede Spur, auch neben den Leichnam ({p.MarkedCount}/{p.TraceTotal})");
            Line(text, $"{Box(Done(p.PhotographedCount, p.TraceTotal))} Detailfoto mit Marker ({p.PhotographedCount}/{p.TraceTotal})");
            Line(text, $"{Box(p.SampleCount > 0)} Blutprobe mit Tupfer nehmen");
            Line(text, $"{Box(Done(p.BaggedCount, p.EvidenceTotal))} Einzeln und richtig verpacken ({p.BaggedCount}/{p.EvidenceTotal})");
            Line(text, $"{Box(Done(p.DepositedCount, p.EvidenceTotal))} In die Asservatenkiste legen, sie ist tragbar ({p.DepositedCount}/{p.EvidenceTotal})");
            Line(text, $"{Box(Done(p.LatentDocumentedCount, p.LatentTotal))} UV-Spuren finden und fotografieren ({p.LatentDocumentedCount}/{p.LatentTotal})");
            Line(text, "");
            var gloves = !p.IsWorn(TrainingProgress.Equipment.Handschuhe) ? "keine" : p.GlovesNeedChange ? "blutig, wechseln" : "sauber";
            var deductions = p.Penalties == 0 ? "kein Abzug" : $"Abzug: -{p.Penalties} ({p.MistakeCount} Fehler)";
            Line(text, $"Punkte: {p.Score}/{p.MaxScore}   {deductions}   Handschuhe: {gloves}");
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

        static string Colored(string color, string text) => $"<color={color}>{text}</color>";

        static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text.Substring(0, maxLength - 1).TrimEnd() + "…";

        static bool Done(int count, int total) => total > 0 && count >= total;

        static string Box(bool done) => done ? "[x]" : "[  ]";
    }
}
