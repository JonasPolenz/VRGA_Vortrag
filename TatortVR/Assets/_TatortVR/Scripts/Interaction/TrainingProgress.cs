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
        [Tooltip("Hand colour once the gloves have blood on them and need changing; the hands pulse in it.")]
        [SerializeField] Color m_SoiledGloveColor = new Color(0.9f, 0.1f, 0.08f);
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
        readonly HashSet<SceneTrace> m_TouchedWithSoiledGloves = new HashSet<SceneTrace>();
        MaterialPropertyBlock m_HandTint;
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
        /// <summary>Traces that have to be in one picture for an overview photo.</summary>
        public int OverviewTraces => m_OverviewTraces;

        /// <summary>The gloves have blood on them and must not touch the next trace.</summary>
        public bool GlovesNeedChange => m_GloveContact != null;
        public int MistakeCount { get; private set; }

        /// <summary>Points deducted so far, and what the latest deduction was for.</summary>
        public int Penalties => m_Penalties;
        public string LastMistake { get; private set; }
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
                m_TouchedWithSoiledGloves.Clear();
                TintHands(m_GloveColor);
                Record(changed ? "Handschuhe gewechselt" : "Handschuhe angezogen");
                return;
            }

            if (m_Worn.Add(item))
                Record(item == Equipment.Anzug ? "Schutzanzug angezogen" : "Mundschutz angelegt");
        }

        /// <summary>
        /// A gloved hand touched evidence. Gloves only have to be changed once they have blood on them: touching
        /// another trace with them carries the blood across. Securing also only starts once every trace is
        /// marked and photographed.
        /// </summary>
        public void NoteEvidenceContact(SceneTrace trace)
        {
            if (!m_CollectionStarted)
            {
                m_CollectionStarted = true;
                if (PhotographedCount < m_TraceTotal)
                    ReportMistake("Mit dem Sichern begonnen, bevor alle Spuren markiert und fotografiert waren", m_EarlyCollectionPenalty);
            }

            if (!IsWorn(Equipment.Handschuhe))
                return;
            // Counted once per trace, not every time it is picked up again with the same gloves.
            if (GlovesNeedChange && m_GloveContact != trace && m_TouchedWithSoiledGloves.Add(trace))
                ReportMistake($"{trace.DisplayName} mit blutigen Handschuhen angefasst (nach {m_GloveContactName} nicht gewechselt)", m_GloveChangePenalty);
            if (trace.SoilsGloves)
                SoilGloves(trace, trace.DisplayName);
        }

        /// <summary>A gloved hand touched blood. From now on the gloves have to be changed before the next trace.</summary>
        public void SoilGloves(UnityEngine.Object source, string sourceName)
        {
            if (!IsWorn(Equipment.Handschuhe) || m_GloveContact == source)
                return;
            var wereClean = !GlovesNeedChange;
            m_GloveContact = source;
            m_GloveContactName = sourceName;
            if (wereClean)
                ShowMessage($"Blut an den Handschuhen ({sourceName}): vor der nächsten Spur wechseln", warning: true);
            Changed?.Invoke();
        }

        // Soiled gloves pulse, so the need to change them is hard to miss even without looking at the overlay.
        void Update()
        {
            if (!GlovesNeedChange)
                return;
            var dark = m_SoiledGloveColor * 0.45f;
            dark.a = m_SoiledGloveColor.a;
            TintHands(Color.Lerp(dark, m_SoiledGloveColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 5f)));
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
        /// Evaluates a photo. Several traces in one picture, still where they were found, make an overview photo;
        /// a marked trace still lying where it was found gets its detail photo; hidden traces count while the UV
        /// lamp lights them. An overview taken after the scene was changed still ticks the step off, the
        /// deduction for changing the scene first stays.
        /// </summary>
        public void RegisterPhoto(int photoNumber, IReadOnlyList<SceneTrace> inView, IReadOnlyList<LatentTrace> latentInView, IReadOnlyList<Door> doorsInView)
        {
            var inPlace = inView.Where(trace => trace.IsInPlace).ToList();
            var isOverview = !OverviewDone && inPlace.Count >= m_OverviewTraces;
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
                Record(m_SceneChanged
                    ? $"Foto {photoNumber}: Übersichtsaufnahme nachgeholt (der Tatort war schon verändert)"
                    : $"Foto {photoNumber}: Übersichtsaufnahme des unveränderten Tatorts");
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
                    ShowMessage($"Foto {photoNumber}: nur {inPlace.Count} Spur im Bild. {OverviewHint}");
                else if (unmarked.Count > 0)
                    ShowMessage($"Foto {photoNumber}: Detailaufnahme zählt erst mit Spurenmarker ({Names(unmarked)})");
                else if (moved.Count > 0)
                    ShowMessage($"Foto {photoNumber}: {Names(moved)} nicht mehr am Fundort");
                else if (inView.Count > 0 || latentInView.Count > 0)
                    ShowMessage($"Foto {photoNumber}: schon dokumentiert");
                else if (!OverviewDone && !m_SceneChanged && HasEnteredScene)
                    ShowMessage($"Foto {photoNumber}: keine Spur im Bild. {OverviewHint}");
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
            LastMistake = $"-{penalty} {text}";
            Record($"Fehler (-{penalty}): {text}", isMistake: true);
        }

        /// <summary>How an overview photo is taken, for the hints.</summary>
        public string OverviewHint =>
            $"Übersichtsfoto: von der Tür aus mindestens {m_OverviewTraces} Spuren zusammen in den Sucher nehmen, Kamera leicht nach unten neigen.";

        /// <summary>Set by the crime scene zone; before that, photos are practice shots and get no overview hint.</summary>
        public bool HasEnteredScene { get; private set; }

        public void NoteSceneEntered(string areaName, bool fullyEquipped)
        {
            HasEnteredScene = true;
            if (fullyEquipped)
                ShowMessage($"{areaName} betreten. Zuerst das {OverviewHint}");
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
            if (m_HandTint == null)
                m_HandTint = new MaterialPropertyBlock();
            foreach (var handRenderer in m_HandRenderers)
            {
                if (handRenderer == null)
                    continue;
                handRenderer.GetPropertyBlock(m_HandTint);
                m_HandTint.SetColor("_BaseColor", color);
                m_HandTint.SetColor("_Color", color);
                handRenderer.SetPropertyBlock(m_HandTint);
            }
        }
    }
}
