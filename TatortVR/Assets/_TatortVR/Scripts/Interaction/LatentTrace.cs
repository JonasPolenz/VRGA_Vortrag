using System.Collections.Generic;
using UnityEngine;

namespace TatortVR
{
    /// <summary>
    /// Trace that is invisible in normal light (fingerprints, shoe prints, body fluids) and only shows up
    /// while a <see cref="UVLamp"/> shines on it. It counts as documented once it is photographed while lit.
    /// The transform position is the centre of the trace.
    /// </summary>
    public class LatentTrace : MonoBehaviour
    {
        [SerializeField] string m_DisplayName = "Spur";

        static readonly List<LatentTrace> s_All = new List<LatentTrace>();

        Renderer[] m_Renderers;
        float m_LitUntil = -1f;

        public static IReadOnlyList<LatentTrace> All => s_All;

        public string DisplayName => m_DisplayName;
        public bool IsVisible { get; private set; }
        public bool IsFound { get; private set; }
        public bool IsDocumented { get; private set; }

        void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            SetVisible(false);
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public void Illuminate(float duration)
        {
            m_LitUntil = Mathf.Max(m_LitUntil, Time.time + duration);
            if (!IsVisible)
                SetVisible(true);
            if (IsFound)
                return;
            IsFound = true;
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.RegisterTraceFound(this);
        }

        public bool TryDocument()
        {
            if (!IsVisible || IsDocumented)
                return false;
            IsDocumented = true;
            return true;
        }

        void Update()
        {
            if (IsVisible && Time.time > m_LitUntil)
                SetVisible(false);
        }

        void SetVisible(bool visible)
        {
            IsVisible = visible;
            foreach (var traceRenderer in m_Renderers)
                traceRenderer.enabled = visible;
        }
    }
}
