using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;

public static class ShelfSceneSetup
{
    [MenuItem("ShelfLife/Setup Sample Scene")]
    public static void SetupScene()
    {
        // --- Cleanup stale objects from previous runs (idempotent) ---
        CleanupOldObjects();

        // --- Materials ---
        string matFolder = "Assets/_ShelfLife/Materials";
        if (!AssetDatabase.IsValidFolder(matFolder))
        {
            AssetDatabase.CreateFolder("Assets/_ShelfLife", "Materials");
        }

        Material matWood   = CreateMat(matFolder, "Mat_Wood",      new Color32(160, 120,  80, 255));
        Material matMetal  = CreateMat(matFolder, "Mat_Metal",     new Color32(180, 180, 185, 255), 0.6f, 0.3f);
        Material matFloor  = CreateMat(matFolder, "Mat_FloorTile",  new Color32(215, 200, 175, 255), 0.4f);
        Material matRed    = CreateMat(matFolder, "Mat_Red",       new Color32(200,  40,  40, 255));
        Material matGreen  = CreateMat(matFolder, "Mat_Green",     new Color32( 50, 170,  60, 255));
        Material matYellow = CreateMat(matFolder, "Mat_Yellow",    new Color32(230, 210,  50, 255));
        Material matOrange = CreateMat(matFolder, "Mat_Orange",    new Color32(230, 130,  30, 255));
        Material matBlue   = CreateMat(matFolder, "Mat_DarkBlue",  new Color32( 30,  50, 140, 255));
        Material matWhite  = CreateMat(matFolder, "Mat_White",     new Color32(240, 240, 240, 255));
        Material matPurple = CreateMat(matFolder, "Mat_Purple",    new Color32(130,  50, 160, 255));
        Material matBrown  = CreateMat(matFolder, "Mat_Brown",     new Color32(110,  70,  40, 255));
        Material matPink   = CreateMat(matFolder, "Mat_Pink",      new Color32(220, 130, 160, 255));
        Material matDkGrn  = CreateMat(matFolder, "Mat_DarkGreen", new Color32( 30, 100,  45, 255));
        Material matBack   = CreateMat(matFolder, "Mat_BackPanel", new Color32(160, 155, 150, 255), 0.1f, 0f);

        // --- Camera ---
        Camera cam = Camera.main;
        if (cam == null)
        {
            // Fallback: find any camera in the scene
            cam = Object.FindAnyObjectByType<Camera>();
        }
        if (cam != null)
        {
            // Ensure the camera is tagged so Camera.main resolves at runtime
            cam.gameObject.tag = "MainCamera";

            // Diorama Cinematic Angle v3 – Controlled Entrance View
            // SYNC: Must match CameraStateManager WideShot baseline (k_WidePos, k_DioramaRot, k_WideFOV)
            cam.transform.position = new Vector3(-4.8f, 6.9f, -6.2f);
            cam.transform.rotation = Quaternion.Euler(47f, 36f, 0f);
            cam.fieldOfView = 27f;
            cam.nearClipPlane = 0.1f;

            var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCam != null)
            {
                urpCam.renderPostProcessing = true;
            }
        }
        else
        {
            Debug.LogError("[ShelfLife] No camera found in scene — InputManager raycast will fail.");
        }

        // --- Floor (Cube for NavMesh baking — flat slab at Y=0) ---
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.05f, 0f);   // top surface at Y=0
        floor.transform.localScale = new Vector3(12f, 0.1f, 10f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = matFloor;
        GameObjectUtility.SetStaticEditorFlags(floor,
            StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

        // --- Floor stain details (visual density) ---
        AddFloorStains(matFolder);

        // --- Shelf Parent ---
        GameObject shelf = new GameObject("ShelfUnit");
        shelf.transform.position = new Vector3(0f, 0f, 0.5f);

        // BoxCollider prevents characters walking through the display
        BoxCollider shelfCollider = shelf.AddComponent<BoxCollider>();
        shelfCollider.center = new Vector3(0f, 1f, 0f);    // centered vertically in shelf
        shelfCollider.size   = new Vector3(4f, 2f, 0.8f);  // matches shelf dimensions

        // NavMeshObstacle carves a hole so agents path around the display
        NavMeshObstacle shelfObstacle = shelf.AddComponent<NavMeshObstacle>();
        shelfObstacle.carving = true;
        shelfObstacle.center  = new Vector3(0f, 1f, 0f);
        shelfObstacle.size    = new Vector3(4f, 2f, 0.8f);

        // Shelf geometry constants
        float shelfWidth   = 4f;
        float shelfDepth   = 0.8f;
        float shelfThick   = 0.04f;
        float supportWidth = 0.06f;
        float supportHeight = 2.0f;

        float[] shelfHeights = { 0.5f, 1.0f, 1.5f };

        // Horizontal shelf planks
        for (int i = 0; i < shelfHeights.Length; i++)
        {
            GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = $"Shelf_Row{i}";
            plank.transform.SetParent(shelf.transform);
            plank.transform.localPosition = new Vector3(0f, shelfHeights[i], 0f);
            plank.transform.localScale = new Vector3(shelfWidth, shelfThick, shelfDepth);
            plank.GetComponent<MeshRenderer>().sharedMaterial = matWood;
        }

        // Back panel (thin board behind the shelves)
        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "Shelf_Back";
        back.transform.SetParent(shelf.transform);
        back.transform.localPosition = new Vector3(0f, supportHeight * 0.5f, shelfDepth * 0.5f - 0.01f);
        back.transform.localScale = new Vector3(shelfWidth, supportHeight, 0.02f);
        back.GetComponent<MeshRenderer>().sharedMaterial = matBack;

        // Vertical supports (left & right)
        float supportX = (shelfWidth * 0.5f) - (supportWidth * 0.5f);
        foreach (float sign in new float[] { -1f, 1f })
        {
            GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            support.name = sign < 0 ? "Support_Left" : "Support_Right";
            support.transform.SetParent(shelf.transform);
            support.transform.localPosition = new Vector3(sign * supportX, supportHeight * 0.5f, 0f);
            support.transform.localScale = new Vector3(supportWidth, supportHeight, shelfDepth);
            support.GetComponent<MeshRenderer>().sharedMaterial = matMetal;
        }

        // --- Diorama Focus Anchor (DOF target at island center) ---
        GameObject focusAnchor = new GameObject("DioramaFocusAnchor");
        focusAnchor.transform.position = new Vector3(0f, 1f, 0.5f); // center of display
        focusAnchor.AddComponent<DioramaFocusAnchor>();

        // --- Products ---
        // Spheres for fruits: Red, Green, Orange, Yellow, Pink
        // Boxes (cereal proportions) for: White, Purple, DarkBlue, Brown, DarkGreen
        Material[] productMats = {
            matRed, matGreen, matYellow, matOrange,   // row 0 (bottom) — 4 items
            matBlue, matWhite, matPurple,              // row 1 (middle) — 3 items
            matBrown, matPink, matDkGrn                // row 2 (top)    — 3 items
        };
        string[] productNames = {
            "Product_Red", "Product_Green", "Product_Yellow", "Product_Orange",
            "Product_DarkBlue", "Product_White", "Product_Purple",
            "Product_Brown", "Product_Pink", "Product_DarkGreen"
        };
        // true = sphere (fruit), false = box (cereal/bottle)
        bool[] isFruit = {
            true, true, true, true,       // Red, Green, Yellow, Orange = fruits
            false, false, false,           // DarkBlue, White, Purple = boxes
            false, true, false             // Brown = box, Pink = fruit, DarkGreen = box
        };

        // Layout: row -> x positions
        float[][] rowX = {
            new float[] { -1.3f, -0.45f,  0.45f,  1.3f },   // 4 items (wider for 4-unit shelf)
            new float[] { -0.9f,  0f,      0.9f          },   // 3 items
            new float[] { -1.0f, -0.1f,    0.85f         }    // 3 items (offset for variety)
        };

        // Shapes: fruits = Sphere (0.9, 0.8, 0.9) scale, boxes = Cube (0.6, 1.1, 0.4) scale
        // Both multiplied by 0.3 base size
        float baseSize = 0.3f;

        int idx = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < rowX[row].Length; col++)
            {
                bool fruit = isFruit[idx];

                // Create the right primitive
                PrimitiveType primType = fruit ? PrimitiveType.Sphere : PrimitiveType.Cube;
                GameObject prod = GameObject.CreatePrimitive(primType);
                prod.name = productNames[idx];
                prod.tag = "Product";

                // Scale: fruits are roughly spherical, boxes are tall and thin
                Vector3 scale;
                if (fruit)
                    scale = new Vector3(0.9f, 0.8f, 0.9f) * baseSize;
                else
                    scale = new Vector3(0.6f, 1.1f, 0.4f) * baseSize;

                prod.transform.localScale = scale;

                // Position: align bottom of product to top of shelf
                float halfHeight = scale.y * 0.5f;
                float baseY = shelfHeights[row] + (shelfThick * 0.5f) + halfHeight;
                prod.transform.position = new Vector3(rowX[row][col], baseY, 0.1f); // front face of shelf

                prod.GetComponent<MeshRenderer>().sharedMaterial = productMats[idx];

                // Ensure collider exists and is NOT a trigger (so raycasts hit it)
                // Spheres get SphereCollider by default, Cubes get BoxCollider
                Collider existingCol = prod.GetComponent<Collider>();
                if (existingCol != null)
                {
                    if (existingCol is SphereCollider sc) sc.isTrigger = false;
                    else if (existingCol is BoxCollider bc) bc.isTrigger = false;
                }

                // ProductFall component — unconditional add with randomized physics
                ProductFall fall = prod.AddComponent<ProductFall>();
                Debug.Assert(fall != null,
                    $"[ShelfLife] CRITICAL: ProductFall missing on '{prod.name}' after AddComponent!");

                // Randomize fall duration: 1.2s (heavy/fast) → 2.5s (light/floaty)
                fall.fallDuration = Random.Range(1.2f, 2.5f);

                // Randomize curve style for variety
                fall.fallCurve = GenerateRandomFallCurve(idx);

                // FaceOverlay component (personality face)
                prod.AddComponent<FaceOverlay>();

                // FaceOverlay child — white quad SpriteRenderer (placeholder face)
                CreateFaceChild(prod, baseSize);

                idx++;
            }
        }

        // --- InputManager + GameplayLinker ---
        SetupInputManager();

        // --- Characters (Player + Customer capsules) ---
        SetupCharacters(matGreen, matRed);

        // --- Post-Processing Volume ---
        SetupPostProcessing();

        // --- Directional Light ---
        SetupDirectionalLight();

        // --- Mark scene dirty so it saves ---
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        // --- Final verification: every Product-tagged object must have ProductFall ---
        GameObject[] allProducts = GameObject.FindGameObjectsWithTag("Product");
        int withFall = 0;
        foreach (GameObject p in allProducts)
        {
            ProductFall pf = p.GetComponent<ProductFall>();
            if (pf != null)
            {
                withFall++;
                Debug.Log($"[ShelfLife]   {p.name}: fallDuration={pf.fallDuration:F2}s, curveKeys={pf.fallCurve.length}");
            }
            else
            {
                Debug.LogError($"[ShelfLife] VERIFY FAIL: '{p.name}' tagged Product but MISSING ProductFall!");
            }
        }
        Debug.Log($"[ShelfLife] Scene setup complete — {allProducts.Length} products, {withFall}/{allProducts.Length} have ProductFall. Shelf, floor, camera, post-processing, lighting OK.");
    }

    /// <summary>
    /// Destroys all objects created by a previous run so the setup is fully idempotent.
    /// </summary>
    static void CleanupOldObjects()
    {
        int destroyed = 0;

        // Named singletons + runtime-created canvases
        string[] namedObjects = {
            "Floor", "FloorStains", "ShelfUnit", "DioramaFocusAnchor",
            "InputManager", "PostProcessing",
            "ScoreCanvas", "CatchFlashCanvas", "SubtitleCanvas", "ReceiptCanvas",
            "WalkieTalkieCanvas", "CustomerHand", "PlayerCapsule", "CustomerCapsule"
        };
        foreach (string objName in namedObjects)
        {
            GameObject old = GameObject.Find(objName);
            while (old != null)
            {
                Debug.Log($"[ShelfLife] Cleanup — destroying old '{old.name}'");
                Object.DestroyImmediate(old);
                destroyed++;
                old = GameObject.Find(objName);          // loop in case duplicates exist
            }
        }

        // All Product-tagged objects (covers every product cube from prior runs)
        GameObject[] taggedProducts = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject prod in taggedProducts)
        {
            Debug.Log($"[ShelfLife] Cleanup — destroying old product '{prod.name}'");
            Object.DestroyImmediate(prod);
            destroyed++;
        }

        // Fallback: find any product by name pattern in case tag was missing
        string[] productPrefixes = {
            "Product_Red", "Product_Green", "Product_Yellow", "Product_Orange",
            "Product_DarkBlue", "Product_White", "Product_Purple",
            "Product_Brown", "Product_Pink", "Product_DarkGreen"
        };
        foreach (string pName in productPrefixes)
        {
            GameObject leftover = GameObject.Find(pName);
            while (leftover != null)
            {
                Debug.Log($"[ShelfLife] Cleanup — destroying leftover '{leftover.name}' (found by name)");
                Object.DestroyImmediate(leftover);
                destroyed++;
                leftover = GameObject.Find(pName);
            }
        }

        if (destroyed > 0)
            Debug.Log($"[ShelfLife] Cleanup complete — {destroyed} old object(s) destroyed.");
        else
            Debug.Log("[ShelfLife] Cleanup — no old objects found (fresh scene).");
    }

    static void SetupInputManager()
    {
        // Old objects already removed by CleanupOldObjects()
        GameObject go = new GameObject("InputManager");
        go.AddComponent<InputManager>();
        go.AddComponent<GameplayLinker>();
        go.AddComponent<GradeManager>();
        go.AddComponent<GradeUIManager>();
        go.AddComponent<ProductSpawner>();
        go.AddComponent<ScoreManager>();
        go.AddComponent<CatchFeedback>();
        go.AddComponent<VisualSilenceController>();
        go.AddComponent<SubtitleSystem>();
        go.AddComponent<ReceiptScreen>();
        go.AddComponent<CascadeSystem>();
        go.AddComponent<WalkieTalkieSystem>();
        go.AddComponent<CameraStateManager>();

        Debug.Log("[ShelfLife] All managers created: InputManager, GameplayLinker, GradeManager, GradeUIManager, ProductSpawner, ScoreManager, CatchFeedback, VisualSilenceController, SubtitleSystem, ReceiptScreen, CascadeSystem, WalkieTalkieSystem, CameraStateManager.");
    }

    static void SetupCharacters(Material matGreen, Material matRed)
    {
        // ── Player capsule (green, left of shelf) ──────────────────
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "PlayerCapsule";
        player.transform.position   = new Vector3(-3f, 0.5f, -2f);
        player.transform.localScale = new Vector3(1f, 0.9f, 1f); // slouched idle
        player.GetComponent<MeshRenderer>().sharedMaterial = matGreen;

        // StealthBehavior drives all personality
        player.AddComponent<StealthBehavior>();

        // NavMeshAgent for pathfinding pursuit
        NavMeshAgent playerAgent   = player.AddComponent<NavMeshAgent>();
        playerAgent.speed          = 3f;
        playerAgent.angularSpeed   = 180f;
        playerAgent.radius         = 0.3f;
        playerAgent.height         = 1.0f;
        playerAgent.stoppingDistance = 0.5f;

        // ── Customer capsule (red, off-screen right) ───────────────
        GameObject customer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        customer.name = "CustomerCapsule";
        customer.transform.position   = new Vector3(4f, 0.5f, 3f);
        customer.transform.localScale = Vector3.one;
        customer.GetComponent<MeshRenderer>().sharedMaterial = matRed;

        // CustomerController drives AI — must live on the capsule so transform.position moves it
        customer.AddComponent<CustomerController>();

        // NavMeshAgent for pathfinding walk
        NavMeshAgent customerAgent   = customer.AddComponent<NavMeshAgent>();
        customerAgent.speed          = 2f;
        customerAgent.angularSpeed   = 120f;
        customerAgent.radius         = 0.3f;
        customerAgent.height         = 1.0f;
        customerAgent.stoppingDistance = 0.3f;

        Debug.Log("[ShelfLife] Characters created: PlayerCapsule (green, (-3, 0.5, -2), StealthBehavior + NavMeshAgent), " +
                  "CustomerCapsule (red, entrance at (4, 0.5, 3), CustomerController + NavMeshAgent).");
    }

    static void SetupPostProcessing()
    {
        // Old objects already removed by CleanupOldObjects()

        // Create Volume Profile asset
        string profileFolder = "Assets/_ShelfLife/Settings";
        if (!AssetDatabase.IsValidFolder(profileFolder))
            AssetDatabase.CreateFolder("Assets/_ShelfLife", "Settings");

        string profilePath = $"{profileFolder}/TiltShiftProfile.asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        else
        {
            // Clear old overrides when re-running
            profile.components.Clear();
        }

        // ── Depth of Field — Bokeh tilt-shift, shelf in sharp focus ──
        // Camera at (-4.8, 6.9, -6.2), shelf at ~(0, 1, 0.5) → distance ~10 units along view axis
        // Focus Distance 10 keeps the full shelf plane sharp from the diorama angle
        // f/2.8 blurs only extreme foreground/background, not the shelf
        var dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(10f);      // shelf plane sharp at camera distance ~10 units
        dof.focalLength.Override(50f);        // standard lens — natural perspective
        dof.aperture.Override(2.8f);          // f/2.8 — gentle blur, products stay crisp

        // ── Bloom — warm tilt-shift glow, bumped to 0.4 ──
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.4f);
        bloom.scatter.Override(0.7f);

        // ── Color Adjustments — punch up for miniature feel ──
        var colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.saturation.Override(10f);    // +10 saturation (range -100 to +100)
        colorAdj.contrast.Override(5f);       // +5 contrast (subtle lift)

        // ── Vignette — subtle edge darkening ──
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.15f);

        // ── Chromatic Aberration — subtle lens fringing ──
        var chrAb = profile.Add<ChromaticAberration>(true);
        chrAb.intensity.Override(0.05f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Create the Volume GameObject
        GameObject ppGo = new GameObject("PostProcessing");
        var volume = ppGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        Debug.Log("[ShelfLife] Post-processing configured — Bokeh DoF (f/2.8, 50mm, focus 5.5), " +
                  "Bloom 0.4, Color Adjustments (+10 sat, +5 contrast), Vignette, Chromatic Aberration.");
    }

    static void SetupDirectionalLight()
    {
        // Find the existing directional light
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        Light dirLight = null;
        foreach (var l in lights)
        {
            if (l.type == LightType.Directional)
            {
                dirLight = l;
                break;
            }
        }

        if (dirLight == null)
        {
            Debug.LogWarning("[ShelfLife] No directional light found in scene.");
            return;
        }

        // Warm light color: RGB(255, 230, 200) normalized
        dirLight.color = new Color(255f / 255f, 230f / 255f, 200f / 255f, 1f);
        dirLight.intensity = 1.2f;
        dirLight.shadows = LightShadows.Soft;

        // URP additional light data
        var urpLight = dirLight.GetComponent<UniversalAdditionalLightData>();
        if (urpLight != null)
        {
            urpLight.softShadowQuality = SoftShadowQuality.Medium;
        }

        Debug.Log("[ShelfLife] Directional light — warm color, intensity 1.2, soft shadows.");
    }

    /// <summary>
    /// Generates a variety of fall curves so each product feels different:
    ///  0,1,2 — Heavy drop: fast start, ease-out (cans, jars)
    ///  3,4,5 — Linear fall: constant speed (boxes)
    ///  6,7   — Light/floaty: slow start, accelerates (chips, bread)
    ///  8,9   — Bounce: overshoots then settles (rubber, fruit)
    /// </summary>
    static AnimationCurve GenerateRandomFallCurve(int productIndex)
    {
        int style = productIndex % 4;

        switch (style)
        {
            case 0: // Heavy drop — fast start, decelerates at floor (ease-out)
                return new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 3f),     // steep launch
                    new Keyframe(1f, 1f, 0f, 0f)      // soft landing
                );

            case 1: // Linear — constant speed, no easing
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);

            case 2: // Light/floaty — slow start, accelerates into floor (ease-in)
                return new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),     // gentle start
                    new Keyframe(1f, 1f, 3f, 0f)      // fast at end
                );

            case 3: // Bounce — overshoots past floor, then settles back
                return new AnimationCurve(
                    new Keyframe(0f,   0f,  0f, 0f),
                    new Keyframe(0.7f, 1.1f, 2f, 0f),  // overshoot to 110%
                    new Keyframe(1f,   1f,  0f, 0f)     // settle back to floor
                );

            default:
                return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    /// <summary>
    /// Creates a child GameObject with a SpriteRenderer on the product.
    /// This is the placeholder "face" that FaceOverlay animates.
    /// White circle sprite, positioned slightly in front of the product.
    /// </summary>
    static void CreateFaceChild(GameObject product, float baseSize)
    {
        GameObject faceGo = new GameObject("Face");
        faceGo.transform.SetParent(product.transform, false);
        faceGo.transform.localPosition = new Vector3(0f, 0f, -0.55f); // slightly in front
        faceGo.transform.localScale    = Vector3.one * 0.6f; // 60% of product size

        SpriteRenderer sr = faceGo.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite(32); // white circle placeholder face
        sr.color  = new Color(1f, 1f, 1f, 0.9f);
        sr.sortingOrder = 50;
        sr.enabled = false; // FaceOverlay enables it on trigger

        // Add two tiny "eye" dots as children
        float eyeSpacing = 0.25f;
        float eyeY       = 0.1f;
        for (int i = 0; i < 2; i++)
        {
            GameObject eyeGo = new GameObject(i == 0 ? "EyeL" : "EyeR");
            eyeGo.transform.SetParent(faceGo.transform, false);
            float xSign = i == 0 ? -1f : 1f;
            eyeGo.transform.localPosition = new Vector3(xSign * eyeSpacing, eyeY, -0.01f);
            eyeGo.transform.localScale    = Vector3.one * 0.2f;

            SpriteRenderer eyeSr = eyeGo.AddComponent<SpriteRenderer>();
            eyeSr.sprite = CreateCircleSprite(16);
            eyeSr.color  = Color.black;
            eyeSr.sortingOrder = 51;
        }

        // Flat mouth line
        GameObject mouthGo = new GameObject("Mouth");
        mouthGo.transform.SetParent(faceGo.transform, false);
        mouthGo.transform.localPosition = new Vector3(0f, -0.15f, -0.01f);
        mouthGo.transform.localScale    = new Vector3(0.35f, 0.06f, 1f);

        SpriteRenderer mouthSr = mouthGo.AddComponent<SpriteRenderer>();
        mouthSr.sprite = CreateCircleSprite(16);
        mouthSr.color  = new Color(0.2f, 0.2f, 0.2f, 1f);
        mouthSr.sortingOrder = 51;
    }

    /// <summary>Generates a white circle sprite at runtime (no asset dependency).</summary>
    static Sprite CreateCircleSprite(int diameter)
    {
        Texture2D tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
        float radius   = diameter * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), center);
            tex.SetPixel(x, y, dist <= radius - 1f ? Color.white : Color.clear);
        }

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter),
            new Vector2(0.5f, 0.5f), 100f);
    }

    // ── Floor stain generation ───────────────────────────────────────

    /// <summary>
    /// Adds 8–15 semi-transparent stain quads on the floor for visual density.
    /// Grouped under a "FloorStains" parent for easy cleanup.
    /// </summary>
    static void AddFloorStains(string matFolder)
    {
        Material matStain = CreateTransparentMat(matFolder, "Mat_FloorStain",
            new Color32(140, 125, 100, 50), 0.5f);

        GameObject stainParent = new GameObject("FloorStains");
        stainParent.transform.position = Vector3.zero;

        int stainCount = Random.Range(8, 16); // 8–15 stains

        for (int i = 0; i < stainCount; i++)
        {
            GameObject stain = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stain.name = $"FloorStain_{i}";
            stain.transform.SetParent(stainParent.transform);

            // Remove collider — stains are visual only
            Object.DestroyImmediate(stain.GetComponent<BoxCollider>());

            // Random position on floor surface
            float x = Random.Range(-5f, 5f);
            float z = Random.Range(-4f, 4f);
            stain.transform.position = new Vector3(x, 0.002f, z); // barely above floor

            // Random patch size
            float sizeX = Random.Range(0.3f, 0.8f);
            float sizeZ = Random.Range(0.3f, 0.8f);
            stain.transform.localScale = new Vector3(sizeX, 0.002f, sizeZ);

            // Random subtle rotation
            stain.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            stain.GetComponent<MeshRenderer>().sharedMaterial = matStain;
        }

        Debug.Log($"[ShelfLife] {stainCount} floor stains added for visual density.");
    }

    // ── Material helpers ──────────────────────────────────────────────

    /// <summary>
    /// Creates a URP Lit material with alpha transparency enabled.
    /// Used for floor stains, decals, and other semi-transparent overlays.
    /// </summary>
    static Material CreateTransparentMat(string folder, string name, Color32 color, float smoothness)
    {
        string path = $"{folder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        Material mat = new Material(litShader);
        mat.SetColor("_BaseColor", (Color)color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);

        // URP transparency setup
        mat.SetFloat("_Surface", 1f);    // 0=Opaque, 1=Transparent
        mat.SetFloat("_Blend", 0f);      // 0=Alpha
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetInt("_SrcBlend", 5);      // SrcAlpha
        mat.SetInt("_DstBlend", 10);     // OneMinusSrcAlpha
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;           // Transparent queue
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material CreateMat(string folder, string name, Color32 color, float smoothness = 0.3f, float metallic = 0f)
    {
        string path = $"{folder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null)
        {
            Debug.LogError($"[ShelfLife] URP Lit shader not found — is URP installed?");
            litShader = Shader.Find("Standard");
        }

        Material mat = new Material(litShader);
        mat.SetColor("_BaseColor", (Color)color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
