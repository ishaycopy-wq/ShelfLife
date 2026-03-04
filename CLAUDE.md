# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

**Scene setup** (Editor only, idempotent):
- Unity menu: `ShelfLife > Setup Sample Scene` — runs `ShelfSceneSetup.cs` to build the full scene from scratch

**Identity validation** (run from `My project/` folder):
```powershell
powershell -File automation/scripts/validate_repo.ps1
```
Checks for Rigidbody usage and manual `transform.rotation` assignments.

**No build CLI** — builds are done through Unity Editor (File > Build Settings). Targets: Android IL2CPP ARM64 min-API 28, iOS IL2CPP min-iOS 15.

## Architecture

### Directory Layout
All game code lives under `Assets/_ShelfLife/`:
- `Core/` — game-wide singletons: `GameplayLinker`, `ScoreManager`, `InputManager`, `VisualSilenceController`
- `Gameplay/` — per-frame systems: `ProductFall`, `ProductSpawner`, `CascadeSystem`, `CustomerController`
- `Personality/` — character voice: `FaceOverlay`, `SubtitleSystem`, `WalkieTalkieSystem`
- `Player/` — employee mechanics: `StealthBehavior`, `CatchFeedback`
- `Camera/` — `CameraStateManager` (3 presets: WideShot / FollowShot / EngagementShot)
- `UI/` — `GradeUIManager`, `ReceiptScreen`
- `Environment/` — procedural scene builders (run once, not per-frame)
- `Editor/` — `ShelfSceneSetup.cs` (Editor-only, `[MenuItem]`)
- `Data/` — `ProductData.cs` ScriptableObjects

### Event Bus (Static C# Events)
Systems communicate entirely through static events — no direct references between managers:

| Event | Source | Listeners |
|---|---|---|
| `InputManager.OnProductTapped` | touch input | `GameplayLinker` |
| `ProductFall.OnProductHitFloor` | ProductFall | `GameplayLinker`, `ScoreManager`, `VisualSilenceController`, `CascadeSystem` |
| `GameplayLinker.OnProductCaught` | catch logic | `CustomerController` (slow-mo cancel) |
| `ScoreManager.OnShiftEnd` | 3 misses or checkout | `ReceiptScreen` |
| `CustomerController.OnChaosStarted` | customer phase | `StealthBehavior`, camera |
| `CustomerController.OnCustomerCheckout` | customer done | `ScoreManager` |
| `CustomerController.OnFirstCatchSlowMo` | chaos + AmbushReady | `CameraStateManager` |

### Singleton Pattern
`ScoreManager`, `ProductSpawner`, `CascadeSystem`, `CustomerController`, `VisualSilenceController`, `CameraStateManager`, `StealthBehavior` all use the same pattern: `public static T Instance { get; private set; }` set in `Awake()`, destroyed on duplicate.

### ProductFall State Machine
Products are scene objects tagged `"Product"`, named `"Product_[ColorKey]"` (e.g. `Product_Red`, `Product_DarkBlue`). `ProductFall` drives all movement:
- `StartFall()` — normal shelf drop, `AnimationCurve` Y lerp
- `LaunchForward(targetPos)` — chaos arc with shape-weighted `FallStyle` (TiredRoll / DramaticTumble / PanicBounce / DeadWeight / ChaoticSpin), hang-time freeze at peak, then post-landing phase
- `CatchProduct()` — scale-to-zero shrink, then `SetActive(false)`
- `ResetToShelf()` — full state reset for next shift

`OnProductHitFloor` fires **after** post-landing completes (not at impact). During post-landing, `IsPostLanding == true` and the product is still catchable.

### Cascade Chain
`CascadeSystem` listens to `OnProductHitFloor`. First-level trigger: 60% chance; chain: 40%. Products directly above (within 0.5 X units, row+1) are queued with 0.3s delay. VisualSilence fires **once** after the entire cascade ends, not per-product.

### Receipt Economy
`ScoreManager.ProcessCatch()` reads `product.name`, strips `"Product_"` prefix to look up price. Combo multiplier: ≤2→×1, 3–4→×1.5, 5–7→×2, 8–9→×3, 10+→×5. Currency is Israeli shekel ₪.

### Camera
Fixed diorama angle `Euler(47, 36, 0)` never changes. Only position and FOV shift between states. `ShelfSceneSetup` must match `CameraStateManager`'s `k_WidePos` / `k_WideFOV` constants — these are marked `// SYNC:` in both files.

### Non-Negotiable Constraints
- **No `FindObjectOfType()` in `Update()`** — cache in `Start()` or `Awake()`
- **No `Rigidbody`** — all movement via `AnimationCurve` + manual transform
- **No coroutines** — all timers are `Update()`-driven floats
- **Visual Silence** — exactly 1.5 s after cascade, `CascadeSystem` → `VisualSilenceController.TriggerSilence()`
- **Personality gate** — 40% chance, 20 s global cooldown (shared `static float s_LastTriggerTime`)
- **Camera Y** — always locked to `Euler(47, 36, 0)`; Y-only rotation changes are identity violations

### Shift Flow
`ProductSpawner.isPaused` and `CustomerController.isPaused` are the two external pause knobs. After `OnShiftEnd`: `ReceiptScreen` shows, "NEXT SHIFT" button calls `ScoreManager.ResetForNewShift()` + `ProductSpawner.ResetForNewShift()` + each `ProductFall.ResetToShelf()` + `CustomerController.ResetForNewCustomer()`.

## BLENDER PIPELINE
- Blender 5.0.1 at: C:\Program Files\Blender Foundation\Blender 5.0\blender.exe
- Generator script: Tools\Blender\generate_primitive.py
- Output folder: Assets\Models\Generated\
- Usage: blender --background --python Tools\Blender\generate_primitive.py -- --type cube --name NAME --scale X Y Z --output Assets\Models\Generated\NAME.fbx
- Types: cube, cylinder, sphere, plane
- Claude can run Blender headless to generate 3D assets without opening the GUI