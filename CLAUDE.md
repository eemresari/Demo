# Project: 3D Unity game (vibe-coded)

Unity **6.6 (6000.6.9f1)**, **URP 17.6**, **Input System only** (`activeInputHandler: 1` — legacy `Input.GetKey` will throw).
Started from the Unity **FPS Microgame** template. The user communicates in Turkish; reply in Turkish.

## Code layout

`Assets/FPS/Scripts/` — four assembly definitions; a type is only visible to assemblies that reference it:

| Folder | Assembly | Namespace | Key classes |
|---|---|---|---|
| `Game/` | `fps.Game` | `Unity.FPS.Game` | `Health`, `Damageable`, `WeaponController`, `EventManager`, `GameFlowManager` |
| `Gameplay/` | `fps.Gameplay` | `Unity.FPS.Gameplay` | `PlayerCharacterController`, `PlayerInputHandler` |
| `AI/` | `fps.AI` | `Unity.FPS.AI` | `EnemyController` |
| `UI/` | `fps.UI` | `Unity.FPS.UI` | HUD, menus |

New gameplay code goes in a matching folder/namespace. Reuse existing systems (health, damage, events, weapons) instead of writing parallel ones.
Input actions: `Assets/FPS/InputSystem_Actions.inputactions`. Scenes: `Assets/FPS/Scenes/` (main: `MainScene.unity`).

## Tools available

- **MCP for Unity** (`UnityMCP`, HTTP `127.0.0.1:8090/mcp` (8080 is taken by a Docker container), registered in local scope): drives the open Editor — scenes, GameObjects, components, scripts, console, screenshots, play mode. Skill: `unity-mcp-orchestrator`. Requires Unity open with *Window → MCP for Unity* showing the server running.
- **Official Unity plugin** (`/unity:using-unity`): Unity's own skills (URP, UI, input, builds, Unity CLI…).
- **awesome-gamedev skills** (project scope): unity-*, game-feel, camera-systems, level-design, fps-shooter, prototype-fast, etc.
- **Subagents** in `.claude/agents/`: `unity-playtester`, `unity-debugger`, `unity-code-reviewer`.

## Working rules

1. Before editing scenes or prefabs, prefer MCP tools over hand-editing `.unity`/`.prefab` YAML.
2. After any script change: wait until `mcpforunity://editor/state` reports not compiling, then `read_console` for errors. Never report a feature as done while the console has new errors.
3. Verify visual changes with a screenshot (`manage_camera` screenshot, `include_image=True`, `max_resolution` 512).
4. Always leave Play Mode stopped when finished.
5. Expose tunable values as `[SerializeField] private` fields so the user can tweak them in the Inspector.
6. Use URP/Lit materials, never Built-in shaders (they render pink).
7. Before large changes, suggest a git commit so the user can roll back.
8. Never edit `Library/`, `Temp/`, `Logs/`, or `.csproj`/`.slnx` files (Unity regenerates them).
