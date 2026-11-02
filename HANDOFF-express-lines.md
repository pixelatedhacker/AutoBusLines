# Handoff: express lines

Branch `express-lines` on the fork `pixelatedhacker/AutoBusLines`, based on upstream
`amitkrishnanm/AutoBusLines` at `30059e1` (2.0.6). Feature commits: `1d57d15` (express lines) and
`302c21e` (bus model picker, see below). No pull request to upstream has been opened.

Companion work: branch `skip-stops-without-demand` on `pixelatedhacker/concurrent-bus-boarding`.
Express buses share kerbside stops with local buses and queue behind them; Concurrent Bus Boarding
lets several buses board at once, and that branch lets a bus with no demand at a stop drive on.

## Status

- Built on Windows with the CS2 modding toolchain (2026-11-01): `tsc --noEmit` passes, and the
  Release `dotnet build` plus webpack succeed and deploy to the local `Mods\AutoBusLines` folder.
- **Not tested in game yet**, neither express lines nor the bus model picker.
- Both are off by default (Express Lines off, both bus models "Any bus"), so plans and lines are
  unchanged unless the player turns them on.

## What it does

With **Express Lines** on (Settings tab, "Route & Line Sizing"), after `PlanTours` returns, every
local tour of at least 4 km (`CalculateTourLength`) and at least 8 stops gets an express tour that:

- keeps the local tour's stops in the same order, so it places no new stops;
- calls at the first stop, then whenever straight-line distance since the last call reaches
  **Express Stop Spacing** (500–3000 m, default 1000 m), or at a transfer stop once 40% of that
  spacing has passed (a station bay, or a stop on more than one local tour);
- drops a last call within half a spacing of the first;
- is discarded if it keeps fewer than 4 stops or more than 60% of the local tour's stops.

Express tours are appended after the local ones (`localTourCount` in `ExecuteGeneration`), named
"Express N", flagged `IsExpress` in `PlannedRoute`, and badged in the planner. They take no part in
`ctx.Served` or coverage, and they are built by the same `CreateBusLine` as local lines.

## Bus model picker (`302c21e`)

Settings gain **Local Line Buses** and **Express Line Buses** (the express row shows only while
Express Lines is on). Each is a card grid of the unlocked bus prefabs the game offers a bus line,
the same list as a line's own "Select vehicles" section, plus **Any bus** (empty string, the default
and the previous behaviour). Choices are stored by prefab name in `Setting.LocalBusModel` /
`ExpressBusModel`.

- `BusLineGenerator.VehicleModels.cs` lists models with the game's `TransportVehicleSelectData`
  (`ListVehicles` with the bus line prefab's `TransportType` and `SizeClass`,
  `PublicTransportPurpose.TransportLine`, `ignoreTheme: true`). Its query excludes `Locked` prefabs.
- `Routes.InitializeSystem` clears the `VehicleModel` buffer of every `Created` route and picks a
  random vehicle, so writing a model in `CreateBusLine` would be lost. Instead, `CreateBusLine`
  queues the route (`QueueVehicleModel`), and `ApplyPendingVehicleModels` at the start of
  `OnUpdate` writes `{ m_PrimaryPrefab = model }` once the route has lost `Created`. Missing or
  deleted routes are dropped from the queue.
- Whether a tour is express is carried beside its colour (`_plannedTourIsExpress`) through both the
  plan and the direct build paths.
- A saved model that is no longer available (asset pack removed, prefab locked) logs a warning and
  falls back to the game's random choice.
- Lines that already exist are not changed.

## What changed

| File | Change |
| --- | --- |
| `BusLineGenerator.Planning.cs` | `PlanExpressTours` and four `EXPRESS_*` constants. |
| `BusLineGenerator.cs` | Appends express tours after `PlanTours` when enabled; plan entries get `IsExpress` and the "Express N" name. |
| `TransitPlan.cs` | `PlannedRoute.IsExpress` (`isExpress` in JSON). |
| `Setting.cs` | Hidden `EnableExpressLines` (false) and `ExpressStopSpacing` (1000), with defaults. |
| `AutoBusLinesUISystem.cs` | `enableExpressLines` / `expressStopSpacing` bindings, `setEnableExpressLines` / `setExpressStopSpacing` triggers (spacing clamped 500–3000), refresh on settings load. |
| `ui/src/mods/PlanPanel.tsx`, `PlanPanel.module.scss` | Checkbox, spacing slider with presets, EXPRESS badge on route rows. |
| `README.md` | Feature section, including the routing and stop-queueing caveats. |
| `BusLineGenerator.VehicleModels.cs` (new) | `ListBusModels`, `QueueVehicleModel`, `ApplyPendingVehicleModels` (`302c21e`). |
| `BusLineGenerator.cs`, `BusLineGenerator.RouteCreation.cs` | Vehicle model selection set up in `OnCreate`, applied each `OnUpdate`; `CreateBusLine` takes `isExpress` and queues the model. |
| `Setting.cs`, `AutoBusLinesUISystem.cs`, `ui/src/mods/PlanPanel.tsx` | `LocalBusModel` / `ExpressBusModel`; `busModelOptions` binding (name, localized label, thumbnail) and set triggers; the two card grids. |

## Known gaps and open questions

1. **Routing between express calls is the game's choice.** `CreateBusLine` only creates connected
   (stop) waypoints, and `RoutePathSystem` pathfinds between consecutive ones. With calls 1 km+
   apart the bus may leave the local loop's roads. The fix is to add plain route waypoints at
   junctions along the local loop between calls. That needs a non-stop waypoint archetype from
   `RouteData` (believed to be `m_WaypointArchetype`; unverified) and care with the
   contiguous `Waypoint` / `Segment` indexing that `CreateBusLine` already documents as a crash
   risk. Not attempted.
2. **Distances are straight-line** between consecutive stops, not road distance, so spacing is
   approximate on winding roads.
3. **Direct generation** (plan mode off) also gets express lines, built in order after the local
   ones; nothing there marks them as express in game, since `CreateBusLine` sets only number and
   colour.
4. **Colours** come from `ComputeTourColors` over all tours, so an express line does not share its
   local line's colour. Reusing the parent's colour, or a fixed express colour, may read better.
5. **Vehicle counts.** Express lines get the prefab's default vehicle interval. Whether that is
   sensible for long express loops is untested.

## How to finish

1. Windows with the CS2 modding toolchain: `cd ui && npm install && npm run build`, then
   `dotnet build` from the repository root (see the README). Already done once; see "Local setup
   used" for the workarounds it needed.
2. In game: Settings → turn on Express Lines → Calculate in Lines Planner on a city with long loops.
   Check express lines appear with the badge, stop counts look right, and toggling works.
3. Build Selected; watch an express bus. Confirm it skips the local-only stops, and note where its
   route leaves the local loop (gap 1).
4. Run with Concurrent Bus Boarding installed, with and without its "Let buses pass stops with no
   demand" option, and compare express travel times.
5. Bus models: check the two grids fill with the city's unlocked buses (thumbnails and localized
   names, asset-pack buses included). Pick a model, build lines from a plan and with plan mode off,
   and confirm each new line's "Select vehicles" panel shows the pick and that model is dispatched.
   Then set "Any bus" and confirm lines get a random model as before.
6. If it holds up, open a pull request to `amitkrishnanm/AutoBusLines`.

## Local setup used

- The fork clones on the Mac had `core.hooksPath` set to `.git/hooks` per repository to bypass a
  global commit hook; this is local config only and is not part of the repository.
- The Windows build (2026-11-01) needed the `CSII_*` user environment variables loaded into the
  shell (webpack fails with "CSII_USERDATAPATH not set" otherwise) and
  `DOTNET_ROLL_FORWARD=Major`, because the toolchain's ModPostProcessor targets .NET 6 and only the
  .NET 8 SDK was installed.
- In the game's playset, disable the subscribed Paradox Mods copy (161531) so the local build in
  `Mods\AutoBusLines` is the one that loads.
