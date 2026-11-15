using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using static TatortVR.EditorTools.TatortEnvironmentBuilder;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Builds the interactive part of the training on top of the environment: protective equipment and the
    /// camera on a table at the start, a material table at the entrance (UV lamp, swabs, fresh gloves), the
    /// holsters on the body, the traces in the living room, the evidence box, the crime scene rules,
    /// on-screen notices and the boards. Everything it adds can be rebuilt without touching the environment.
    /// </summary>
    public static class TatortInteractionBuilder
    {
        public const string RootName = "Interaktion";

        const string k_BodyName = "Koerper";
        const string k_HandZoneName = "Handzone";
        const string k_HeadZoneName = "Kopfzone";
        const int k_DefaultLayers = 1;
        const int k_BagLayerIndex = 1;
        const int k_CameraLayerIndex = 2;
        const int k_PistolLayerIndex = 3;
        const int k_MarkerLayerIndex = 4;
        const int k_GloveLayerIndex = 5;
        const float k_TableHeight = 0.8f;
        const string k_OverlayMaterialPath = "Assets/_TatortVR/Materials/UI_ImmerSichtbar.mat";

        static readonly Vector3 k_TablePosition = new Vector3(-1.5f, 0f, -2.1f);
        static readonly Vector3 k_MaterialTablePosition = new Vector3(2.15f, 0f, 1.5f);
        static readonly Vector3 k_EvidenceBoxPosition = new Vector3(1.5f, 0f, 1f);
        static readonly Quaternion k_LieFlat = Quaternion.Euler(90f, 0f, 0f);

        [MenuItem("Tools/TatortVR/Interaktion neu bauen")]
        public static void Rebuild()
        {
            if (Application.isBatchMode)
                EditorSceneManager.OpenScene(XRProjectSetup.k_ScenePath);
            var scene = SceneManager.GetActiveScene();
            var origin = scene.GetRootGameObjects().Select(g => g.GetComponent<XROrigin>()).FirstOrDefault(o => o != null);
            if (origin == null)
            {
                Debug.LogError("[TatortSetup] No XR Origin in the active scene");
                return;
            }

            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName))
                Object.DestroyImmediate(old);
            var rigParts = new[] { k_BodyName, k_HandZoneName, k_HeadZoneName };
            foreach (var old in origin.GetComponentsInChildren<Transform>(true).Where(t => rigParts.Contains(t.name)).ToArray())
            {
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
            }

            Build(scene, origin);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TatortSetup] Interaction rebuilt");
        }

        public static void Build(Scene scene, XROrigin origin)
        {
            var bagLayers = EnsureInteractionLayer(k_BagLayerIndex, "Beweisbeutel");
            var cameraLayers = EnsureInteractionLayer(k_CameraLayerIndex, "Kamera");
            var pistolLayers = EnsureInteractionLayer(k_PistolLayerIndex, "Waffe");
            var markerLayers = EnsureInteractionLayer(k_MarkerLayerIndex, "Marker");
            var gloveLayers = EnsureInteractionLayer(k_GloveLayerIndex, "Handschuhe");
            var head = origin.Camera.transform;
            ConfigureRig(origin);

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var progress = new GameObject("Trainingsablauf").AddComponent<TrainingProgress>();
            progress.transform.SetParent(root.transform, false);
            var messages = BuildMessageDisplay(root.transform, head);

            BuildBody(origin, head, bagLayers, cameraLayers, pistolLayers, markerLayers,
                out var bagHolster, out var cameraHolster, out var pistolHolster, out var markerPouch);
            var gloves = BuildGloveTemplate(root.transform, gloveLayers);
            var checklist = BuildEquipment(root.transform, progress, cameraHolster, cameraLayers, gloves, gloveLayers);
            BuildMaterialTable(root.transform, gloves, gloveLayers);
            BuildEvidenceBags(root.transform, bagHolster, bagLayers);
            BuildMarkers(root.transform, markerPouch, markerLayers);
            BuildPistol(root.transform, pistolHolster, pistolLayers);
            var blood = CreateMaterial("Blut", new Color(0.33f, 0.02f, 0.02f));
            blood.SetFloat("_Smoothness", 0.85f);
            var traceCount = BuildEvidence(root.transform, blood);
            var latentCount = BuildLatentTraces(root.transform);
            BuildEvidenceBox(root.transform);
            BuildCrimeSceneZone(root.transform);
            BuildStepGuard(root.transform, origin, head, blood);
            BuildBriefing(root.transform);
            BuildHandZone(FindChild(origin.transform, "Left Controller"));
            BuildHandZone(FindChild(origin.transform, "Right Controller"));

            var handRenderers = new[] { "Left Controller Visual", "Right Controller Visual" }
                .Select(n => FindChild(origin.transform, n))
                .Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<MeshRenderer>(true))
                .Cast<Object>()
                .ToArray();
            if (handRenderers.Length == 0)
                Debug.LogWarning("[TatortSetup] Controller models not found, gloves will not tint the hands");

            var so = new SerializedObject(progress);
            so.FindProperty("m_Messages").objectReferenceValue = messages;
            // Every trace gets a marker and a photo; every item plus the blood sample ends up in the evidence box.
            so.FindProperty("m_TraceTotal").intValue = traceCount;
            so.FindProperty("m_EvidenceTotal").intValue = traceCount;
            so.FindProperty("m_LatentTotal").intValue = latentCount;
            SetArray(so.FindProperty("m_HandRenderers"), handRenderers);
            so.ApplyModifiedPropertiesWithoutUndo();
            checklist.Refresh();
        }

        // The training is about careful work on foot: no jumping or climbing, and no stepping up onto furniture.
        static void ConfigureRig(XROrigin origin)
        {
            foreach (var name in new[] { "Jump", "Climb", "Climb Teleport" })
            {
                var part = FindChild(origin.transform, name);
                if (part == null)
                    continue;
                part.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
            }

            if (origin.TryGetComponent<CharacterController>(out var controller))
            {
                controller.stepOffset = 0.2f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            }
        }

        // Notice in front of the trainee; drawn on top of walls so it is never hidden.
        static TrainingMessageDisplay BuildMessageDisplay(Transform root, Transform head)
        {
            var text = CreateSign("Meldung", root, StartPosition + new Vector3(0f, 1.3f, 1.1f), Vector3.back, new Vector2(0.8f, 0.22f), "", 32,
                new Color(0.1f, 0.35f, 0.15f, 0.92f));
            var canvas = text.transform.parent.gameObject;
            canvas.GetComponent<Canvas>().sortingOrder = 100;
            var group = canvas.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var background = canvas.transform.Find("Hintergrund").GetComponent<Image>();

            var overlay = GetOverlayMaterial();
            if (overlay != null)
            {
                text.material = overlay;
                background.material = overlay;
            }

            var display = canvas.AddComponent<TrainingMessageDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("m_Text").objectReferenceValue = text;
            so.FindProperty("m_Background").objectReferenceValue = background;
            so.FindProperty("m_Group").objectReferenceValue = group;
            so.FindProperty("m_Head").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();
            return display;
        }

        static Material GetOverlayMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_OverlayMaterialPath);
            if (material != null)
                return material;
            var shader = Shader.Find("UI/NoZTest");
            if (shader == null)
            {
                Debug.LogWarning("[TatortSetup] Shader UI/NoZTest not found, notices can be hidden behind walls");
                return null;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, k_OverlayMaterialPath);
            return material;
        }

        // Table next to the start with the protective equipment and the camera, plus the checklist board.
        static TrainingChecklist BuildEquipment(Transform root, TrainingProgress progress, XRSocketInteractor cameraHolster, int cameraLayers,
            XRGrabInteractable gloves, int gloveLayers)
        {
            var equipment = new GameObject("Ausruestung").transform;
            equipment.SetParent(root, false);
            CreateBox("Tisch", equipment, k_TablePosition + Vector3.up * (k_TableHeight / 2), new Vector3(0.6f, k_TableHeight, 1.2f), LoadMaterial("Moebel_Hell"));
            var top = k_TablePosition + Vector3.up * k_TableHeight;

            BuildGloveBox(equipment, top + new Vector3(0f, 0f, -0.42f), Quaternion.Euler(0f, -90f, 0f), gloves, gloveLayers);

            var mask = CreateGrabbable("Mundschutz", equipment, top + new Vector3(0.03f, 0.004f, -0.14f), Quaternion.identity, 0.02f);
            var maskMaterial = CreateMaterial("Mundschutz", new Color(0.6f, 0.8f, 0.95f));
            CreateMeshObject("Vlies", mask.transform, TatortMeshes.Prism("Mundschutz", TatortMeshes.RoundedRect(0.17f, 0.09f, 0.012f), 0.004f), maskMaterial, Vector3.zero, k_LieFlat);
            for (var i = -1; i <= 1; i++)
                CreateBox("Falte", mask.transform, new Vector3(0f, 0.0023f, i * 0.02f), new Vector3(0.165f, 0.0006f, 0.002f), LoadMaterial("Moebel_Hell"), collider: false, isStatic: false);
            foreach (var side in new[] { -1f, 1f })
                CreateBox("Band", mask.transform, new Vector3(side * 0.105f, 0f, 0f), new Vector3(0.04f, 0.002f, 0.07f), LoadMaterial("Weiss"), collider: false, isStatic: false);
            AddBoxCollider(mask, Vector3.zero, new Vector3(0.18f, 0.012f, 0.1f));
            SetEquipment(mask.AddComponent<WearableItem>(), TrainingProgress.Equipment.Mundschutz);

            // Lens towards the start so the camera lies ready to pick up.
            BuildCamera(equipment, top + new Vector3(0f, 0.046f, 0.08f), Quaternion.Euler(0f, 90f, 0f), cameraHolster, cameraLayers);

            var suit = CreateGrabbable("Schutzanzug", equipment, top + new Vector3(0f, 0.051f, 0.38f), Quaternion.identity, 0.4f);
            CreateBox("Stoff", suit.transform, Vector3.zero, new Vector3(0.32f, 0.1f, 0.28f), CreateMaterial("Schutzanzug", new Color(0.95f, 0.95f, 0.97f)), isStatic: false);
            CreateBox("Reissverschluss", suit.transform, Vector3.zero, new Vector3(0.012f, 0.102f, 0.28f), LoadMaterial("Zaun"), collider: false, isStatic: false);
            CreateSign("Etikett", suit.transform, suit.transform.position + new Vector3(0.162f, 0f, 0f), Vector3.right, new Vector2(0.26f, 0.07f), "Schutzanzug mit Kapuze und Überschuhen", 11);
            SetEquipment(suit.AddComponent<WearableItem>(), TrainingProgress.Equipment.Anzug);

            var eye = StartPosition + Vector3.up * 1.5f;
            var boardPosition = new Vector3(-2.05f, 1.5f, -2.1f);
            var readFrom = FlatDirection(eye - boardPosition);
            CreateBox("Staender Ablauf", equipment, new Vector3(boardPosition.x, 0.5f, boardPosition.z), new Vector3(0.06f, 1f, 0.06f), LoadMaterial("Zaun"));
            CreateSign("Titel Ablauf", equipment, boardPosition + new Vector3(0f, 0.58f, 0f), readFrom, new Vector2(1.4f, 0.12f), "Spurensicherung: Ablauf", 44);
            var checklistText = CreateSign("Ablauf", equipment, boardPosition, readFrom, new Vector2(1.4f, 1f), "", 20, alignment: TextAnchor.UpperLeft);
            var checklist = checklistText.transform.parent.gameObject.AddComponent<TrainingChecklist>();
            var so = new SerializedObject(checklist);
            so.FindProperty("m_Progress").objectReferenceValue = progress;
            so.FindProperty("m_Text").objectReferenceValue = checklistText;
            so.ApplyModifiedPropertiesWithoutUndo();

            var buttonPosition = new Vector3(boardPosition.x, 0.82f, boardPosition.z) + readFrom * 0.06f;
            var button = CreateBox("Neustart", equipment, buttonPosition, new Vector3(0.24f, 0.1f, 0.06f),
                CreateMaterial("Knopf_Neustart", new Color(0.8f, 0.2f, 0.15f)), isStatic: false);
            button.transform.rotation = Quaternion.LookRotation(-readFrom);
            button.AddComponent<XRSimpleInteractable>();
            button.AddComponent<RestartButton>();
            CreateSign("Schild Neustart", equipment, buttonPosition + new Vector3(0f, 0.1f, 0f) + readFrom * 0.035f, readFrom, new Vector2(0.36f, 0.07f), "Neustart (Grip)", 22);
            return checklist;
        }

        // Table at the entrance with everything that is needed again and again while working inside.
        static void BuildMaterialTable(Transform root, XRGrabInteractable gloves, int gloveLayers)
        {
            var table = new GameObject("Materialtisch").transform;
            table.SetParent(root, false);
            CreateBox("Tisch", table, k_MaterialTablePosition + Vector3.up * (k_TableHeight / 2), new Vector3(1f, k_TableHeight, 0.5f), LoadMaterial("Moebel_Hell"));
            var top = k_MaterialTablePosition + Vector3.up * k_TableHeight;
            CreateSign("Schild Material", table, k_MaterialTablePosition + new Vector3(0f, 0.68f, -0.252f), Vector3.back, new Vector2(0.94f, 0.13f),
                "Material: UV-Lampe, Tupfer, frische Handschuhe", 26);

            BuildUVLamp(table, top + new Vector3(-0.3f, 0.026f, 0f), Quaternion.Euler(0f, 90f, 0f));
            BuildSwab(table, "Tupfer 1", top + new Vector3(0f, 0.008f, -0.08f));
            BuildSwab(table, "Tupfer 2", top + new Vector3(0f, 0.008f, 0.08f));
            BuildGloveBox(table, top + new Vector3(0.33f, 0f, 0f), Quaternion.identity, gloves, gloveLayers);
        }

        // Inactive pair of disposable gloves that the glove boxes hand out again and again.
        static XRGrabInteractable BuildGloveTemplate(Transform root, int gloveLayers)
        {
            var pair = CreateGrabbable("Handschuhe (Vorlage)", root, k_TablePosition + Vector3.up, Quaternion.identity, 0.05f, k_DefaultLayers | gloveLayers);
            // Mitten outline with the thumb to the side; the cuff is at y = 0.
            var outline = new[]
            {
                new Vector2(-0.04f, 0f), new Vector2(0.04f, 0f), new Vector2(0.045f, 0.09f), new Vector2(0.04f, 0.17f), new Vector2(0.02f, 0.19f),
                new Vector2(-0.02f, 0.19f), new Vector2(-0.04f, 0.17f), new Vector2(-0.045f, 0.1f), new Vector2(-0.08f, 0.12f),
                new Vector2(-0.09f, 0.1f), new Vector2(-0.05f, 0.05f),
            };
            var mesh = TatortMeshes.Prism("Handschuh", outline, 0.012f, 50f);
            var material = CreateMaterial("Handschuhe", new Color(0.25f, 0.55f, 1f));
            CreateMeshObject("Handschuh links", pair.transform, mesh, material, new Vector3(-0.055f, 0f, -0.095f), k_LieFlat);
            var right = CreateMeshObject("Handschuh rechts", pair.transform, mesh, material, new Vector3(0.055f, 0f, -0.095f), k_LieFlat);
            right.transform.localScale = new Vector3(-1f, 1f, 1f);
            AddBoxCollider(pair, Vector3.zero, new Vector3(0.26f, 0.014f, 0.2f));
            SetEquipment(pair.AddComponent<WearableItem>(), TrainingProgress.Equipment.Handschuhe);
            pair.SetActive(false);
            return pair.GetComponent<XRGrabInteractable>();
        }

        // Box of disposable gloves; the label is on the local -Z side.
        static void BuildGloveBox(Transform parent, Vector3 position, Quaternion rotation, XRGrabInteractable template, int gloveLayers)
        {
            var box = new GameObject("Handschuhbox").transform;
            box.SetParent(parent, false);
            box.SetPositionAndRotation(position, rotation);
            CreateBox("Karton", box, new Vector3(0f, 0.035f, 0f), new Vector3(0.28f, 0.07f, 0.22f), CreateMaterial("Handschuhbox", new Color(0.85f, 0.92f, 0.98f)));
            CreateSign("Etikett", box, box.TransformPoint(new Vector3(0f, 0.035f, -0.111f)), box.TransformDirection(Vector3.back), new Vector2(0.26f, 0.06f),
                "Einmalhandschuhe", 14);

            var socket = CreateHolster("Ausgabe", box, new Vector3(0f, 0.078f, 0f), Quaternion.identity, 0.1f, gloveLayers);
            var so = new SerializedObject(socket.gameObject.AddComponent<ItemDispenser>());
            so.FindProperty("m_Template").objectReferenceValue = template;
            so.FindProperty("m_ItemName").stringValue = "Handschuhe";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Sterile swab for the blood sample; it only becomes evidence once it has been used.
        static void BuildSwab(Transform parent, string name, Vector3 position)
        {
            var swab = CreateGrabbable(name, parent, position, Quaternion.Euler(0f, 90f, 0f), 0.01f);
            CreateCylinder("Stiel", swab.transform, Vector3.zero, new Vector3(0.005f, 0.07f, 0.005f), LoadMaterial("Weiss"), false);
            var tip = CreateCylinder("Watte", swab.transform, new Vector3(0f, 0f, 0.075f), new Vector3(0.012f, 0.012f, 0.012f), CreateMaterial("Tupfer", new Color(0.97f, 0.97f, 0.95f)), false);
            CreateCylinder("Griff", swab.transform, new Vector3(0f, 0f, -0.065f), new Vector3(0.011f, 0.012f, 0.011f), LoadMaterial("Handschuhbox"), false);
            AddBoxCollider(swab, Vector3.zero, new Vector3(0.02f, 0.02f, 0.18f));

            var item = swab.AddComponent<EvidenceItem>();
            SetEvidence(item, "Tupfer", 1f, Vector3.zero, EvidenceItem.PackagingRule.Papier, "Feuchte Tupfer müssen trocknen: Papier oder Karton, nie luftdicht.");
            item.enabled = false;
            var so = new SerializedObject(swab.AddComponent<Swab>());
            so.FindProperty("m_Tip").objectReferenceValue = tip.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Evidence camera; when let go it is stowed on the strap in front of the belly.
        static void BuildCamera(Transform parent, Vector3 position, Quaternion rotation, XRSocketInteractor holster, int cameraLayers)
        {
            var cameraObject = CreateGrabbable("Kamera", parent, position, rotation, 0.4f, k_DefaultLayers | cameraLayers);
            var grab = cameraObject.GetComponent<XRGrabInteractable>();
            // Held in a fixed pose so the lens points forward from the hand.
            grab.useDynamicAttach = false;
            var handle = new GameObject("Griff").transform;
            handle.SetParent(cameraObject.transform, false);
            handle.localPosition = new Vector3(0f, -0.03f, -0.02f);
            grab.attachTransform = handle;

            var bodyMaterial = CreateMaterial("Kamera", new Color(0.12f, 0.12f, 0.14f));
            CreateBox("Gehaeuse", cameraObject.transform, Vector3.zero, new Vector3(0.14f, 0.09f, 0.06f), bodyMaterial, isStatic: false);
            CreateBox("Griffwulst", cameraObject.transform, new Vector3(0.055f, -0.005f, 0.035f), new Vector3(0.03f, 0.08f, 0.02f), bodyMaterial, collider: false, isStatic: false);
            CreateBox("Sucher", cameraObject.transform, new Vector3(0f, 0.055f, -0.005f), new Vector3(0.05f, 0.025f, 0.045f), bodyMaterial, collider: false, isStatic: false);
            CreateCylinder("Objektiv", cameraObject.transform, new Vector3(0f, 0f, 0.055f), new Vector3(0.056f, 0.025f, 0.056f), CreateMaterial("Kamera_Objektiv", new Color(0.04f, 0.04f, 0.05f)), false);
            CreateCylinder("Linsenglas", cameraObject.transform, new Vector3(0f, 0f, 0.081f), new Vector3(0.042f, 0.001f, 0.042f), CreateMaterial("Kamera_Linse", new Color(0.12f, 0.16f, 0.3f)), false);
            CreateBox("Blitzgeraet", cameraObject.transform, new Vector3(0.045f, 0.052f, 0.02f), new Vector3(0.035f, 0.015f, 0.014f), LoadMaterial("Zaun"), collider: false, isStatic: false);
            var glow = CreateBox("Blitz", cameraObject.transform, new Vector3(0.045f, 0.052f, 0.028f), new Vector3(0.03f, 0.011f, 0.002f),
                CreateMaterial("Kamera_Blitz", Color.white, emission: Color.white * 3f), collider: false, isStatic: false);
            glow.SetActive(false);

            var lensPoint = new GameObject("Linse").transform;
            lensPoint.SetParent(cameraObject.transform, false);
            lensPoint.localPosition = new Vector3(0f, 0f, 0.085f);

            var flash = new GameObject("Blitzlicht").AddComponent<Light>();
            flash.transform.SetParent(cameraObject.transform, false);
            flash.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
            flash.type = LightType.Point;
            flash.range = 4f;
            flash.intensity = 4f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            var display = CreateSign("Display", cameraObject.transform, cameraObject.transform.TransformPoint(new Vector3(0f, 0.005f, -0.031f)),
                cameraObject.transform.TransformDirection(Vector3.back), new Vector2(0.11f, 0.06f), "Fotos: 0", 14);

            var so = new SerializedObject(cameraObject.AddComponent<PhotoCamera>());
            so.FindProperty("m_Lens").objectReferenceValue = lensPoint;
            so.FindProperty("m_Flash").objectReferenceValue = flash;
            so.FindProperty("m_FlashGlow").objectReferenceValue = glow;
            so.FindProperty("m_Display").objectReferenceValue = display;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetHolster(cameraObject.AddComponent<HolsterReturn>(), holster);
        }

        // Forensic UV lamp, beam along +Z; the trigger switches it on and off.
        static void BuildUVLamp(Transform parent, Vector3 position, Quaternion rotation)
        {
            var lamp = CreateGrabbable("UV-Lampe", parent, position, rotation, 0.3f);
            var grab = lamp.GetComponent<XRGrabInteractable>();
            grab.useDynamicAttach = false;
            var handle = new GameObject("Griff").transform;
            handle.SetParent(lamp.transform, false);
            handle.localPosition = new Vector3(0f, 0f, -0.03f);
            grab.attachTransform = handle;

            var housing = CreateMaterial("UV_Lampe", new Color(0.12f, 0.12f, 0.2f));
            var violet = new Color(0.55f, 0.3f, 1f);
            CreateCylinder("Gehaeuse", lamp.transform, Vector3.zero, new Vector3(0.035f, 0.08f, 0.035f), housing, true);
            CreateCylinder("Kopf", lamp.transform, new Vector3(0f, 0f, 0.095f), new Vector3(0.05f, 0.015f, 0.05f), housing, true);
            CreateCylinder("Linse", lamp.transform, new Vector3(0f, 0f, 0.111f), new Vector3(0.042f, 0.002f, 0.042f), CreateMaterial("UV_Linse", new Color(0.15f, 0.08f, 0.3f)), false);
            var glow = CreateCylinder("Leuchten", lamp.transform, new Vector3(0f, 0f, 0.113f), new Vector3(0.042f, 0.002f, 0.042f),
                CreateMaterial("UV_Leuchten", violet, emission: violet * 4f), false);
            glow.SetActive(false);

            var lens = new GameObject("Lichtaustritt").transform;
            lens.SetParent(lamp.transform, false);
            lens.localPosition = new Vector3(0f, 0f, 0.116f);
            var light = new GameObject("UV-Licht").AddComponent<Light>();
            light.transform.SetParent(lens, false);
            light.type = LightType.Spot;
            light.spotAngle = 40f;
            light.range = 3f;
            light.intensity = 3f;
            light.color = violet;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var so = new SerializedObject(lamp.AddComponent<UVLamp>());
            so.FindProperty("m_Lens").objectReferenceValue = lens;
            so.FindProperty("m_Light").objectReferenceValue = light;
            so.FindProperty("m_Glow").objectReferenceValue = glow;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Inactive template for the numbered evidence markers: a yellow tent with a scale, as used for detail
        // photos. The dispenser in the belt pouch copies it.
        static void BuildMarkers(Transform root, XRSocketInteractor pouch, int markerLayers)
        {
            var marker = CreateGrabbable("Spurenmarker (Vorlage)", root, pouch.transform.position, pouch.transform.rotation, 0.05f, k_DefaultLayers | markerLayers);
            // Markers are set down, not thrown; they stand upright where they are let go.
            marker.GetComponent<XRGrabInteractable>().throwOnDetach = false;
            var tent = new[] { new Vector2(-0.04f, 0f), new Vector2(0.04f, 0f), new Vector2(0f, 0.09f) };
            CreateMeshObject("Aufsteller", marker.transform, TatortMeshes.Prism("Spurenmarker", tent, 0.075f), LoadMaterial("Spurenmarker"), Vector3.zero, Quaternion.identity);
            AddBoxCollider(marker, new Vector3(0f, 0.045f, 0f), new Vector3(0.08f, 0.09f, 0.075f));

            // Scale next to the marker, 10 cm with a tick every 2 cm.
            CreateBox("Massstab", marker.transform, new Vector3(0.075f, 0.0015f, 0f), new Vector3(0.014f, 0.003f, 0.1f), LoadMaterial("Weiss"), collider: false, isStatic: false);
            for (var i = 0; i <= 5; i++)
                CreateBox("Teilstrich", marker.transform, new Vector3(0.075f, 0.0017f, -0.05f + i * 0.02f), new Vector3(0.014f, 0.003f, i % 5 == 0 ? 0.003f : 0.0015f), LoadMaterial("Dunkel"), collider: false, isStatic: false);

            var yellow = new Color(0.98f, 0.78f, 0.1f);
            var labels = new Object[2];
            for (var i = 0; i < 2; i++)
            {
                // The two slanted faces of the tent.
                var side = i == 0 ? 1f : -1f;
                var normal = new Vector3(side * 0.914f, 0.406f, 0f);
                labels[i] = CreateSign(i == 0 ? "Nummer rechts" : "Nummer links", marker.transform, marker.transform.TransformPoint(new Vector3(side * 0.02f, 0.045f, 0f) + normal * 0.002f),
                    marker.transform.TransformDirection(normal), new Vector2(0.065f, 0.08f), "0", 30, yellow, Color.black);
            }

            var evidenceMarker = marker.AddComponent<EvidenceMarker>();
            var so = new SerializedObject(evidenceMarker);
            SetArray(so.FindProperty("m_Labels"), labels);
            so.ApplyModifiedPropertiesWithoutUndo();

            so = new SerializedObject(pouch.gameObject.AddComponent<MarkerDispenser>());
            so.FindProperty("m_Template").objectReferenceValue = evidenceMarker;
            so.ApplyModifiedPropertiesWithoutUndo();
            marker.SetActive(false);
        }

        // Service pistol, barrel along +Z. Starts in the right hip holster and returns there when let go, which reloads it.
        static void BuildPistol(Transform root, XRSocketInteractor holster, int pistolLayers)
        {
            var pistol = CreateGrabbable("Pistole", root, holster.transform.position, holster.transform.rotation, 0.8f, k_DefaultLayers | pistolLayers);
            var grab = pistol.GetComponent<XRGrabInteractable>();
            // Held by the grip in a fixed pose so the barrel points forward from the hand.
            grab.useDynamicAttach = false;
            var handle = new GameObject("Griff").transform;
            handle.SetParent(pistol.transform, false);
            handle.localPosition = new Vector3(0f, -0.07f, -0.06f);
            grab.attachTransform = handle;

            var metal = CreateMaterial("Pistole", new Color(0.13f, 0.13f, 0.14f));
            metal.SetFloat("_Metallic", 0.6f);
            metal.SetFloat("_Smoothness", 0.45f);
            var polymer = CreateMaterial("Pistole_Griff", new Color(0.07f, 0.07f, 0.07f));
            // Side profiles (along the barrel, up) extruded to the pistol's width.
            var sideways = Quaternion.Euler(0f, -90f, 0f);
            var slide = new[]
            {
                new Vector2(-0.095f, -0.016f), new Vector2(0.095f, -0.016f), new Vector2(0.095f, 0.01f),
                new Vector2(0.089f, 0.016f), new Vector2(-0.089f, 0.016f), new Vector2(-0.095f, 0.01f),
            };
            var frame = new[]
            {
                new Vector2(0.085f, -0.034f), new Vector2(0.085f, -0.016f), new Vector2(-0.092f, -0.016f), new Vector2(-0.1f, -0.05f),
                new Vector2(-0.118f, -0.135f), new Vector2(-0.07f, -0.14f), new Vector2(-0.052f, -0.05f), new Vector2(-0.045f, -0.034f),
            };
            CreateMeshObject("Schlitten", pistol.transform, TatortMeshes.Prism("Pistole_Schlitten", slide, 0.028f), metal, Vector3.zero, sideways);
            CreateMeshObject("Griffstueck", pistol.transform, TatortMeshes.Prism("Pistole_Griffstueck", frame, 0.026f), polymer, Vector3.zero, sideways);
            CreateBox("Abzugsbuegel unten", pistol.transform, new Vector3(0f, -0.062f, -0.012f), new Vector3(0.008f, 0.005f, 0.07f), polymer, collider: false, isStatic: false);
            CreateBox("Abzugsbuegel vorn", pistol.transform, new Vector3(0f, -0.048f, 0.022f), new Vector3(0.008f, 0.03f, 0.005f), polymer, collider: false, isStatic: false);
            CreateBox("Abzug", pistol.transform, new Vector3(0f, -0.046f, -0.01f), new Vector3(0.006f, 0.02f, 0.006f), metal, collider: false, isStatic: false);
            CreateBox("Kimme", pistol.transform, new Vector3(0f, 0.019f, -0.08f), new Vector3(0.014f, 0.006f, 0.008f), metal, collider: false, isStatic: false);
            CreateBox("Korn", pistol.transform, new Vector3(0f, 0.019f, 0.085f), new Vector3(0.004f, 0.006f, 0.008f), metal, collider: false, isStatic: false);
            AddBoxCollider(pistol, Vector3.zero, new Vector3(0.03f, 0.034f, 0.19f));
            AddBoxCollider(pistol, new Vector3(0f, -0.085f, -0.085f), new Vector3(0.03f, 0.11f, 0.05f));

            var muzzle = new GameObject("Muendung").transform;
            muzzle.SetParent(pistol.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.004f, 0.1f);
            var flash = CreateBox("Muendungsfeuer", pistol.transform, new Vector3(0f, 0.004f, 0.115f), new Vector3(0.025f, 0.025f, 0.03f),
                CreateMaterial("Muendungsfeuer", new Color(1f, 0.8f, 0.3f), emission: new Color(1f, 0.7f, 0.2f) * 4f), collider: false, isStatic: false);
            flash.SetActive(false);
            var light = new GameObject("Muendungslicht").AddComponent<Light>();
            light.transform.SetParent(pistol.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.004f, 0.13f);
            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.shadows = LightShadows.None;
            light.enabled = false;

            // Round counter behind the rear sight, readable while aiming.
            var ammo = CreateSign("Munition", pistol.transform, pistol.transform.TransformPoint(new Vector3(0f, 0.034f, -0.088f)),
                pistol.transform.TransformDirection(Vector3.back), new Vector2(0.04f, 0.026f), "15", 11, new Color(0.05f, 0.05f, 0.05f, 0.9f), new Color(1f, 0.85f, 0.2f));

            var so = new SerializedObject(pistol.AddComponent<Pistol>());
            so.FindProperty("m_Muzzle").objectReferenceValue = muzzle;
            so.FindProperty("m_MuzzleLight").objectReferenceValue = light;
            so.FindProperty("m_MuzzleFlash").objectReferenceValue = flash;
            so.FindProperty("m_AmmoDisplay").objectReferenceValue = ammo;
            so.FindProperty("m_ImpactMaterial").objectReferenceValue = CreateMaterial("Einschuss", new Color(0.04f, 0.04f, 0.04f));
            so.ApplyModifiedPropertiesWithoutUndo();
            SetHolster(pistol.AddComponent<HolsterReturn>(), holster);
            holster.startingSelectedInteractable = grab;
        }

        static void BuildStepGuard(Transform root, XROrigin origin, Transform head, Material blood)
        {
            var guard = new GameObject("Trittschutz").AddComponent<EvidenceStepGuard>();
            guard.transform.SetParent(root, false);
            var so = new SerializedObject(guard);
            so.FindProperty("m_Head").objectReferenceValue = head;
            so.FindProperty("m_Floor").objectReferenceValue = origin.transform;
            so.FindProperty("m_FootprintMaterial").objectReferenceValue = blood;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The traces in the living room: blood on the floor and three objects. Long items lie along their X axis.
        // Returns how many traces need a marker and a detail photo.
        static int BuildEvidence(Transform root, Material blood)
        {
            var group = new GameObject("Spuren").transform;
            group.SetParent(root, false);
            var paperHint = "Biologische Spuren müssen trocken und atmungsaktiv in Papier oder Karton.";

            // Blood pool next to the knocked-over chair, with drops leading towards the door.
            var pool = new GameObject("Blutlache");
            pool.transform.SetParent(group, false);
            pool.transform.SetPositionAndRotation(new Vector3(2.7f, k_FloorTop + 0.0015f, 5f), Quaternion.Euler(0f, 25f, 0f));
            var radii = new Vector2(0.38f, 0.27f);
            CreateMeshObject("Lache", pool.transform, TatortMeshes.Blob("Blutlache", radii.x, radii.y, 11), blood, Vector3.zero, Quaternion.identity)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var drop = TatortMeshes.Blob("Blutstropfen", 0.02f, 0.016f, 5, 12, 0.15f);
            var drops = new[] { new Vector3(-0.55f, 0f, 0.1f), new Vector3(-0.75f, 0f, 0.22f), new Vector3(-0.98f, 0f, 0.3f), new Vector3(0.5f, 0f, -0.12f), new Vector3(0.1f, 0f, 0.4f), new Vector3(-0.3f, 0f, -0.38f) };
            for (var i = 0; i < drops.Length; i++)
            {
                var dropObject = CreateMeshObject("Tropfen", pool.transform, drop, blood, drops[i], Quaternion.Euler(0f, i * 57f, 0f));
                dropObject.transform.localScale = Vector3.one * (0.7f + i % 3 * 0.5f);
                dropObject.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Trigger for the swab; it does not block walking.
            var sampleArea = pool.AddComponent<BoxCollider>();
            sampleArea.isTrigger = true;
            sampleArea.center = new Vector3(0f, 0.03f, 0f);
            sampleArea.size = new Vector3(radii.x * 2f, 0.08f, radii.y * 2f);
            var poolTrace = new SerializedObject(pool.AddComponent<BloodPool>());
            poolTrace.FindProperty("m_DisplayName").stringValue = "Blutlache";
            poolTrace.FindProperty("m_Radii").vector2Value = radii;
            poolTrace.ApplyModifiedPropertiesWithoutUndo();

            var knife = CreateEvidence(group, "Messer", new Vector3(3.35f, k_FloorTop + 0.011f, 5.1f), Quaternion.Euler(0f, 35f, 0f), 0.15f, 0.6f, new Vector3(0f, 0f, 90f),
                EvidenceItem.PackagingRule.Papier, "Blutanhaftungen: " + paperHint);
            var blade = new[]
            {
                new Vector2(0f, -0.016f), new Vector2(0.11f, -0.016f), new Vector2(0.16f, -0.01f), new Vector2(0.19f, 0.004f),
                new Vector2(0.165f, 0.013f), new Vector2(0.13f, 0.016f), new Vector2(0f, 0.016f),
            };
            var grip = new[]
            {
                new Vector2(-0.11f, -0.012f), new Vector2(-0.005f, -0.014f), new Vector2(0f, -0.008f), new Vector2(0f, 0.008f),
                new Vector2(-0.005f, 0.014f), new Vector2(-0.11f, 0.012f), new Vector2(-0.116f, 0f),
            };
            var bladeMaterial = CreateMaterial("Messer_Klinge", new Color(0.78f, 0.8f, 0.83f));
            bladeMaterial.SetFloat("_Metallic", 0.8f);
            bladeMaterial.SetFloat("_Smoothness", 0.7f);
            CreateMeshObject("Klinge", knife.transform, TatortMeshes.Prism("Messer_Klinge", blade, 0.003f), bladeMaterial, new Vector3(-0.04f, -0.006f, 0f), k_LieFlat);
            CreateMeshObject("Griff", knife.transform, TatortMeshes.Prism("Messer_Griff", grip, 0.02f, 60f), CreateMaterial("Messer_Griff", new Color(0.12f, 0.08f, 0.06f)), new Vector3(-0.04f, 0f, 0f), k_LieFlat);
            CreateMeshObject("Blutanhaftung", knife.transform, TatortMeshes.Blob("Blutanhaftung", 0.04f, 0.011f, 3, 14), blood, new Vector3(0.06f, -0.0042f, 0f), Quaternion.identity);
            AddBoxCollider(knife, new Vector3(-0.005f, 0f, 0f), new Vector3(0.31f, 0.022f, 0.034f));

            var phone = CreateEvidence(group, "Handy", new Vector3(4.75f, k_FloorTop + 0.0045f, 7.2f), Quaternion.Euler(0f, -20f, 0f), 0.2f, 0.9f, new Vector3(0f, 0f, 90f),
                EvidenceItem.PackagingRule.Abschirmung, "Handys gehören in den Abschirmbeutel, sonst können sie aus der Ferne gelöscht werden.");
            CreateMeshObject("Gehaeuse", phone.transform, TatortMeshes.Prism("Handy", TatortMeshes.RoundedRect(0.15f, 0.072f, 0.009f), 0.008f), CreateMaterial("Handy", new Color(0.08f, 0.08f, 0.09f)), Vector3.zero, k_LieFlat);
            var screenMaterial = CreateMaterial("Handy_Display", new Color(0.1f, 0.15f, 0.22f));
            screenMaterial.SetFloat("_Smoothness", 0.9f);
            CreateMeshObject("Display", phone.transform, TatortMeshes.Prism("Handy_Display", TatortMeshes.RoundedRect(0.138f, 0.062f, 0.006f), 0.0008f), screenMaterial, new Vector3(0f, 0.0044f, 0f), k_LieFlat);
            AddBoxCollider(phone, Vector3.zero, new Vector3(0.15f, 0.01f, 0.072f));

            // The glass stands on the dining table; it is 12 cm tall and its root sits at half height.
            var glassSpot = new Vector3(3.25f, 2f, 3.45f);
            glassSpot.y = TatortFurnitureKit.SurfaceBelow(glassSpot) + 0.06f;
            var glass = CreateEvidence(group, "Trinkglas", glassSpot, Quaternion.identity, 0.25f, 0.9f, Vector3.zero,
                EvidenceItem.PackagingRule.Papier, "Speichel am Glasrand: " + paperHint);
            var profile = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.03f, 0f), new Vector2(0.036f, 0.12f), new Vector2(0.036f, 0.12f),
                new Vector2(0.033f, 0.12f), new Vector2(0.033f, 0.12f), new Vector2(0.028f, 0.012f), new Vector2(0.028f, 0.012f), new Vector2(0f, 0.012f),
            };
            CreateMeshObject("Glas", glass.transform, TatortMeshes.Lathe("Trinkglas", profile, 20), CreateTransparentMaterial("Trinkglas", new Color(0.85f, 0.95f, 1f, 0.3f), 0.9f), new Vector3(0f, -0.06f, 0f), Quaternion.identity);
            AddBoxCollider(glass, Vector3.zero, new Vector3(0.07f, 0.12f, 0.07f));

            return group.childCount;
        }

        // Traces that only show under the UV lamp: fingerprints on the door frame, shoe prints across the
        // living room floor and a body fluid trace on the sofa. Each root sits at the centre of its trace.
        static int BuildLatentTraces(Transform root)
        {
            var group = new GameObject("Verborgene Spuren").transform;
            group.SetParent(root, false);
            var glowColor = new Color(0.35f, 1f, 0.65f);
            var glow = CreateMaterial("UV_Spur", glowColor, emission: glowColor * 2.5f);

            // Living room side of the hallway wall (x = 1.075), just north of the door, at handle height.
            var prints = CreateTrace(group, "Fingerabdrücke", new Vector3(1.078f, 1.05f, 6.12f));
            var facingRoom = Quaternion.LookRotation(Vector3.left);
            var fingers = new[] { new Vector2(-0.03f, 0.008f), new Vector2(-0.01f, 0.02f), new Vector2(0.012f, 0.018f), new Vector2(0.032f, 0.004f) };
            foreach (var finger in fingers)
                AddTraceQuad(prints, facingRoom * new Vector3(finger.x, finger.y, 0f), facingRoom, new Vector2(0.016f, 0.022f), glow);

            // Trail from the window towards the hallway, alternating left and right foot.
            var shoes = CreateTrace(group, "Schuhabdrücke", new Vector3(3.5f, k_FloorTop + 0.0015f, 7.6f));
            var walking = Quaternion.LookRotation(Vector3.down, Vector3.left);
            for (var i = 0; i < 4; i++)
                AddTraceQuad(shoes, new Vector3(0.75f - i * 0.5f, 0f, i % 2 == 0 ? 0.09f : -0.09f), walking, new Vector2(0.1f, 0.27f), glow);

            // On the seat of the sofa.
            var seatSpot = new Vector3(5.3f, 2f, 6.35f);
            seatSpot.y = TatortFurnitureKit.SurfaceBelow(seatSpot) + 0.0015f;
            var fluid = CreateTrace(group, "Biologische Spur", seatSpot);
            var facingUp = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            AddTraceQuad(fluid, Vector3.zero, facingUp, new Vector2(0.07f, 0.05f), glow);
            AddTraceQuad(fluid, new Vector3(0.04f, 0f, 0.03f), facingUp * Quaternion.Euler(0f, 0f, 35f), new Vector2(0.05f, 0.035f), glow);
            AddTraceQuad(fluid, new Vector3(-0.035f, 0f, -0.025f), facingUp * Quaternion.Euler(0f, 0f, -20f), new Vector2(0.03f, 0.025f), glow);

            return group.childCount;
        }

        static Transform CreateTrace(Transform parent, string displayName, Vector3 position)
        {
            var trace = new GameObject(displayName).transform;
            trace.SetParent(parent, false);
            trace.position = position;
            var so = new SerializedObject(trace.gameObject.AddComponent<LatentTrace>());
            so.FindProperty("m_DisplayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            return trace;
        }

        // Quads face their local -Z, so the rotation's forward points into the surface.
        static void AddTraceQuad(Transform trace, Vector3 localPosition, Quaternion worldRotation, Vector2 size, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Abdruck";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(trace, false);
            quad.transform.localPosition = localPosition;
            quad.transform.rotation = worldRotation;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Cylinder lying along the local Z axis; 'scale' is (diameter, half length, diameter).
        static GameObject CreateCylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool collider)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        // Case briefing to the right of the start.
        static void BuildBriefing(Transform root)
        {
            var briefing = new GameObject("Einsatzauftrag").transform;
            briefing.SetParent(root, false);
            var position = new Vector3(1.75f, 1.5f, -2.5f);
            var readFrom = FlatDirection(StartPosition + Vector3.up * 1.5f - position);
            CreateBox("Staender Einsatz", briefing, new Vector3(position.x, 0.45f, position.z), new Vector3(0.06f, 0.9f, 0.06f), LoadMaterial("Zaun"));
            CreateSign("Titel Einsatz", briefing, position + Vector3.up * 0.66f, readFrom, new Vector2(1.4f, 0.12f), "Einsatzauftrag", 44);
            CreateSign("Einsatz", briefing, position, readFrom, new Vector2(1.4f, 1.16f),
                "Lage: Notruf um 22:47 Uhr. In der Erdgeschosswohnung Musterweg 7 wurde ein Bewohner mit Stichverletzung aufgefunden " +
                "und ins Krankenhaus gebracht. Der Tatverdächtige ist flüchtig, der Tatort ist abgesperrt und gesichert.\n\n" +
                "Auftrag: Spuren im Wohnzimmer dokumentieren und sichern.\n\n" +
                "Vorgehen: Auge - Kamera - Hand\n" +
                "1. Schutzanzug, Mundschutz und Handschuhe anlegen (Tisch links)\n" +
                "2. Übersichtsfoto machen, bevor irgendetwas verändert wird\n" +
                "3. Spurenmarker neben jede Spur stellen, dann Detailfoto\n" +
                "4. Erst wenn alles fotografiert ist: Blutprobe mit Tupfer nehmen,\n    Gegenstände einzeln verpacken\n" +
                "5. Blut und Speichel in Papier, Handy in den Abschirmbeutel\n" +
                "6. Versiegelte Beutel in die Asservatenkiste (rechts neben der Haustür)\n" +
                "7. Mit der UV-Lampe verborgene Spuren suchen und fotografieren\n\n" +
                "Handschuhe wechseln: zwischen zwei Spuren und nach jeder Türklinke\n(Handschuhbox am Materialtisch neben der Haustür).\n" +
                "Nicht in Blut oder auf Spuren treten. Die Dienstwaffe bleibt im Holster.", 18, alignment: TextAnchor.UpperLeft);
        }

        // Body anchor on the rig: packaging at the left hip, pistol at the right hip, markers at the front of the
        // belt, camera on a strap in front of the belly, zones for putting on suit and mask, and the head collision.
        static void BuildBody(XROrigin origin, Transform head, int bagLayers, int cameraLayers, int pistolLayers, int markerLayers,
            out XRSocketInteractor bagHolster, out XRSocketInteractor cameraHolster, out XRSocketInteractor pistolHolster, out XRSocketInteractor markerPouch)
        {
            var body = new GameObject(k_BodyName);
            body.transform.SetParent(origin.transform, false);
            var tracker = body.AddComponent<BodyTracker>();

            // Turned sideways so the bag hangs flat against the leg.
            bagHolster = CreateHolster("Huefte links", body.transform, new Vector3(-0.2f, 0.9f, 0.05f), Quaternion.Euler(0f, 90f, 0f), 0.12f, bagLayers);
            // Barrel pointing down, grip up, like a belt holster.
            pistolHolster = CreateHolster("Huefte rechts", body.transform, new Vector3(0.2f, 0.9f, 0.05f), Quaternion.Euler(90f, 0f, 0f), 0.1f, pistolLayers);
            cameraHolster = CreateHolster("Brustgurt", body.transform, new Vector3(0f, 1.05f, 0.17f), Quaternion.identity, 0.1f, cameraLayers);
            markerPouch = CreateHolster("Markertasche", body.transform, new Vector3(-0.14f, 0.9f, 0.16f), Quaternion.identity, 0.07f, markerLayers);

            var chest = new GameObject("Brust", typeof(CapsuleCollider), typeof(Rigidbody), typeof(WearZone));
            chest.transform.SetParent(body.transform, false);
            chest.transform.localPosition = new Vector3(0f, 1.3f, 0.05f);
            var chestCollider = chest.GetComponent<CapsuleCollider>();
            chestCollider.isTrigger = true;
            chestCollider.radius = 0.18f;
            chestCollider.height = 0.55f;
            chestCollider.direction = 1;
            MakeKinematic(chest.GetComponent<Rigidbody>());
            SetZoneType(chest.GetComponent<WearZone>(), WearZone.ZoneType.Body, null);

            // The face mask is put on by holding it to the face.
            var face = new GameObject(k_HeadZoneName, typeof(SphereCollider), typeof(Rigidbody), typeof(WearZone));
            face.transform.SetParent(head, false);
            var faceCollider = face.GetComponent<SphereCollider>();
            faceCollider.isTrigger = true;
            faceCollider.radius = 0.14f;
            MakeKinematic(face.GetComponent<Rigidbody>());
            SetZoneType(face.GetComponent<WearZone>(), WearZone.ZoneType.Head, null);

            var so = new SerializedObject(tracker);
            so.FindProperty("m_Head").objectReferenceValue = head;
            so.FindProperty("m_Floor").objectReferenceValue = origin.transform;
            so.FindProperty("m_Hip").objectReferenceValue = bagHolster.transform;
            so.FindProperty("m_RightHip").objectReferenceValue = pistolHolster.transform;
            so.FindProperty("m_Front").objectReferenceValue = cameraHolster.transform;
            so.FindProperty("m_Pouch").objectReferenceValue = markerPouch.transform;
            so.FindProperty("m_Chest").objectReferenceValue = chest.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            so = new SerializedObject(body.AddComponent<HeadCollision>());
            so.FindProperty("m_Head").objectReferenceValue = head;
            so.FindProperty("m_Origin").objectReferenceValue = origin.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static XRSocketInteractor CreateHolster(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, float radius, int layers)
        {
            var holster = new GameObject(name, typeof(SphereCollider), typeof(Rigidbody));
            holster.transform.SetParent(parent, false);
            holster.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            var sphere = holster.GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = radius;
            MakeKinematic(holster.GetComponent<Rigidbody>());
            var socket = holster.AddComponent<XRSocketInteractor>();
            socket.interactionLayers = layers;
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0.5f;
            return socket;
        }

        // Inactive template for the evidence packaging (24 x 40 cm); the dispenser on the hip holster copies it
        // at the start and after every sealed bag. The trigger cycles through plastic, paper and shielding bag.
        static void BuildEvidenceBags(Transform root, XRSocketInteractor holster, int bagLayers)
        {
            var bag = CreateGrabbable("Beweisbeutel (Vorlage)", root, holster.transform.position, holster.transform.rotation, 0.1f, k_DefaultLayers | bagLayers);
            var grab = bag.GetComponent<XRGrabInteractable>();
            var handle = new GameObject("Griff").transform;
            handle.SetParent(bag.transform, false);
            grab.attachTransform = handle;

            var center = new Vector3(0f, -0.21f, 0f);
            var size = new Vector3(0.24f, 0.4f, 0.035f);
            var shielding = CreateMaterial("Beutel_Abschirmung", new Color(0.72f, 0.74f, 0.78f));
            shielding.SetFloat("_Metallic", 0.9f);
            shielding.SetFloat("_Smoothness", 0.65f);
            // Order of EvidenceBag.Packaging. The visuals have no colliders of their own: switching them would
            // otherwise take away the collider the bag is grabbed by.
            var visuals = new Object[]
            {
                CreateBox("Kunststoffbeutel", bag.transform, center, size, CreateTransparentMaterial("Beweisbeutel", new Color(0.92f, 0.95f, 1f, 0.35f), 0.6f), collider: false, isStatic: false),
                CreateBox("Papiertuete", bag.transform, center, size, CreateMaterial("Beutel_Papier", new Color(0.72f, 0.58f, 0.4f)), collider: false, isStatic: false),
                CreateBox("Abschirmbeutel", bag.transform, center, size, shielding, collider: false, isStatic: false),
            };
            AddBoxCollider(bag, center, size);
            var seal = CreateBox("Verschluss", bag.transform, new Vector3(0f, -0.0125f, 0f), new Vector3(0.24f, 0.025f, 0.037f),
                CreateMaterial("Beweisbeutel_Verschluss", new Color(0.85f, 0.1f, 0.1f)), collider: false, isStatic: false);

            // Generous trigger around the bag: an item counts as "in the bag" as soon as it touches this volume.
            var opening = new GameObject("Oeffnung", typeof(BoxCollider));
            opening.transform.SetParent(bag.transform, false);
            var openingCollider = opening.GetComponent<BoxCollider>();
            openingCollider.isTrigger = true;
            openingCollider.center = center;
            openingCollider.size = new Vector3(0.32f, 0.5f, 0.16f);

            var contents = new GameObject("Inhalt").transform;
            contents.SetParent(bag.transform, false);
            contents.localPosition = new Vector3(0f, -0.13f, 0f);

            var labelColor = new Color(0.97f, 0.97f, 0.95f, 1f);
            var front = CreateSign("Etikett vorne", bag.transform, bag.transform.TransformPoint(new Vector3(0f, -0.31f, -0.019f)),
                bag.transform.TransformDirection(Vector3.back), new Vector2(0.22f, 0.15f), "", 10, labelColor, Color.black);
            var back = CreateSign("Etikett hinten", bag.transform, bag.transform.TransformPoint(new Vector3(0f, -0.31f, 0.019f)),
                bag.transform.TransformDirection(Vector3.forward), new Vector2(0.22f, 0.15f), "", 10, labelColor, Color.black);

            var dispenser = holster.gameObject.AddComponent<BagDispenser>();
            var evidenceBag = bag.AddComponent<EvidenceBag>();
            var so = new SerializedObject(evidenceBag);
            so.FindProperty("m_Dispenser").objectReferenceValue = dispenser;
            so.FindProperty("m_Contents").objectReferenceValue = contents;
            so.FindProperty("m_Seal").objectReferenceValue = seal.GetComponent<Renderer>();
            SetArray(so.FindProperty("m_Labels"), new Object[] { front, back });
            SetArray(so.FindProperty("m_Visuals"), visuals);
            so.ApplyModifiedPropertiesWithoutUndo();
            SetHolster(bag.AddComponent<HolsterReturn>(), holster);

            so = new SerializedObject(dispenser);
            so.FindProperty("m_Template").objectReferenceValue = evidenceBag;
            so.ApplyModifiedPropertiesWithoutUndo();
            bag.SetActive(false);
        }

        // Open crate in front of the flat for handing in sealed bags, with the evidence list on the wall.
        static void BuildEvidenceBox(Transform root)
        {
            var box = new GameObject("Asservatenkiste").transform;
            box.SetParent(root, false);
            box.localPosition = k_EvidenceBoxPosition;
            var material = CreateMaterial("Asservatenkiste", new Color(0.25f, 0.33f, 0.45f));
            const float w = 0.6f, d = 0.46f, h = 0.35f, t = 0.02f;
            CreateBox("Boden", box, new Vector3(0f, t / 2, 0f), new Vector3(w, t, d), material);
            CreateBox("Wand vorne", box, new Vector3(0f, h / 2, -d / 2 + t / 2), new Vector3(w, h, t), material);
            CreateBox("Wand hinten", box, new Vector3(0f, h / 2, d / 2 - t / 2), new Vector3(w, h, t), material);
            CreateBox("Wand links", box, new Vector3(-w / 2 + t / 2, h / 2, 0f), new Vector3(t, h, d - 2 * t), material);
            CreateBox("Wand rechts", box, new Vector3(w / 2 - t / 2, h / 2, 0f), new Vector3(t, h, d - 2 * t), material);

            var floor = new GameObject("Ablage").transform;
            floor.SetParent(box, false);
            floor.localPosition = new Vector3(0f, t, 0f);

            var signPosition = box.position + new Vector3(0f, h + 0.08f, d / 2);
            var reader = new Vector3(0f, 1.5f, 0.3f);
            CreateSign("Schild Asservatenkiste", box, signPosition, FlatDirection(reader - signPosition), new Vector2(0.6f, 0.12f), "Asservatenkiste", 40);
            // On the house wall between the house sign and the window.
            var list = CreateSign("Asservatenliste", box, new Vector3(2.38f, 1.45f, 1.914f), Vector3.back, new Vector2(0.6f, 0.5f), "Asservatenliste", 20, alignment: TextAnchor.UpperLeft);

            // Reaches above the rim so a bag only has to be held over the box.
            var slot = new GameObject("Einwurf", typeof(BoxCollider));
            slot.transform.SetParent(box, false);
            var slotCollider = slot.GetComponent<BoxCollider>();
            slotCollider.isTrigger = true;
            slotCollider.center = new Vector3(0f, 0.3f, 0f);
            slotCollider.size = new Vector3(w - 2 * t, 0.6f, d - 2 * t);
            var so = new SerializedObject(slot.AddComponent<EvidenceBox>());
            so.FindProperty("m_Floor").objectReferenceValue = floor;
            so.FindProperty("m_List").objectReferenceValue = list;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The whole flat counts as the crime scene.
        static void BuildCrimeSceneZone(Transform root)
        {
            var zone = new GameObject("Tatortbereich");
            zone.transform.SetParent(root, false);
            zone.transform.localPosition = new Vector3(0f, 1.3f, 7f);
            var so = new SerializedObject(zone.AddComponent<CrimeSceneZone>());
            so.FindProperty("m_AreaName").stringValue = "Tatort";
            so.FindProperty("m_Size").vector3Value = new Vector3(12f, 2.6f, 10f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildHandZone(Transform controller)
        {
            if (controller == null)
            {
                Debug.LogWarning("[TatortSetup] Controller not found, no hand zone for gloves");
                return;
            }

            var zone = new GameObject(k_HandZoneName, typeof(SphereCollider), typeof(Rigidbody), typeof(WearZone));
            zone.transform.SetParent(controller, false);
            var sphere = zone.GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.09f;
            MakeKinematic(zone.GetComponent<Rigidbody>());
            SetZoneType(zone.GetComponent<WearZone>(), WearZone.ZoneType.Hand, controller);
        }

        static GameObject CreateEvidence(Transform parent, string displayName, Vector3 position, Quaternion rotation, float mass, float baggedScale, Vector3 baggedRotation,
            EvidenceItem.PackagingRule packaging, string packagingHint)
        {
            var go = CreateGrabbable(displayName, parent, position, rotation, mass);
            SetEvidence(go.AddComponent<EvidenceItem>(), displayName, baggedScale, baggedRotation, packaging, packagingHint);
            return go;
        }

        static void SetEvidence(EvidenceItem item, string displayName, float baggedScale, Vector3 baggedRotation, EvidenceItem.PackagingRule packaging, string packagingHint)
        {
            var so = new SerializedObject(item);
            so.FindProperty("m_DisplayName").stringValue = displayName;
            so.FindProperty("m_BaggedScale").floatValue = baggedScale;
            so.FindProperty("m_BaggedRotation").vector3Value = baggedRotation;
            so.FindProperty("m_Packaging").enumValueIndex = (int)packaging;
            so.FindProperty("m_PackagingHint").stringValue = packagingHint;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Grabbed where the hand touches it (dynamic attach). Grabbed with the ray from a distance it comes into
        // the hand instead of staying out at the end of the ray, so it can be held to the body or into a bag.
        static GameObject CreateGrabbable(string name, Transform parent, Vector3 position, Quaternion rotation, float mass, int layers = k_DefaultLayers)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.interactionLayers = layers;
            grab.useDynamicAttach = true;
            grab.farAttachMode = InteractableFarAttachMode.Near;
            var body = go.GetComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return go;
        }

        static void AddBoxCollider(GameObject target, Vector3 center, Vector3 size)
        {
            var box = target.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        static void MakeKinematic(Rigidbody body)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        static void SetHolster(HolsterReturn holsterReturn, XRSocketInteractor holster)
        {
            var so = new SerializedObject(holsterReturn);
            so.FindProperty("m_Holster").objectReferenceValue = holster;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetEquipment(WearableItem item, TrainingProgress.Equipment equipment)
        {
            var so = new SerializedObject(item);
            so.FindProperty("m_Equipment").enumValueIndex = (int)equipment;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetZoneType(WearZone zone, WearZone.ZoneType type, Transform handRoot)
        {
            var so = new SerializedObject(zone);
            so.FindProperty("m_Type").enumValueIndex = (int)type;
            so.FindProperty("m_HandRoot").objectReferenceValue = handRoot;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static Transform FindChild(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }
}
