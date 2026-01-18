# MainViewModel / MainRenderer Refactor Plan

Goal: Split `MainViewModel` and `MainRenderer` into explicit, single‑responsibility components with shared layout metrics and no extra mapping/duplication.

## Phase 1: Shared UI Layout + Render Infrastructure
- Add `MainLayoutMetrics` (single source of truth for layout constants).
- Add render support types:
  - `MainPaintCache` (preallocated paints + theme access)
  - `MainHitTargetRegistry` (stores hit targets during draw)
  - `MainHitTester` (read‑only hit testing against registry)
  - `MainRenderPrimitives` (shared meters/knobs/text helpers)
- Split `MainRenderer` into draw passes (titlebar, hotbar, full/minimal, master, debug) with explicit composition.
- Keep `MainRenderer` API stable while delegating to sub‑renderers.

## Phase 2: Window Routing + Container Windows
- Create `MainWindowRouter` (settings + analyzer windows).
- Create `PluginWindowRouter` (plugin browser, plugin parameter windows, VST3 editor).
- Create `PluginContainerWindowManager` (container window lifecycle + view model sync + meter updates).
- Move all WPF window construction out of `MainViewModel`.

## Phase 3: Plugin Graph + Containers
- Create `MainPluginCoordinator`:
  - Graph init/sync, config load, plugin CRUD, container CRUD.
  - Output send normalization + input plugin normalization.
  - Plugin parameter/state updates, preset dirtying, view model refresh.
- Wire to `PluginWindowRouter` and `PluginContainerWindowManager`.

## Phase 4: Channels + Layout Sizing
- Create `MainChannelCoordinator`:
  - Build channel view models, update channel config, add/remove/rename.
  - Apply config to view models.
  - Compute dynamic window sizing via `MainLayoutMetrics` and plugin visibility.

## Phase 5: Engine + Metering + MIDI + Presets
- Create `MainAudioEngineCoordinator`:
  - Engine lifecycle, device selection, quality restarts, channel input/state apply.
  - Diagnostics + meter updates (delegating container meter updates to manager).
- Create `MainMidiCoordinator` (midi init/learn/bindings + apply binding).
- Create `MainPresetCoordinator` (preset apply/save/delete + active preset sync).

## Phase 6: Integration + Cleanup
- Update `MainViewModel` to compose coordinators and delegate all responsibilities.
- Remove duplicated layout constants and redundant helpers.
- Ensure disposal order and container window cleanup.
- Remove unused usings and dead methods.

## Verification
- Build/run on Windows and validate:
  - startup + device selection + engine restart
  - plugin add/remove/containers + preset flows
  - meters + debug overlay + container windows
  - minimal/full view sizing
