using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Tracks the trainee's protective equipment, the forensic work steps and any mistakes, keeps a timed log
    /// and announces every change. The score starts at the maximum and every mistake deducts points.
    /// </summary>
    public class TrainingProgress : MonoBehaviour
    {
        public enum Equipment
        {
            Anzug,
            Handschuhe,
            Mundschutz,
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
        [SerializeField] Color m_GloveColor = new Color(0.25f, 0.55f, 1f);
        [Tooltip("Hand colour once the gloves have touched evidence and need changing.")]
        [SerializeField] Color m_UsedGloveColor = new Color(0.45f, 0.2f, 0.6f);
        [Tooltip("Traces that need a marker and a detail photo.")]
        [SerializeField] int m_TraceTotal;
        [Tooltip("Sealed bags that have to end up in the evidence box.")]
        [SerializeField] int m_EvidenceTotal;
        [Tooltip("Hidden traces that have to be photographed under UV light.")]
        [SerializeField] int m_LatentTotal;
        [SerializeField] int m_MaxScore = 100;
        [Tooltip("Traces that must be in one picture for it to count as an overview photo.")]
        [SerializeField] int m_OverviewTraces = 2;
        [SerializeField] int m_NoOverviewPenalty = 10;
        [SerializeField] int m_GloveChangePenalty = 5;
        [SerializeField] int m_EarlyCollectionPenalty = 5;

        static TrainingProgress s_Instance;

        readonly HashSet<Equipment> m_Worn = new HashSet<Equipment>();
        readonly HashSet<SceneTrace> m_Photographed = new HashSet<SceneTrace>();
        readonly HashSet<LatentTrace> m_LatentDocumented = new HashSet<LatentTrace>();
        readonly List<string> m_Bagged = new List<string>();
        readonly List<string> m_Deposited = new List<string>();
        readonly List<LogEntry> m_Log = new List<LogEntry>();
        UnityEngine.Object m_GloveContact;
        string m_GloveContactName;
        float m_StartTime;
        int m_Penalties;
        bool m_SceneChanged;
        bool m_CollectionStarted;
        string m_LastMissingHint;

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

        public int EquipmentTotal => Enum.GetValues(typeof(Equipment)).Length;
        public int EquipmentWorn => m_Worn.Count;
        public int TraceTotal => m_TraceTotal;
        public int EvidenceTotal => m_EvidenceTotal;
        public int LatentTotal => m_LatentTotal;
        public bool OverviewDone { get; private set; }
        public int MarkedCount => SceneTrace.AllTraces.Count(trace => trace.NeedsOwnDocumentation && trace.Marker != null);
        public int PhotographedCount => m_Photographed.Count;
        public int SampleCount { get; private set; }
        public int BaggedCount => m_Bagged.Count;
        public int DepositedCount => m_Deposited.Count;
        public int LatentDocumentedCount => m_LatentDocumented.Count;
        public int DoorTotal => Door.All.Count;
        public int DoorsDocumented => Door.All.Count(door => door.IsDocumented);
        public bool GlovesUsed => m_GloveContact != null;
        public int MistakeCount { get; private set; }
        public int MaxScore => m_MaxScore;
        public int Score => Mathf.Max(0, m_MaxScore - m_Penalties);
        public int ShotsFired { get; private set; }
        public bool IsComplete { get; private set; }
        public float CompletionTime { get; private set; }
        public float ElapsedTime => Time.time - m_StartTime;
        public IReadOnlyList<LogEntry> Log => m_Log;

        public string Grade => Score >= 90 ? "sehr gut" : Score >= 75 ? "gut" : Score >= 50 ? "bestanden" : "nicht bestanden";

        public bool IsWorn(Equipment item) => m_Worn.Contains(item);

        public static string FormatTime(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

        void Awake()
        {
            s_Instance = this;
            m_StartTime = Time.time;
        }

        /// <summary>Starts the training time over, e.g. once the tutorial is finished.</summary>
        public void RestartClock() => m_StartTime = Time.time;

        /// <summary>Putting on gloves again while wearing some counts as changing them.</summary>
        public void Wear(Equipment item)
        {
            if (item == Equipment.Handschuhe)
            {
                var changed = !m_Worn.Add(item);
                m_GloveContact = null;
                TintHands(m_GloveColor);
                Record(changed ? "Handschuhe gewechselt" : "Handschuhe angezogen");
                return;
            }

            if (m_Worn.Add(item))
                Record(item == Equipment.Anzug ? "Schutzanzug angezogen" : "Mundschutz angelegt");
        }

        /// <summary>
        /// A gloved hand touched evidence. Touching it with gloves that were on another trace or on a door handle
        /// carries material across, so gloves have to be changed in between. Securing also only starts once
        /// every trace is marked and photographed.
        /// </summary>
        public void NoteEvidenceContact(SceneTrace trace)
        {
            if (!m_CollectionStarted)
            {
                m_CollectionStarted = true;
                if (PhotographedCount < m_TraceTotal)
                    ReportMistake("Mit dem Sichern begonnen, bevor alle Spuren markiert und fotografiert waren", m_EarlyCollectionPenalty);
            }

            NoteGloveContact(trace, trace.DisplayName, true);
        }

        /// <summary>A gloved hand touched something that carries traces, e.g. a door handle.</summary>
        public void NoteGloveContact(UnityEngine.Object source, string sourceName, bool isEvidence = false)
        {
            if (!IsWorn(Equipment.Handschuhe) || m_GloveContact == source)
                return;
            if (m_GloveContact == null)
                TintHands(m_UsedGloveColor);
            else if (isEvidence)
                ReportMistake($"Handschuhe nach {m_GloveContactName} nicht gewechselt (Kreuzkontamination)", m_GloveChangePenalty);
            m_GloveContact = source;
            m_GloveContactName = sourceName;
            Changed?.Invoke();
        }

        /// <summary>Something at the scene was moved, marked or sampled. Overview photos have to come first.</summary>
        public void NoteSceneChange()
        {
            if (m_SceneChanged)
                return;
            m_SceneChanged = true;
            if (!OverviewDone)
                ReportMistake("Tatort verändert, bevor eine Übersichtsaufnahme gemacht wurde", m_NoOverviewPenalty);
        }

        /// <summary>
        /// Evaluates a photo. Several untouched traces in one picture make an overview photo; a marked trace still
        /// lying where it was found gets its detail photo; hidden traces count while the UV lamp lights them.
        /// </summary>
        public void RegisterPhoto(int photoNumber, IReadOnlyList<SceneTrace> inView, IReadOnlyList<LatentTrace> latentInView, IReadOnlyList<Door> doorsInView)
        {
            var inPlace = inView.Where(trace => trace.IsInPlace).ToList();
            var isOverview = !OverviewDone && !m_SceneChanged && inPlace.Count >= m_OverviewTraces;
            var documented = inView.Where(trace => trace.TryDocument()).ToList();
            foreach (var trace in documented)
                m_Photographed.Add(trace);
            var documentedLatent = latentInView.Where(trace => trace.TryDocument()).ToList();
            foreach (var trace in documentedLatent)
                m_LatentDocumented.Add(trace);
            // The state a door was found in is recorded by a photo of it; no marker needed.
            var documentedDoors = doorsInView.Where(door => door.TryDocument()).ToList();

            if (isOverview)
            {
                OverviewDone = true;
                Record($"Foto {photoNumber}: Übersichtsaufnahme des unveränderten Tatorts");
            }

            var names = documented.Select(trace => $"Spur {trace.Marker.Number} ({trace.DisplayName})")
                .Concat(documentedLatent.Select(trace => trace.DisplayName))
                .Concat(documentedDoors.Select(door => $"{door.DisplayName} ({door.StateName})")).ToList();
            if (names.Count > 0)
            {
                Record($"Foto {photoNumber}: {string.Join(", ", names)} dokumentiert");
            }
            else if (!isOverview)
            {
                var unmarked = inPlace.Where(trace => trace.NeedsOwnDocumentation && trace.Marker == null).ToList();
                var moved = inView.Where(trace => !trace.IsInPlace).ToList();
                if (unmarked.Count > 0 && !OverviewDone && !m_SceneChanged)
                    ShowMessage($"Foto {photoNumber}: Für die Übersichtsaufnahme müssen mindestens {m_OverviewTraces} Spuren im Bild sein");
                else if (unmarked.Count > 0)
                    ShowMessage($"Foto {photoNumber}: Detailaufnahme zählt erst mit Spurenmarker ({Names(unmarked)})");
                else if (moved.Count > 0)
                    ShowMessage($"Foto {photoNumber}: {Names(moved)} nicht mehr am Fundort");
                else if (inView.Count > 0 || latentInView.Count > 0)
                    ShowMessage($"Foto {photoNumber}: schon dokumentiert");
                else
                    ShowMessage($"Foto {photoNumber}: keine Spur im Bild");
            }

            if (documented.Count > 0 || documentedLatent.Count > 0 || documentedDoors.Count > 0)
                CheckComplete();
        }

        public void RegisterMarker(int number, SceneTrace trace) => Record($"Spurenmarker {number} bei {trace.DisplayName}");

        public void RegisterTraceFound(LatentTrace trace) => Record($"UV-Spur entdeckt: {trace.DisplayName}");

        public void RegisterSample(SceneTrace source)
        {
            SampleCount++;
            Record($"Probe von {source.DisplayName} mit Tupfer genommen");
        }

        public void RegisterBagged(EvidenceItem item, string label, string packaging)
        {
            m_Bagged.Add(item.DisplayName);
            Record($"{item.DisplayName} als {label} in {packaging} versiegelt");
        }

        public void RegisterDeposit(string itemName, string label)
        {
            m_Deposited.Add(itemName);
            Record($"{label} ({itemName}) in der Asservatenkiste");
            CheckComplete();
        }

        // Done once every item is in the evidence box and everything that stays at the scene is documented:
        // hidden traces, traces that are not collected (such as the body) and the doors.
        void CheckComplete()
        {
            if (IsComplete || m_EvidenceTotal == 0 || m_Deposited.Count < m_EvidenceTotal)
                return;
            var missing = MissingDocumentation();
            if (missing != null)
            {
                // Said once per kind of gap, not after every photo.
                if (m_LastMissingHint != missing)
                {
                    m_LastMissingHint = missing;
                    ShowMessage($"Alle Asservate abgegeben. Es fehlt noch: {missing}");
                }

                return;
            }

            IsComplete = true;
            CompletionTime = ElapsedTime;
            Record($"Training abgeschlossen in {FormatTime(CompletionTime)}: {Score}/{m_MaxScore} Punkte ({Grade})");
        }

        string MissingDocumentation()
        {
            if (m_LatentDocumented.Count < m_LatentTotal)
                return "verborgene Spuren (UV-Lampe)";
            var undocumented = SceneTrace.AllTraces.Where(trace => trace is FixedTrace && !trace.IsDocumented).ToList();
            if (undocumented.Count > 0)
                return $"Marker und Foto für {Names(undocumented)}";
            var doors = Door.All.Where(door => !door.IsSettled).Select(door => door.DisplayName).ToList();
            return doors.Count > 0 ? $"Foto vom Zustand: {string.Join(", ", doors)}" : null;
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

        static string Names(IEnumerable<SceneTrace> traces) => string.Join(", ", traces.Select(trace => trace.DisplayName));

        // The controller models stand in for the hands, so gloves colour them.
        void TintHands(Color color)
        {
            var block = new MaterialPropertyBlock();
            foreach (var handRenderer in m_HandRenderers)
            {
                if (handRenderer == null)
                    continue;
                handRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                handRenderer.SetPropertyBlock(block);
            }
        }
    }
}
