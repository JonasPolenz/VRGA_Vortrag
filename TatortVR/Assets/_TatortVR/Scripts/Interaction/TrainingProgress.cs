using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Tracks the trainee's protective equipment, the evidence work and any mistakes, keeps a timed log
    /// and announces every change. The score starts at the maximum and every mistake deducts points.
    /// </summary>
    public class TrainingProgress : MonoBehaviour
    {
        public enum Equipment
        {
            Anzug,
            Handschuhe,
        }

        public readonly struct LogEntry
        {
            public LogEntry(float time, string text, bool isMistake)
            {
                Time = time;
                Text = text;
                IsMistake = isMistake;
            }

            public float Time { get; }
            public string Text { get; }
            public bool IsMistake { get; }
        }

        [SerializeField] TrainingMessageDisplay m_Messages;
        [SerializeField] Renderer[] m_HandRenderers;
        [SerializeField] Color m_GloveColor = new Color(0.2f, 0.42f, 0.95f);
        [SerializeField] int m_EvidenceTotal;
        [SerializeField] int m_TraceTotal;
        [SerializeField] int m_MaxScore = 100;

        static TrainingProgress s_Instance;

        readonly HashSet<Equipment> m_Worn = new HashSet<Equipment>();
        readonly HashSet<EvidenceItem> m_Photographed = new HashSet<EvidenceItem>();
        readonly List<string> m_Bagged = new List<string>();
        readonly List<string> m_Deposited = new List<string>();
        readonly HashSet<LatentTrace> m_TracesDocumented = new HashSet<LatentTrace>();
        readonly List<LogEntry> m_Log = new List<LogEntry>();
        float m_StartTime;
        int m_LastBagNumber;
        int m_Penalties;
        bool m_HintedMissingTraces;

        public static TrainingProgress Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = FindAnyObjectByType<TrainingProgress>();
                return s_Instance;
            }
        }

        public event Action Changed;

        public int EvidenceTotal => m_EvidenceTotal;
        public int TraceTotal => m_TraceTotal;
        public int MarkedCount => EvidenceItem.All.Count(item => item.Marker != null);
        public int TracesDocumentedCount => m_TracesDocumented.Count;
        public int PhotographedCount => m_Photographed.Count;
        public int BaggedCount => m_Bagged.Count;
        public int DepositedCount => m_Deposited.Count;
        public int MistakeCount { get; private set; }
        public int MaxScore => m_MaxScore;
        public int Score => Mathf.Max(0, m_MaxScore - m_Penalties);
        public int ShotsFired { get; private set; }
        public bool IsComplete { get; private set; }
        public float CompletionTime { get; private set; }
        public float ElapsedTime => Time.time - m_StartTime;
        public IReadOnlyList<LogEntry> Log => m_Log;

        public bool IsWorn(Equipment item) => m_Worn.Contains(item);

        public static string FormatTime(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

        void Awake()
        {
            s_Instance = this;
            m_StartTime = Time.time;
        }

        public void Wear(Equipment item)
        {
            if (!m_Worn.Add(item))
                return;

            if (item == Equipment.Handschuhe)
                TintHands();
            Record(item == Equipment.Handschuhe ? "Handschuhe angezogen" : "Anzug angezogen");
        }

        /// <summary>
        /// Evaluates a photo: evidence still lying where it was found and traces lit by the UV lamp count as documented.
        /// </summary>
        public void RegisterPhoto(int photoNumber, IReadOnlyList<EvidenceItem> inView, IReadOnlyList<LatentTrace> tracesInView)
        {
            var documented = inView.Where(item => item.TryDocument()).ToList();
            foreach (var item in documented)
                m_Photographed.Add(item);
            var documentedTraces = tracesInView.Where(trace => trace.TryDocument()).ToList();
            foreach (var trace in documentedTraces)
                m_TracesDocumented.Add(trace);
            var moved = inView.Where(item => item.HasBeenMoved).ToList();

            var names = documented.Select(item => item.DisplayName).Concat(documentedTraces.Select(trace => trace.DisplayName)).ToList();
            string text;
            if (names.Count > 0)
                text = $"Foto {photoNumber}: {string.Join(", ", names)} dokumentiert";
            else if (moved.Count > 0)
                text = $"Foto {photoNumber}: {Names(moved)} nicht mehr am Fundort";
            else if (inView.Count > 0 || tracesInView.Count > 0)
                text = $"Foto {photoNumber}: schon dokumentiert";
            else
                text = $"Foto {photoNumber}: kein Beweisstück im Bild";
            Record(text);
            if (documentedTraces.Count > 0)
                CheckComplete();
        }

        public void RegisterMarker(int number, EvidenceItem item) => Record($"Spurenmarker {number} bei {item.DisplayName}");

        public void RegisterTraceFound(LatentTrace trace) => Record($"UV-Spur entdeckt: {trace.DisplayName}");

        public int NextBagNumber() => ++m_LastBagNumber;

        public void RegisterBagged(EvidenceItem item, int bagNumber)
        {
            m_Bagged.Add(item.DisplayName);
            Record($"{item.DisplayName} in Beutel Nr. {bagNumber} versiegelt");
        }

        public void RegisterDeposit(string itemName, int bagNumber)
        {
            m_Deposited.Add(itemName);
            Record($"Beutel Nr. {bagNumber} ({itemName}) in der Asservatenkiste");
            CheckComplete();
        }

        // Done once every item is in the evidence box and every hidden trace has been photographed.
        void CheckComplete()
        {
            if (IsComplete || m_EvidenceTotal == 0 || m_Deposited.Count < m_EvidenceTotal)
                return;
            if (m_TracesDocumented.Count < m_TraceTotal)
            {
                if (!m_HintedMissingTraces)
                {
                    m_HintedMissingTraces = true;
                    ShowMessage("Alle Beweisstücke abgegeben. Es fehlen noch verborgene Spuren (UV-Lampe)");
                }

                return;
            }

            IsComplete = true;
            CompletionTime = ElapsedTime;
            Record($"Training abgeschlossen in {FormatTime(CompletionTime)}: {Score}/{m_MaxScore} Punkte");
        }

        public void ReportMistake(string text, int penalty)
        {
            MistakeCount++;
            m_Penalties += penalty;
            Record($"Fehler (-{penalty}): {text}", isMistake: true);
        }

        public void RegisterShot()
        {
            ShotsFired++;
            Changed?.Invoke();
        }

        /// <summary>Shows a notice without writing it to the log, e.g. for hints.</summary>
        public void ShowMessage(string text, bool warning = false)
        {
            Debug.Log($"[Training] {text}");
            if (m_Messages != null)
                m_Messages.Show(text, warning);
        }

        void Record(string text, bool isMistake = false)
        {
            m_Log.Add(new LogEntry(ElapsedTime, text, isMistake));
            ShowMessage(text, isMistake);
            Changed?.Invoke();
        }

        static string Names(IEnumerable<EvidenceItem> items) => string.Join(", ", items.Select(item => item.DisplayName));

        // The controller models stand in for the hands, so gloves colour them like blue nitrile gloves.
        void TintHands()
        {
            var block = new MaterialPropertyBlock();
            foreach (var handRenderer in m_HandRenderers)
            {
                if (handRenderer == null)
                    continue;
                handRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", m_GloveColor);
                block.SetColor("_Color", m_GloveColor);
                handRenderer.SetPropertyBlock(block);
            }
        }
    }
}
