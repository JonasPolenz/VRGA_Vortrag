using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Something at the scene that gets a numbered marker and a detail photo: a piece of evidence
    /// (<see cref="EvidenceItem"/>) or a trace area (<see cref="BloodPool"/>).
    /// </summary>
    public abstract class SceneTrace : MonoBehaviour
    {
        [SerializeField] protected string m_DisplayName = "Spur";
        [Tooltip("Carries blood or other body fluids: gloves that touched it have to be changed before the next trace.")]
        [SerializeField] protected bool m_SoilsGloves;

        static readonly List<SceneTrace> s_Traces = new List<SceneTrace>();

        public static IReadOnlyList<SceneTrace> AllTraces => s_Traces;

        public string DisplayName => m_DisplayName;
        public bool SoilsGloves => m_SoilsGloves;
        public EvidenceMarker Marker { get; internal set; }
        public bool IsDocumented { get; protected set; }

        /// <summary>Still where it was found, so marking and photographing it documents the find spot.</summary>
        public abstract bool IsInPlace { get; }
        public abstract Vector3 Center { get; }

        /// <summary>False for samples taken from another trace; they share that trace's marker and photo.</summary>
        public virtual bool NeedsOwnDocumentation => true;

        /// <summary>Distance used to decide which trace a marker set down at <paramref name="point"/> belongs to.</summary>
        public virtual float MarkDistance(Vector3 point) => Vector3.Distance(Center, point);

        protected virtual void OnEnable() => s_Traces.Add(this);
        protected virtual void OnDisable() => s_Traces.Remove(this);

        /// <summary>Detail photo. Only counts with a marker next to the trace, so its number is in the picture.</summary>
        public bool TryDocument()
        {
            if (IsDocumented || !IsInPlace || Marker == null)
                return false;
            IsDocumented = true;
            return true;
        }
    }
}
