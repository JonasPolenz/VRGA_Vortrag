using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Builds the grey-box training environment: a flat with five rooms, room signs, coloured floors,
    /// a fenced yard with landmarks, a police cordon, teleport areas and anchors, and the controller status board.
    /// Everything lives under the "Umgebung" root so it can be rebuilt without touching the XR rig.
    /// </summary>
    public static class TatortEnvironmentBuilder
    {
        public const string RootName = "Umgebung";

        const string k_Root = "Assets/_TatortVR";
        const string k_AnchorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/DemoAssets/Prefabs/Teleport/Teleport Anchor.prefab";
        const int k_TeleportLayerIndex = 31;
        const float k_WallHeight = 2.6f;
        const float k_WallThickness = 0.15f;
        const float k_DoorHeight = 2.1f;
        internal const float k_FloorTop = 0.02f;
        const float k_GroundHalfSize = 15f;
        const float k_CanvasScale = 0.002f; // 500 canvas pixels per metre

        public static readonly Vector3 StartPosition = new Vector3(0f, 0f, -2f);

        struct Opening
        {
            public float Start, End, Bottom, Top;
        }

        static Opening Door(float center, float width = 1f) =>
            new Opening { Start = center - width / 2, End = center + width / 2, Bottom = 0f, Top = k_DoorHeight };

        static Opening Window(float center, float width = 1.6f, float bottom = 0.9f) =>
            new Opening { Start = center - width / 2, End = center + width / 2, Bottom = bottom, Top = k_DoorHeight };

        // Materials are created once in Build() and shared by the helpers below.
        static Material s_InnerWall, s_OuterWall, s_Glass, s_Furniture, s_LightFurniture, s_Fabric, s_Fence, s_Path, s_Tape, s_Stain;
        static int s_TeleportLayers;

        [MenuItem("Tools/TatortVR/Umgebung neu bauen")]
        public static void Rebuild()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName))
                Object.DestroyImmediate(old);

            Build(scene);

            var rig = scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("XR Origin"));
            if (rig != null)
                rig.transform.SetPositionAndRotation(StartPosition, Quaternion.identity);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TatortSetup] Environment rebuilt");
        }

        public static void Build(Scene scene)
        {
            Directory.CreateDirectory(k_Root + "/Materials");
            Directory.CreateDirectory(k_Root + "/Textures");
            AssetDatabase.Refresh();
            CreateMaterials();
            s_TeleportLayers = EnsureTeleportLayer();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            BuildYard(root.transform);
            BuildFlat(new GameObject("Wohnung").transform, root.transform);
            BuildTeleportAnchors(root.transform);
            BuildInfoBoards(root.transform);
        }

        static void CreateMaterials()
        {
            var grid = CreateGridTexture();
            CreateMaterial("Boden", Color.white, grid, new Vector2(2 * k_GroundHalfSize, 2 * k_GroundHalfSize));
            s_InnerWall = CreateMaterial("Wand", new Color(0.88f, 0.86f, 0.82f));
            s_OuterWall = CreateMaterial("Wand_Aussen", new Color(0.66f, 0.6f, 0.54f));
            s_Glass = CreateGlassMaterial();
            s_Furniture = CreateMaterial("Moebel", new Color(0.45f, 0.31f, 0.2f));
            s_LightFurniture = CreateMaterial("Moebel_Hell", new Color(0.82f, 0.82f, 0.8f));
            s_Fabric = CreateMaterial("Stoff", new Color(0.27f, 0.32f, 0.42f));
            // Used by the evidence markers the trainee places.
            CreateMaterial("Spurenmarker", new Color(0.98f, 0.78f, 0.1f));
            s_Fence = CreateMaterial("Zaun", new Color(0.3f, 0.3f, 0.32f));
            s_Path = CreateMaterial("Weg", new Color(0.42f, 0.42f, 0.42f));
            s_Tape = CreateMaterial("Absperrband", new Color(0.85f, 0.1f, 0.1f));
            s_Stain = CreateMaterial("Fleck", new Color(0.32f, 0.05f, 0.05f));
        }

        static void BuildYard(Transform root)
        {
            var yard = new GameObject("Gelaende").transform;
            yard.SetParent(root, false);

            // 30 x 30 m ground with a 1 m grid so movement is easy to judge in the headset.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Boden";
            ground.transform.SetParent(yard, false);
            ground.transform.localScale = new Vector3(2 * k_GroundHalfSize / 10f, 1f, 2 * k_GroundHalfSize / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = LoadMaterial("Boden");
            MakeTeleportArea(ground);
            MarkStatic(ground);

            CreateFloor("Weg", yard, new Rect(-0.75f, -4f, 1.5f, 6f), s_Path);

            // Fence along the edge of the ground so nobody walks off the world.
            var fence = new GameObject("Zaun").transform;
            fence.SetParent(yard, false);
            const float e = k_GroundHalfSize;
            BuildWall("Zaun Sued", fence, new Vector3(-e, 0, -e), new Vector3(e, 0, -e), s_Fence, 1.2f, 0.1f);
            BuildWall("Zaun Nord", fence, new Vector3(-e, 0, e), new Vector3(e, 0, e), s_Fence, 1.2f, 0.1f);
            BuildWall("Zaun West", fence, new Vector3(-e, 0, -e), new Vector3(-e, 0, e), s_Fence, 1.2f, 0.1f);
            BuildWall("Zaun Ost", fence, new Vector3(e, 0, -e), new Vector3(e, 0, e), s_Fence, 1.2f, 0.1f);

            // Compass signs on the fence, readable from inside the yard.
            CreateSign("Schild Nord", yard, new Vector3(0, 1.6f, e - 0.1f), Vector3.back, new Vector2(1.6f, 0.5f), "NORD", 140);
            CreateSign("Schild Sued", yard, new Vector3(0, 1.6f, -e + 0.1f), Vector3.forward, new Vector2(1.6f, 0.5f), "SÜD", 140);
            CreateSign("Schild West", yard, new Vector3(-e + 0.1f, 1.6f, 0), Vector3.right, new Vector2(1.6f, 0.5f), "WEST", 140);
            CreateSign("Schild Ost", yard, new Vector3(e - 0.1f, 1.6f, 0), Vector3.left, new Vector2(1.6f, 0.5f), "OST", 140);

            // Coloured corner pillars as landmarks.
            var landmarks = new GameObject("Landmarken").transform;
            landmarks.SetParent(yard, false);
            CreateBox("Saeule NW (blau)", landmarks, new Vector3(-e + 1, 1.5f, e - 1), new Vector3(0.5f, 3f, 0.5f), CreateMaterial("Saeule_Blau", new Color(0.15f, 0.35f, 0.85f)));
            CreateBox("Saeule NO (rot)", landmarks, new Vector3(e - 1, 1.5f, e - 1), new Vector3(0.5f, 3f, 0.5f), CreateMaterial("Saeule_Rot", new Color(0.85f, 0.2f, 0.15f)));
            CreateBox("Saeule SW (gruen)", landmarks, new Vector3(-e + 1, 1.5f, -e + 1), new Vector3(0.5f, 3f, 0.5f), CreateMaterial("Saeule_Gruen", new Color(0.2f, 0.65f, 0.25f)));
            CreateBox("Saeule SO (gelb)", landmarks, new Vector3(e - 1, 1.5f, -e + 1), new Vector3(0.5f, 3f, 0.5f), CreateMaterial("Saeule_Gelb", new Color(0.95f, 0.8f, 0.15f)));

            // Police car parked next to the path.
            var car = new GameObject("Einsatzfahrzeug").transform;
            car.SetParent(landmarks, false);
            car.localPosition = new Vector3(-4f, 0f, -3.5f);
            CreateBox("Karosserie", car, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 0.7f, 4.4f), CreateMaterial("Fahrzeug", new Color(0.92f, 0.93f, 0.95f)));
            CreateBox("Kabine", car, new Vector3(0, 1.2f, -0.2f), new Vector3(1.6f, 0.6f, 2.2f), s_Fabric);
            CreateBox("Blaulicht", car, new Vector3(0, 1.56f, 0.1f), new Vector3(0.9f, 0.12f, 0.25f), CreateMaterial("Blaulicht", new Color(0.1f, 0.3f, 1f), emission: new Color(0.1f, 0.3f, 1f)));
            for (var i = 0; i < 4; i++)
                CreateBox("Rad " + (i + 1), car, new Vector3(i < 2 ? -0.85f : 0.85f, 0.32f, i % 2 == 0 ? 1.4f : -1.4f), new Vector3(0.25f, 0.64f, 0.64f), s_Fence);

            BuildCordon(yard);
        }

        // Red police tape around the flat with a gap at the path. Posts collide, the tape itself does not.
        static void BuildCordon(Transform yard)
        {
            var cordon = new GameObject("Absperrung").transform;
            cordon.SetParent(yard, false);
            var posts = new[]
            {
                new Vector2(-1f, 0f), new Vector2(-4f, 0f), new Vector2(-7f, 0f), new Vector2(-7f, 4.5f), new Vector2(-7f, 9f), new Vector2(-7f, 13f),
                new Vector2(-3.5f, 13f), new Vector2(0f, 13f), new Vector2(3.5f, 13f), new Vector2(7f, 13f), new Vector2(7f, 9f), new Vector2(7f, 4.5f),
                new Vector2(7f, 0f), new Vector2(4f, 0f), new Vector2(1f, 0f),
            };
            for (var i = 0; i < posts.Length; i++)
            {
                var p = new Vector3(posts[i].x, 0f, posts[i].y);
                CreateBox($"Pfosten {i + 1}", cordon, p + new Vector3(0, 0.55f, 0), new Vector3(0.06f, 1.1f, 0.06f), s_LightFurniture);
                if (i == 0)
                    continue;
                var prev = new Vector3(posts[i - 1].x, 0f, posts[i - 1].y);
                var tape = CreateBox($"Band {i}", cordon, (prev + p) / 2 + new Vector3(0, 1f, 0), new Vector3(0.01f, 0.06f, Vector3.Distance(prev, p)), s_Tape, collider: false);
                tape.transform.localRotation = Quaternion.LookRotation(p - prev);
            }
        }

        // Flat of 12 x 10 m south of the yard centre: hallway, kitchen, bathroom, living room (crime scene), bedroom.
        static void BuildFlat(Transform flat, Transform root)
        {
            flat.SetParent(root, false);

            var floors = new GameObject("Boeden").transform;
            floors.SetParent(flat, false);
            CreateFloor("Boden Flur", floors, new Rect(-1f, 2f, 2f, 7f), CreateGridMaterial("Boden_Flur", new Color(0.78f, 0.78f, 0.78f), new Vector2(2f, 7f)));
            CreateFloor("Boden Kueche", floors, new Rect(-6f, 2f, 5f, 4f), CreateGridMaterial("Boden_Kueche", new Color(0.72f, 0.84f, 0.92f), new Vector2(5f, 4f)));
            CreateFloor("Boden Bad", floors, new Rect(-6f, 6f, 5f, 3f), CreateGridMaterial("Boden_Bad", new Color(0.55f, 0.8f, 0.82f), new Vector2(5f, 3f)));
            CreateFloor("Boden Wohnzimmer", floors, new Rect(1f, 2f, 5f, 7f), CreateGridMaterial("Boden_Wohnzimmer", new Color(0.85f, 0.62f, 0.4f), new Vector2(5f, 7f)));
            CreateFloor("Boden Schlafzimmer", floors, new Rect(-6f, 9f, 12f, 3f), CreateGridMaterial("Boden_Schlafzimmer", new Color(0.86f, 0.78f, 0.62f), new Vector2(12f, 3f)));

            var walls = new GameObject("Waende").transform;
            walls.SetParent(flat, false);
            BuildWall("Aussenwand Sued", walls, new Vector3(-6, 0, 2), new Vector3(6, 0, 2), s_OuterWall, openings: new[] { Door(6f, 1.2f), Window(2.5f), Window(9.5f) });
            BuildWall("Aussenwand Nord", walls, new Vector3(-6, 0, 12), new Vector3(6, 0, 12), s_OuterWall, openings: new[] { Window(1.5f), Window(9f) });
            BuildWall("Aussenwand West", walls, new Vector3(-6, 0, 2), new Vector3(-6, 0, 12), s_OuterWall, openings: new[] { Window(2f), Window(5.5f, 0.8f, 1.4f), Window(8.5f) });
            BuildWall("Aussenwand Ost", walls, new Vector3(6, 0, 2), new Vector3(6, 0, 12), s_OuterWall, openings: new[] { Window(3.5f, 2f), Window(8.5f) });
            BuildWall("Flurwand West", walls, new Vector3(-1, 0, 2), new Vector3(-1, 0, 9), s_InnerWall, openings: new[] { Door(2f), Door(5.5f, 0.9f) }, extendEnds: false);
            BuildWall("Flurwand Ost", walls, new Vector3(1, 0, 2), new Vector3(1, 0, 9), s_InnerWall, openings: new[] { Door(3.5f) }, extendEnds: false);
            BuildWall("Wand Kueche-Bad", walls, new Vector3(-6, 0, 6), new Vector3(-1, 0, 6), s_InnerWall, extendEnds: false);
            BuildWall("Wand Schlafzimmer", walls, new Vector3(-6, 0, 9), new Vector3(6, 0, 9), s_InnerWall, openings: new[] { Door(6f) }, extendEnds: false);

            // Room signs above the doors, readable from the hallway; house sign next to the entrance.
            var signs = new GameObject("Raumschilder").transform;
            signs.SetParent(flat, false);
            var offset = k_WallThickness / 2 + 0.01f;
            CreateSign("Schild Kueche", signs, new Vector3(-1 + offset, 2.35f, 4f), Vector3.right, new Vector2(1f, 0.3f), "Küche", 80);
            CreateSign("Schild Bad", signs, new Vector3(-1 + offset, 2.35f, 7.5f), Vector3.right, new Vector2(1f, 0.3f), "Bad", 80);
            CreateSign("Schild Wohnzimmer", signs, new Vector3(1 - offset, 2.35f, 5.5f), Vector3.left, new Vector2(1.4f, 0.3f), "Wohnzimmer (Tatort)", 70);
            CreateSign("Schild Schlafzimmer", signs, new Vector3(0f, 2.35f, 9 - offset), Vector3.back, new Vector2(1.2f, 0.3f), "Schlafzimmer", 80);
            CreateSign("Hausschild", signs, new Vector3(1.6f, 1.7f, 2 - offset), Vector3.back, new Vector2(0.9f, 0.4f), "Musterweg 7\nWohnung Erdgeschoss", 50);

            var furniture = new GameObject("Einrichtung").transform;
            furniture.SetParent(flat, false);
            BuildLivingRoom(furniture);

            var kitchen = Group("Kueche", furniture);
            CreateBox("Arbeitsplatte", kitchen, new Vector3(-5.6f, 0.45f, 3.8f), new Vector3(0.6f, 0.9f, 2.6f), s_LightFurniture);
            CreateBox("Kuehlschrank", kitchen, new Vector3(-5.6f, 0.9f, 5.5f), new Vector3(0.65f, 1.8f, 0.65f), s_LightFurniture);
            CreateBox("Kuechentisch", kitchen, new Vector3(-3f, 0.375f, 3.6f), new Vector3(1f, 0.75f, 0.8f), s_Furniture);

            var bath = Group("Bad", furniture);
            CreateBox("Badewanne", bath, new Vector3(-5f, 0.275f, 8.55f), new Vector3(1.7f, 0.55f, 0.75f), s_LightFurniture);
            CreateBox("WC", bath, new Vector3(-3.2f, 0.225f, 8.65f), new Vector3(0.4f, 0.45f, 0.6f), s_LightFurniture);
            CreateBox("Waschbecken", bath, new Vector3(-2f, 0.425f, 8.7f), new Vector3(0.6f, 0.85f, 0.45f), s_LightFurniture);

            var bedroom = Group("Schlafzimmer", furniture);
            CreateBox("Bett", bedroom, new Vector3(3.5f, 0.25f, 11f), new Vector3(1.6f, 0.5f, 2f), s_Fabric);
            CreateBox("Nachttisch", bedroom, new Vector3(2.35f, 0.25f, 11.7f), new Vector3(0.45f, 0.5f, 0.4f), s_Furniture);
            CreateBox("Kleiderschrank", bedroom, new Vector3(-1.8f, 1f, 11.6f), new Vector3(2f, 2f, 0.6f), s_Furniture);

            var hall = Group("Flur", furniture);
            CreateBox("Kommode", hall, new Vector3(0.75f, 0.45f, 7.8f), new Vector3(0.35f, 0.9f, 0.8f), s_Furniture);
        }

        static void BuildLivingRoom(Transform furniture)
        {
            var living = Group("Wohnzimmer", furniture);
            CreateBox("Sofa", living, new Vector3(5.45f, 0.225f, 6f), new Vector3(0.9f, 0.45f, 2f), s_Fabric);
            CreateBox("Sofa Lehne", living, new Vector3(5.8f, 0.6f, 6f), new Vector3(0.2f, 0.75f, 2f), s_Fabric);
            CreateBox("Couchtisch", living, new Vector3(4.4f, 0.2f, 6f), new Vector3(0.6f, 0.4f, 1f), s_Furniture);
            CreateBox("Teppich", living, new Vector3(4.3f, k_FloorTop + 0.003f, 6f), new Vector3(2.2f, 0.006f, 1.6f), CreateMaterial("Teppich", new Color(0.55f, 0.18f, 0.16f)), collider: false);
            CreateBox("Esstisch", living, new Vector3(3f, 0.375f, 3.4f), new Vector3(1.2f, 0.75f, 0.8f), s_Furniture);
            CreateBox("Stuhl", living, new Vector3(3f, 0.225f, 2.75f), new Vector3(0.45f, 0.45f, 0.45f), s_Furniture);
            var tipped = CreateBox("Stuhl (umgekippt)", living, new Vector3(2.3f, 0.23f, 4.4f), new Vector3(0.45f, 0.45f, 0.45f), s_Furniture);
            tipped.transform.localRotation = Quaternion.Euler(75f, 30f, 0f);
            CreateBox("Regal", living, new Vector3(3.5f, 0.9f, 8.7f), new Vector3(1.6f, 1.8f, 0.4f), s_Furniture);

            var stain = CreateBox("Fleck", living, new Vector3(2.7f, k_FloorTop + 0.002f, 5f), new Vector3(0.6f, 0.004f, 0.4f), s_Stain, collider: false);
            stain.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            // Evidence markers are placed by the trainee (see TatortInteractionBuilder).
        }

        static void BuildTeleportAnchors(Transform root)
        {
            var anchorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_AnchorPrefabPath);
            if (anchorPrefab == null)
            {
                Debug.LogWarning("[TatortSetup] Teleport Anchor prefab not found, skipping teleport points");
                return;
            }

            var anchors = new GameObject("Teleportpunkte").transform;
            anchors.SetParent(root, false);
            CreateAnchor(anchorPrefab, anchors, "Teleportpunkt Start", StartPosition + new Vector3(0f, k_FloorTop, 0f), Vector3.forward);
            CreateAnchor(anchorPrefab, anchors, "Teleportpunkt Eingang", new Vector3(0f, k_FloorTop, 0.9f), Vector3.forward);
            CreateAnchor(anchorPrefab, anchors, "Teleportpunkt Flur", new Vector3(0f, k_FloorTop, 4.5f), Vector3.forward);
            CreateAnchor(anchorPrefab, anchors, "Teleportpunkt Tatort", new Vector3(1.8f, k_FloorTop, 7.9f), new Vector3(1f, 0f, -1.2f));
        }

        static void CreateAnchor(GameObject prefab, Transform parent, string name, Vector3 position, Vector3 lookDirection)
        {
            var anchor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            anchor.name = name;
            anchor.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookDirection.normalized));
            foreach (var interactable in anchor.GetComponentsInChildren<BaseTeleportationInteractable>(true))
                interactable.interactionLayers = s_TeleportLayers;
        }

        static void BuildInfoBoards(Transform root)
        {
            var boards = new GameObject("Infotafeln").transform;
            boards.SetParent(root, false);
            var eye = StartPosition + new Vector3(0f, 1.5f, 0f);

            var statusPos = new Vector3(1.5f, 1.45f, -0.6f);
            CreateBox("Staender Status", boards, new Vector3(statusPos.x, 0.55f, statusPos.z), new Vector3(0.06f, 1.1f, 0.06f), s_Fence);
            CreateSign("Titel Status", boards, statusPos + new Vector3(0, 0.38f, 0), FlatDirection(eye - statusPos), new Vector2(1.2f, 0.12f), "Controller-Status", 44);
            var status = CreateSign("Controller-Status", boards, statusPos, FlatDirection(eye - statusPos), new Vector2(1.2f, 0.62f), "Warte auf XR …", 24, alignment: TextAnchor.UpperLeft);
            var display = status.transform.parent.gameObject.AddComponent<ControllerStatusDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("m_StatusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            var helpPos = new Vector3(-1.5f, 1.45f, -0.6f);
            CreateBox("Staender Steuerung", boards, new Vector3(helpPos.x, 0.55f, helpPos.z), new Vector3(0.06f, 1.1f, 0.06f), s_Fence);
            CreateSign("Steuerung", boards, helpPos, FlatDirection(eye - helpPos), new Vector2(1.2f, 0.74f),
                "Steuerung\n\nLinker Stick: gehen\nRechter Stick nach vorn: Teleport zielen, loslassen springt\n" +
                "Rechter Stick seitlich: drehen\nGrip: greifen, Knopf drücken\nTrigger: Kamera auslösen / Pistole abfeuern\n" +
                "Linke Hüfte: Beweisbeutel, rechte Hüfte: Pistole,\nvor dem Bauch: Kamera. Holster lädt die Pistole nach.", 22, alignment: TextAnchor.UpperLeft);
        }

        internal static Vector3 FlatDirection(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        // Axis-aligned wall from 'from' to 'to' on the ground, split around door and window openings.
        // Outer walls extend each end by half the thickness so corners close; inner walls stop at the
        // centre line of the wall they meet, otherwise their end faces z-fight with the facade.
        static void BuildWall(string name, Transform parent, Vector3 from, Vector3 to, Material material,
            float height = k_WallHeight, float thickness = k_WallThickness, Opening[] openings = null, bool extendEnds = true)
        {
            var wall = new GameObject(name).transform;
            wall.SetParent(parent, false);
            var direction = to - from;
            var length = direction.magnitude;
            wall.localPosition = from;
            wall.localRotation = Quaternion.FromToRotation(Vector3.right, direction / length);

            var extension = extendEnds ? thickness / 2 : 0f;
            var cursor = -extension;
            foreach (var opening in (openings ?? new Opening[0]).OrderBy(o => o.Start))
            {
                AddWallPiece(wall, cursor, opening.Start, 0f, height, thickness, material);
                AddWallPiece(wall, opening.Start, opening.End, 0f, opening.Bottom, thickness, material);
                AddWallPiece(wall, opening.Start, opening.End, opening.Top, height, thickness, material);
                if (opening.Bottom > 0f)
                    AddWallPiece(wall, opening.Start, opening.End, opening.Bottom, opening.Top, 0.02f, s_Glass, "Fenster");
                cursor = opening.End;
            }

            AddWallPiece(wall, cursor, length + extension, 0f, height, thickness, material);
        }

        static void AddWallPiece(Transform wall, float x0, float x1, float y0, float y1, float thickness, Material material, string name = "Segment")
        {
            if (x1 - x0 < 0.01f || y1 - y0 < 0.01f)
                return;
            var piece = CreateBox(name, wall, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, 0f), new Vector3(x1 - x0, y1 - y0, thickness), material);
            if (material == s_Glass)
                piece.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Thin floor slab on top of the ground; each one is its own teleport area.
        static void CreateFloor(string name, Transform parent, Rect rect, Material material)
        {
            var floor = CreateBox(name, parent, new Vector3(rect.center.x, k_FloorTop / 2, rect.center.y), new Vector3(rect.width, k_FloorTop, rect.height), material);
            MakeTeleportArea(floor);
        }

        static void MakeTeleportArea(GameObject target)
        {
            var area = target.AddComponent<TeleportationArea>();
            area.interactionLayers = s_TeleportLayers;
        }

        static Transform Group(string name, Transform parent)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        internal static GameObject CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool collider = true, bool isStatic = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(box.GetComponent<Collider>());
            if (isStatic)
                MarkStatic(box);
            return box;
        }

        // Static batching keeps draw calls low on the Quest's mobile GPU.
        static void MarkStatic(GameObject go) =>
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        // World-space UI panel with a text label. 'readFrom' points from the sign towards the reader.
        internal static Text CreateSign(string name, Transform parent, Vector3 worldPosition, Vector3 readFrom, Vector2 sizeMetres, string text, int fontSize,
            Color? background = null, Color? textColor = null, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = sizeMetres / k_CanvasScale;
            rect.localScale = Vector3.one * k_CanvasScale;
            rect.SetPositionAndRotation(worldPosition, Quaternion.LookRotation(-readFrom));

            var panel = new GameObject("Hintergrund", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(go.transform, false);
            Stretch((RectTransform)panel.transform, 0f);
            panel.GetComponent<Image>().color = background ?? new Color(0.12f, 0.13f, 0.16f, 0.92f);

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch((RectTransform)label.transform, sizeMetres.y / k_CanvasScale * 0.06f);
            var uiText = label.GetComponent<Text>();
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = textColor ?? Color.white;
            uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
            uiText.verticalOverflow = VerticalWrapMode.Truncate;
            uiText.text = text;
            return uiText;
        }

        static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        // Name layer 31 "Teleport" like the Starter Assets expect.
        static int EnsureTeleportLayer() => EnsureInteractionLayer(k_TeleportLayerIndex, "Teleport");

        // XRI keeps interaction layer names in an asset; returns the layer's mask.
        internal static int EnsureInteractionLayer(int index, string layerName)
        {
            var settingsPath = AssetDatabase.FindAssets("t:InteractionLayerSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (settingsPath != null)
            {
                var so = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(settingsPath));
                var names = so.FindProperty("m_LayerNames");
                if (names != null && names.arraySize > index && names.GetArrayElementAtIndex(index).stringValue != layerName)
                {
                    names.GetArrayElementAtIndex(index).stringValue = layerName;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }
            }

            return 1 << index;
        }

        internal static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{k_Root}/Materials/{name}.mat");

        static Material CreateGridMaterial(string name, Color tint, Vector2 sizeMetres) =>
            CreateMaterial(name, tint, CreateGridTexture(), sizeMetres);

        internal static Material CreateMaterial(string name, Color color, Texture2D texture = null, Vector2? tiling = null, Color? emission = null)
        {
            var path = $"{k_Root}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            }

            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static Material CreateGlassMaterial() => CreateTransparentMaterial("Glas", new Color(0.7f, 0.85f, 0.95f, 0.25f), 0.9f);

        internal static Material CreateTransparentMaterial(string name, Color color, float smoothness)
        {
            var material = CreateMaterial(name, color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D CreateGridTexture()
        {
            const string path = k_Root + "/Textures/Raster_1m.png";
            if (!File.Exists(path))
            {
                const int size = 256, line = 3;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var background = new Color(0.6f, 0.6f, 0.6f);
                var lineColor = new Color(0.35f, 0.35f, 0.35f);
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var onLine = x < line || y < line || x >= size - line || y >= size - line;
                    texture.SetPixel(x, y, onLine ? lineColor : background);
                }

                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
