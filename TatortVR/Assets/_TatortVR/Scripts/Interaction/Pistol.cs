using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TatortVR
{
    /// <summary>
    /// Service pistol carried in the right hip holster. Each trigger pull fires one round along the barrel:
    /// muzzle flash, shot sound, recoil vibration and a bullet hole where it hits. Putting it back into the
    /// holster reloads the magazine. Hitting evidence costs points.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Pistol : MonoBehaviour
    {
        [SerializeField] Transform m_Muzzle;
        [SerializeField] Light m_MuzzleLight;
        [SerializeField] GameObject m_MuzzleFlash;
        [SerializeField] Text m_AmmoDisplay;
        [SerializeField] Material m_ImpactMaterial;
        [SerializeField] int m_MagazineSize = 15;
        [SerializeField] float m_Range = 50f;
        [SerializeField] float m_ImpactImpulse = 1.5f;
        [SerializeField] float m_FlashDuration = 0.05f;
        [SerializeField] int m_MaxImpacts = 40;
        [SerializeField] int m_EvidenceHitPenalty = 25;

        const float k_ImpactSize = 0.025f;

        XRGrabInteractable m_Grab;
        AudioSource m_Audio;
        AudioClip m_ShotClip;
        AudioClip m_EmptyClip;
        Transform m_ImpactRoot;
        readonly Queue<GameObject> m_Impacts = new Queue<GameObject>();
        float m_FlashOffAt = -1f;

        public int Rounds { get; private set; }
        public int MagazineSize => m_MagazineSize;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            m_Grab.selectEntered.AddListener(OnSelected);

            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
            m_ShotClip = CreateShotClip();
            m_EmptyClip = CreateClickClip();

            Rounds = m_MagazineSize;
            SetFlash(false);
            UpdateDisplay();
        }

        void OnDestroy()
        {
            m_Grab.activated.RemoveListener(OnActivated);
            m_Grab.selectEntered.RemoveListener(OnSelected);
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (Fire() && args.interactorObject is XRBaseInputInteractor hand)
                hand.SendHapticImpulse(0.9f, 0.08f);
        }

        // Holstering reloads.
        void OnSelected(SelectEnterEventArgs args)
        {
            if (!(args.interactorObject is XRSocketInteractor) || Rounds == m_MagazineSize)
                return;
            Rounds = m_MagazineSize;
            UpdateDisplay();
            var progress = TrainingProgress.Instance;
            if (progress != null)
                progress.ShowMessage("Pistole nachgeladen");
        }

        /// <summary>Fires one round. Returns false when the magazine is empty.</summary>
        public bool Fire()
        {
            var progress = TrainingProgress.Instance;
            if (Rounds <= 0)
            {
                m_Audio.PlayOneShot(m_EmptyClip);
                if (progress != null)
                    progress.ShowMessage("Magazin leer: zum Nachladen ins Holster stecken", warning: true);
                return false;
            }

            Rounds--;
            UpdateDisplay();
            m_Audio.PlayOneShot(m_ShotClip);
            SetFlash(true);
            m_FlashOffAt = Time.time + m_FlashDuration;
            if (progress != null)
                progress.RegisterShot();

            if (!Physics.Raycast(m_Muzzle.position, m_Muzzle.forward, out var hit, m_Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;

            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(m_Muzzle.forward * m_ImpactImpulse, hit.point, ForceMode.Impulse);
            AddImpact(hit);

            var evidence = hit.collider.GetComponentInParent<EvidenceItem>();
            if (evidence != null && progress != null)
                progress.ReportMistake($"Auf {evidence.DisplayName} geschossen", m_EvidenceHitPenalty);
            return true;
        }

        void Update()
        {
            if (m_FlashOffAt < 0f || Time.time < m_FlashOffAt)
                return;
            m_FlashOffAt = -1f;
            SetFlash(false);
        }

        // Bullet hole; the oldest one is reused once the limit is reached.
        void AddImpact(RaycastHit hit)
        {
            if (m_ImpactRoot == null)
                m_ImpactRoot = new GameObject("Einschuesse").transform;

            GameObject impact;
            if (m_Impacts.Count >= m_MaxImpacts)
            {
                impact = m_Impacts.Dequeue();
            }
            else
            {
                impact = GameObject.CreatePrimitive(PrimitiveType.Quad);
                impact.name = "Einschuss";
                Destroy(impact.GetComponent<Collider>());
                var impactRenderer = impact.GetComponent<Renderer>();
                impactRenderer.sharedMaterial = m_ImpactMaterial;
                impactRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            m_Impacts.Enqueue(impact);
            // Moving objects carry their holes along; their root is unscaled, unlike the stretched wall boxes.
            impact.transform.SetParent(hit.rigidbody != null ? hit.rigidbody.transform : m_ImpactRoot, false);
            impact.transform.SetPositionAndRotation(hit.point + hit.normal * 0.002f, Quaternion.LookRotation(-hit.normal));
            impact.transform.localScale = Vector3.one * k_ImpactSize;
        }

        void SetFlash(bool on)
        {
            if (m_MuzzleLight != null)
                m_MuzzleLight.enabled = on;
            if (m_MuzzleFlash != null)
                m_MuzzleFlash.SetActive(on);
        }

        void UpdateDisplay()
        {
            if (m_AmmoDisplay != null)
                m_AmmoDisplay.text = Rounds.ToString();
        }

        // There are no audio assets in the project, so the shot is synthesised: a noise crack plus a low thump.
        static AudioClip CreateShotClip()
        {
            const int rate = 22050;
            var samples = new float[(int)(0.4f * rate)];
            var random = new System.Random(7);
            var noise = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                noise = Mathf.Lerp(noise, (float)(random.NextDouble() * 2.0 - 1.0), 0.6f);
                var crack = noise * Mathf.Exp(-t * 18f);
                var thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 9f);
                samples[i] = Mathf.Clamp(0.8f * crack + 0.5f * thump, -1f, 1f);
            }

            var clip = AudioClip.Create("Schuss", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip CreateClickClip()
        {
            const int rate = 22050;
            var samples = new float[(int)(0.04f * rate)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Sin(2f * Mathf.PI * 1800f * i / rate) * Mathf.Exp(-i / (float)rate * 150f) * 0.4f;
            var clip = AudioClip.Create("Klick", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
