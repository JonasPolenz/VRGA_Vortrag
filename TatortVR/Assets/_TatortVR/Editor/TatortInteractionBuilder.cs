using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using static TatortVR.EditorTools.TatortEnvironmentBuilder;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Builds the interactive part of the training on top of the environment: protective equipment and the
    /// evidence camera on a table at the start, evidence bags from a holster at the left hip, a camera holster
    /// at the right hip, example evidence in the living room, the evidence box, the crime scene area,
    /// on-screen notices and the checklist board with a restart button.
    /// Everything it adds can be rebuilt without touching the environment.
    /// </summary>
    public static class TatortInteractionBuilder
    {
        public const string RootName = "Interaktion";

        const string k_BodyName = "Koerper";
        const string k_HandZoneName = "Handzone";
        const int k_DefaultLayers = 1;
        const int k_BagLayerIndex = 1;
        const int k_CameraLayerIndex = 2;
        const float k_TableHeight = 0.8f;
        const string k_OverlayMaterialPath = "Assets/_TatortVR/Materials/UI_ImmerSichtbar.mat";

        static readonly Vector3 k_TablePosition = new Vector3(-1.5f, 0f, -2.1f);
        static readonly Vector3 k_EvidenceBoxPosition = new Vector3(1.5f, 0f, 1f);

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
            foreach (var old in origin.GetComponentsInChildren<Transform>(true).Where(t => t.name == k_BodyName || t.name == k_HandZoneName).ToArray())
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
            var head = origin.Camera.transform;

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var progress = new GameObject("Trainingsablauf").AddComponent<TrainingProgress>();
            progress.transform.SetParent(root.transform, false);
            var messages = BuildMessageDisplay(root.transform, head);

            BuildBody(origin, head, bagLayers, cameraLayers, out var bagHolster, out var cameraHolster);
            var checklist = BuildEquipment(root.transform, progress, cameraHolster, cameraLayers);
            BuildEvidenceBags(root.transform, bagHolster, bagLayers);
            var evidenceCount = BuildEvidence(root.transform);
            BuildEvidenceBox(root.transform);
            BuildCrimeSceneZone(root.transform);
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
            so.FindProperty("m_EvidenceTotal").intValue = evidenceCount;
            SetArray(so.FindProperty("m_HandRenderers"), handRenderers);
            so.ApplyModifiedPropertiesWithoutUndo();
            checklist.Refresh();
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

        // Table next to the start with suit, gloves and camera, plus the checklist board and restart button.
        static TrainingChecklist BuildEquipment(Transform root, TrainingProgress progress, XRSocketInteractor cameraHolster, int cameraLayers)
        {
            var equipment = new GameObject("Ausruestung").transform;
            equipment.SetParent(root, false);
            CreateBox("Tisch", equipment, k_TablePosition + Vector3.up * (k_TableHeight / 2), new Vector3(0.6f, k_TableHeight, 1.2f), LoadMaterial("Moebel_Hell"));
            var top = k_TablePosition + Vector3.up * k_TableHeight;

            var gloves = CreateGrabbable("Handschuhe", equipment, top + new Vector3(0.05f, 0.011f, -0.3f), Quaternion.identity, 0.05f);
            var gloveMaterial = CreateMaterial("Handschuhe", new Color(0.2f, 0.42f, 0.95f));
            CreateBox("Handschuh links", gloves.transform, new Vector3(-0.055f, 0f, 0f), new Vector3(0.09f, 0.02f, 0.2f), gloveMaterial, isStatic: false);
            CreateBox("Handschuh rechts", gloves.transform, new Vector3(0.055f, 0f, 0f), new Vector3(0.09f, 0.02f, 0.2f), gloveMaterial, isStatic: false);
            SetEquipment(gloves.AddComponent<WearableItem>(), TrainingProgress.Equipment.Handschuhe);

            // Lens towards the start so the camera lies ready to pick up.
            BuildCamera(equipment, top + new Vector3(0f, 0.046f, -0.02f), Quaternion.Euler(0f, 90f, 0f), cameraHolster, cameraLayers);

            var suit = CreateGrabbable("Schutzanzug", equipment, top + new Vector3(0f, 0.051f, 0.25f), Quaternion.identity, 0.4f);
            CreateBox("Stoff", suit.transform, Vector3.zero, new Vector3(0.32f, 0.1f, 0.28f), CreateMaterial("Schutzanzug", new Color(0.95f, 0.95f, 0.97f)), isStatic: false);
            CreateBox("Reissverschluss", suit.transform, Vector3.zero, new Vector3(0.012f, 0.102f, 0.28f), LoadMaterial("Zaun"), collider: false, isStatic: false);
            SetEquipment(suit.AddComponent<WearableItem>(), TrainingProgress.Equipment.Anzug);

            var eye = StartPosition + Vector3.up * 1.5f;
            var boardPosition = new Vector3(-2.05f, 1.45f, -2.1f);
            var readFrom = FlatDirection(eye - boardPosition);
            CreateBox("Staender Ablauf", equipment, new Vector3(boardPosition.x, 0.5f, boardPosition.z), new Vector3(0.06f, 1f, 0.06f), LoadMaterial("Zaun"));
            CreateSign("Titel Ablauf", equipment, boardPosition + new Vector3(0f, 0.53f, 0f), readFrom, new Vector2(1.2f, 0.12f), "Spurensicherung: Ablauf", 44);
            var checklistText = CreateSign("Ablauf", equipment, boardPosition, readFrom, new Vector2(1.2f, 0.9f), "", 22, alignment: TextAnchor.UpperLeft);
            var checklist = checklistText.transform.parent.gameObject.AddComponent<TrainingChecklist>();
            var so = new SerializedObject(checklist);
            so.FindProperty("m_Progress").objectReferenceValue = progress;
            so.FindProperty("m_Text").objectReferenceValue = checklistText;
            so.ApplyModifiedPropertiesWithoutUndo();

            var buttonPosition = new Vector3(boardPosition.x, 0.85f, boardPosition.z) + readFrom * 0.06f;
            var button = CreateBox("Neustart", equipment, buttonPosition, new Vector3(0.24f, 0.1f, 0.06f),
                CreateMaterial("Knopf_Neustart", new Color(0.8f, 0.2f, 0.15f)), isStatic: false);
            button.transform.rotation = Quaternion.LookRotation(-readFrom);
            button.AddComponent<XRSimpleInteractable>();
            button.AddComponent<RestartButton>();
            CreateSign("Schild Neustart", equipment, buttonPosition + new Vector3(0f, 0.1f, 0f) + readFrom * 0.035f, readFrom, new Vector2(0.36f, 0.07f), "Neustart (Grip)", 22);
            return checklist;
        }

        // Evidence camera; when let go it is stowed at the right hip.
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

            CreateBox("Gehaeuse", cameraObject.transform, Vector3.zero, new Vector3(0.14f, 0.09f, 0.06f), CreateMaterial("Kamera", new Color(0.12f, 0.12f, 0.14f)), isStatic: false);
            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Objektiv";
            lens.transform.SetParent(cameraObject.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.055f);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lens.transform.localScale = new Vector3(0.05f, 0.025f, 0.05f);
            lens.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Kamera_Objektiv", new Color(0.04f, 0.04f, 0.05f));
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

        // Example evidence in the living room next to the markers. Long items lie along their X axis.
        static int BuildEvidence(Transform root)
        {
            var group = new GameObject("Beweisstuecke").transform;
            group.SetParent(root, false);

            var knife = CreateEvidence(group, "Messer", new Vector3(3.35f, k_FloorTop + 0.011f, 5.1f), Quaternion.Euler(0f, 35f, 0f), 0.15f, 0.6f, new Vector3(0f, 0f, 90f));
            CreateBox("Griff", knife.transform, new Vector3(-0.095f, 0f, 0f), new Vector3(0.11f, 0.022f, 0.028f), CreateMaterial("Messer_Griff", new Color(0.12f, 0.08f, 0.06f)), isStatic: false);
            var bladeMaterial = CreateMaterial("Messer_Klinge", new Color(0.78f, 0.8f, 0.83f));
            bladeMaterial.SetFloat("_Metallic", 0.8f);
            bladeMaterial.SetFloat("_Smoothness", 0.7f);
            CreateBox("Klinge", knife.transform, new Vector3(0.055f, -0.007f, 0f), new Vector3(0.19f, 0.004f, 0.03f), bladeMaterial, isStatic: false);

            var phone = CreateEvidence(group, "Handy", new Vector3(4.75f, k_FloorTop + 0.0045f, 7.2f), Quaternion.Euler(0f, -20f, 0f), 0.2f, 0.9f, new Vector3(0f, 0f, 90f));
            CreateBox("Gehaeuse", phone.transform, Vector3.zero, new Vector3(0.15f, 0.009f, 0.072f), CreateMaterial("Handy", new Color(0.08f, 0.08f, 0.09f)), isStatic: false);
            var screenMaterial = CreateMaterial("Handy_Display", new Color(0.1f, 0.15f, 0.22f));
            screenMaterial.SetFloat("_Smoothness", 0.9f);
            CreateBox("Display", phone.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.135f, 0.001f, 0.064f), screenMaterial, collider: false, isStatic: false);

            // Dining table top is at 0.75 m.
            var glass = CreateEvidence(group, "Trinkglas", new Vector3(3.25f, 0.81f, 3.45f), Quaternion.identity, 0.25f, 0.9f, Vector3.zero);
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Glas";
            cylinder.transform.SetParent(glass.transform, false);
            cylinder.transform.localScale = new Vector3(0.07f, 0.06f, 0.07f);
            cylinder.GetComponent<Renderer>().sharedMaterial = CreateTransparentMaterial("Trinkglas", new Color(0.85f, 0.95f, 1f, 0.3f), 0.9f);

            return group.childCount;
        }

        // Body anchor on the rig: bag holster at the left hip, camera holster at the right hip
        // and a chest zone for putting on the suit.
        static void BuildBody(XROrigin origin, Transform head, int bagLayers, int cameraLayers,
            out XRSocketInteractor bagHolster, out XRSocketInteractor cameraHolster)
        {
            var body = new GameObject(k_BodyName);
            body.transform.SetParent(origin.transform, false);
            var tracker = body.AddComponent<BodyTracker>();

            // Turned sideways so the bag hangs flat against the leg.
            bagHolster = CreateHolster("Huefte links", body.transform, new Vector3(-0.2f, 0.9f, 0.05f), Quaternion.Euler(0f, 90f, 0f), 0.12f, bagLayers);
            cameraHolster = CreateHolster("Huefte rechts", body.transform, new Vector3(0.2f, 0.9f, 0.05f), Quaternion.identity, 0.1f, cameraLayers);

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

            var so = new SerializedObject(tracker);
            so.FindProperty("m_Head").objectReferenceValue = head;
            so.FindProperty("m_Floor").objectReferenceValue = origin.transform;
            so.FindProperty("m_Hip").objectReferenceValue = bagHolster.transform;
            so.FindProperty("m_RightHip").objectReferenceValue = cameraHolster.transform;
            so.FindProperty("m_Chest").objectReferenceValue = chest.transform;
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

        // Inactive template for the transparent evidence bag (24 x 40 cm); the dispenser on the hip holster
        // copies it at the start and after every sealed bag.
        static void BuildEvidenceBags(Transform root, XRSocketInteractor holster, int bagLayers)
        {
            var bag = CreateGrabbable("Beweisbeutel (Vorlage)", root, holster.transform.position, holster.transform.rotation, 0.1f, k_DefaultLayers | bagLayers);
            var grab = bag.GetComponent<XRGrabInteractable>();
            var handle = new GameObject("Griff").transform;
            handle.SetParent(bag.transform, false);
            grab.attachTransform = handle;

            CreateBox("Beutel", bag.transform, new Vector3(0f, -0.21f, 0f), new Vector3(0.24f, 0.4f, 0.035f),
                CreateTransparentMaterial("Beweisbeutel", new Color(0.92f, 0.95f, 1f, 0.35f), 0.6f), isStatic: false);
            var seal = CreateBox("Verschluss", bag.transform, new Vector3(0f, -0.0125f, 0f), new Vector3(0.24f, 0.025f, 0.037f),
                CreateMaterial("Beweisbeutel_Verschluss", new Color(0.85f, 0.1f, 0.1f)), collider: false, isStatic: false);

            // Generous trigger around the bag: an item counts as "in the bag" as soon as it touches this volume.
            var opening = new GameObject("Oeffnung", typeof(BoxCollider));
            opening.transform.SetParent(bag.transform, false);
            var openingCollider = opening.GetComponent<BoxCollider>();
            openingCollider.isTrigger = true;
            openingCollider.center = new Vector3(0f, -0.21f, 0f);
            openingCollider.size = new Vector3(0.32f, 0.5f, 0.16f);

            var contents = new GameObject("Inhalt").transform;
            contents.SetParent(bag.transform, false);
            contents.localPosition = new Vector3(0f, -0.13f, 0f);

            var labelColor = new Color(0.97f, 0.97f, 0.95f, 1f);
            var front = CreateSign("Etikett vorne", bag.transform, bag.transform.TransformPoint(new Vector3(0f, -0.34f, -0.019f)),
                bag.transform.TransformDirection(Vector3.back), new Vector2(0.2f, 0.12f), "BEWEISMITTEL\nleer", 12, labelColor, Color.black);
            var back = CreateSign("Etikett hinten", bag.transform, bag.transform.TransformPoint(new Vector3(0f, -0.34f, 0.019f)),
                bag.transform.TransformDirection(Vector3.forward), new Vector2(0.2f, 0.12f), "BEWEISMITTEL\nleer", 12, labelColor, Color.black);

            var dispenser = holster.gameObject.AddComponent<BagDispenser>();
            var evidenceBag = bag.AddComponent<EvidenceBag>();
            var so = new SerializedObject(evidenceBag);
            so.FindProperty("m_Dispenser").objectReferenceValue = dispenser;
            so.FindProperty("m_Contents").objectReferenceValue = contents;
            so.FindProperty("m_Seal").objectReferenceValue = seal.GetComponent<Renderer>();
            SetArray(so.FindProperty("m_Labels"), new Object[] { front, back });
            so.ApplyModifiedPropertiesWithoutUndo();
            SetHolster(bag.AddComponent<HolsterReturn>(), holster);

            so = new SerializedObject(dispenser);
            so.FindProperty("m_Template").objectReferenceValue = evidenceBag;
            so.ApplyModifiedPropertiesWithoutUndo();
            bag.SetActive(false);
        }

        // Open crate in front of the flat for handing in sealed bags.
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

            // Reaches above the rim so a bag only has to be held over the box.
            var slot = new GameObject("Einwurf", typeof(BoxCollider));
            slot.transform.SetParent(box, false);
            var slotCollider = slot.GetComponent<BoxCollider>();
            slotCollider.isTrigger = true;
            slotCollider.center = new Vector3(0f, 0.3f, 0f);
            slotCollider.size = new Vector3(w - 2 * t, 0.6f, d - 2 * t);
            var so = new SerializedObject(slot.AddComponent<EvidenceBox>());
            so.FindProperty("m_Floor").objectReferenceValue = floor;
            so.ApplyModifiedPropertiesWithoutUndo();

            var signPosition = box.position + new Vector3(0f, h + 0.08f, d / 2);
            var reader = new Vector3(0f, 1.5f, 0.3f);
            CreateSign("Schild Asservatenkiste", box, signPosition, FlatDirection(reader - signPosition), new Vector2(0.6f, 0.12f), "Asservatenkiste", 40);
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

        static GameObject CreateEvidence(Transform parent, string displayName, Vector3 position, Quaternion rotation, float mass, float baggedScale, Vector3 baggedRotation)
        {
            var go = CreateGrabbable(displayName, parent, position, rotation, mass);
            var so = new SerializedObject(go.AddComponent<EvidenceItem>());
            so.FindProperty("m_DisplayName").stringValue = displayName;
            so.FindProperty("m_BaggedScale").floatValue = baggedScale;
            so.FindProperty("m_BaggedRotation").vector3Value = baggedRotation;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Grabbed where the hand touches it (dynamic attach), or pulled to the hand with the ray.
        static GameObject CreateGrabbable(string name, Transform parent, Vector3 position, Quaternion rotation, float mass, int layers = k_DefaultLayers)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.interactionLayers = layers;
            grab.useDynamicAttach = true;
            var body = go.GetComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return go;
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
