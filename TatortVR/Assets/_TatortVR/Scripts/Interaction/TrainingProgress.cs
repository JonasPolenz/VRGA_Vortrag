using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Tracks the trainee's protective equipment, the evidence work and any mistakes, keeps a timed log
    /// and announces every change.
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

        static TrainingProgress s_Instance;

        readonly HashSet<Equipment> m_Worn = new HashSet<Equipment>();
        readonly HashSet<EvidenceItem> m_Photographed = new HashSet<EvidenceItem>();
        readonly List<string> m_Bagged = new List<string>();
        readonly List<string> m_Deposited = new List<string>();
        readonly List<LogEntry> m_Log = new List<LogEntry>();
        float m_StartTime;
        int m_LastBagNumber;

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
        public int PhotographedCount => m_Photographed.Count;
        public int BaggedCount => m_Bagged.Count;
        public int DepositedCount => m_Deposited.Count;
        public int MistakeCount { get; private set; }
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

        /// <summary>Evaluates a photo: evidence still lying where it was found counts as documented.</summary>
        public void RegisterPhoto(int photoNumber, IReadOnlyList<EvidenceItem> inView)
        {
            var documented = inView.Where(item => item.TryDocument()).ToList();
            foreach (var item in documented)
                m_Photographed.Add(item);
            var moved = inView.Where(item => item.HasBeenMoved).ToList();

            string text;
            if (documented.Count > 0)
                text = $"Foto {photoNumber}: {Names(documented)} dokumentiert";
            else if (moved.Count > 0)
                text = $"Foto {photoNumber}: {Names(moved)} nicht mehr am Fundort";
            else if (inView.Count > 0)
                text = $"Foto {photoNumber}: {Names(inView)} schon dokumentiert";
            else
                text = $"Foto {photoNumber}: kein Beweisstück im Bild";
            Record(text);
        }

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

            if (IsComplete || m_EvidenceTotal == 0 || m_Deposited.Count < m_EvidenceTotal)
                return;
            IsComplete = true;
            CompletionTime = ElapsedTime;
            Record($"Training abgeschlossen in {FormatTime(CompletionTime)} mit {MistakeCount} Fehler(n)");
        }

        public void ReportMistake(string text)
        {
            MistakeCount++;
            Record("Fehler: " + text, isMistake: true);
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
