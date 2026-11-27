# AGENT_MEMORY — DeskCiv

Hand-off record shared between agents. Newest entries first. Do not overwrite prior entries or record secrets.

## 2026-11-27 — Firelight pool on the ground round the camp fire

- **Changed:** `WorldView` has a new `_campGlow` multimesh: one `MeshKit.Glow()` additive disc, about 8 m radius, flickering, 0.12 m above the camp fire. It is shown only after dark (`_nightGlow > 0.15`) and hidden when the fire goes out.
- **Verified:** `dotnet build` succeeds.
- **Not verified on screen.** Four Godot shots at year 3, hour 23 did not frame the fire:
  - `--focus` on the camp
  - `--view`
  - `--watch-dig`
  - the default wild camera

  The fixed camera azimuth looks from +Z, so the cave rock stands between the camera and the camp, which is 6 m in front of the mouth toward -Z.
- **Open issue found:** at night with `--hour` the scene stays fairly bright ("moonlit"). Viewers likely see the wild camp from behind the rock. The camera should look at the cave mouth from its open side (azimuth by `CaveYaw`) when it frames the camp.

## 2026-11-27 — Wet foreshore at low tide

- **Changed:** `WorldView` terrain shader. Ground below the high-water line is darkened toward wet mud, fading out between 0.08 and 0.3 m. Drained polders are excluded (`reclaim > 0`). Because the water plane covers this band at high tide, the dark mud strip widens and narrows with the tide.
- **Verified:** `dotnet build` succeeds. Godot shots, seed 1000, year 3, estuary mouth (230,120): at hour 12.4 (high water) only a thin edge shows; at hour 17.2 (low water) a clear dark-brown mud strip runs along both banks.
- **Limitation:** the terrain shader has specular disabled, so the mud is darker but not glossy.

## 2026-11-27 — Fable review 11 fixes

- **Changed:**
  - `WorldView`: the camp-fire `OmniLight3D` is visible only after dark (`_nightGlow > 0.15`, taken from `SetNightGlow`), so by day there is no extra light pass in the compatibility renderer.
  - `Simulation`:
    - New `TrainArrivals` counter, incremented once per group that actually spawns at the station. SimTests asserts on it instead of the 64-entry event ring.
    - The no-hunter check runs on every evaluation in the strike window; it no longer relies on `Tick == HuntStrike`.
    - When the road link fails, `DikeOnField` retries with the next riverside field, up to 3 fields.
    - The factory gate and station entrance tiles must be Free, Road or PlannedRoad. New `LinkGate` plans a road on the gate tile and a path to the nearest road, so the mill and station get a street and their gates are never built over.
  - `Main`: the tide uses continuous UTC hours mod 1242 h, so there is no jump at New Year or a clock change.
  - `Wildlife`: boat lanterns ride the tide.
- **Verified:**
  - SimTests: ALL PASSED. Mill shifts rose on 3 of 4 full seeds (1000: 3178→3464, 16838: 1051→2024, 72271: 3855→4522). All seeds keep every landmark and have train arrivals.
  - Godot shot, seed 1000, year 320, focus 146,200: a paved street runs past the factory front.
- **Not done:** Fable's other suggestions: elders settling on the step for longer (an earlier try perturbed the seeds), a wet mudflat band in the ground shader, and a ground glow disc for the fire. The camp-light GPU cost was not measured.

## 2026-11-27 — Update clip 18 published

- **Done:** 0:40, 3.4 MB, four scenes:
  - the estuary mouth at high water (hour 12.4) and at low water (hour 18.4), 2,500 BC
  - the factory-gate area at AD 1962
  - the railway station at AD 1962

  Published publicly (not made for kids) as "DeskCiv update 18 – tides on the estuary, reachable mills and stations, old folk on doorsteps": https://youtu.be/Ubehr7gASa8. The description covers the tides (and says they are subtle), the doorstep elders, the mill, station and quay fixes, and the new test guards.
- **Limitations:**
  - The tide contrast is small, and the low-water scene is in dusk light.
  - The factory scene is framed on the church and market, with the factory at the top edge.
  - No train arrival is caught on camera.
  - The doorstep elders are not visible in any scene.

## 2026-11-27 — Railway station entrance on dry ground

- **Cause:** on seed 16838, using the test night pattern, the station's entrance tile was at height -0.41, which is not walkable. `ArriveMigrants` then fell back to arrivals at the estuary edge, so nobody ever came by train (`bytrain=0`).
- **Changed:**
  - `Simulation.PlaceLandmarks`, station: the candidate's entrance tile (z ± 2) must be in the grid, `Map.Walkable`, and on the town's side of the river.
  - `SimTests` full runs now require at least one "Newcomers step off the train" event.
- **Verified:** SimTests ALL PASSED. Seed 16838 now has `bytrain=8` (its rail moved to 58,190). The other full seeds are unchanged.

## 2026-11-27 — Mill reachable from town; quay finds a strip; tests guard both

- **Cause:** on seed 40595 the factory was placed across the river from the town (gate at z 43, river at about z 87, town centre at z 178). Its gate was also on near-sea-level ground that is not walkable. With no way over, mill hands headed there but their paths failed, so they stood still: `ShiftsWorked` stayed at 0. The quay search (shift ≤ 20, back ≤ 2) also found no free strip, so no quay was ever built.
- **Changed:**
  - `Simulation.PlaceLandmarks`, factory: the candidate's gate point must be `Map.Walkable` and on the same side of the river as the town centre.
  - Quay search widened to shift ≤ 40, back ≤ 4.
  - `Wander`: the walk to the factory gate uses a 4000-expansion path search; other wandering keeps 300.
  - `SimTests` full runs now require all six landmark kinds and at least one mill shift.
- **Verified:**
  - SimTests: ALL PASSED. All four full seeds now show landmarks 0+5+1+3+4+2. Seed 40595 went from 0 shifts to 2799.
- **Limitation:** existing saves keep a mill already placed across the river.

## 2026-11-27 — Old folk on their doorsteps

- **Changed:**
  - `Simulation.Wander`: by day (not in the wild years), a villager aged 60+ with a home goes to their own door point, radius 0.8 m, 60% of the time (Cosmetic draw). The Wander timer is unchanged.
  - `SimHost`: an idle elder within about 1.6 m of their door faces the street, back to the house.
- **Verified:**
  - SimTests: ALL PASSED.
  - Debug count, seed 1000, year 150, 3000 daytime ticks: 28% of elder samples stand on their doorstep.
  - A Godot shot at year 150 (hour 15, `--look=Hut`) rendered fine, but individual elders cannot be picked out at that distance.
- **Note:** a first version kept elders on the step for 20–40 s. It shifted the RNG path enough that seeds 16838 and 40595 lost a landmark, so it was dropped. With the normal timer, seed 40595 still ends with `shifts=0` and one landmark type missing. This is the known chaotic sensitivity of that seed, and no check covers it.
- **Tide follow-up:** a shot at year 100, hour 17.5 (tide about -0.27 m) shows the fishing boat sitting in the water, not floating above it.

## 2026-11-27 — Tide on the estuary

- **Changed:**
  - `WorldView`: the water shader has a `tide` uniform that raises the water plane, and the shore foam depth now uses `tide - ground`. `SetTide(hours)` sets the level to -0.09 + 0.21·cos(2π·h/12.42): low water is -0.30 m, high water +0.12 m, which stays below the 0.25 m walkable/farmland line together with the existing ±0.1 swell.
  - `Main.UpdateSun` calls `SetTide` with the hour plus DayOfYear·24 on the real clock, so the tides drift about 50 min a day. With `--hour` the day offset is 0, which puts high water at 0:00/12:25 and low water at 6:13/18:38.
  - `Wildlife.Tide`: fishing boats, the trading ship and skaters ride on the tide level.
- **Verified:**
  - `dotnet build` succeeds.
  - Godot shots, seed 1000, year 3, hours 12.4 and 18.6, at the river (60,150) and the estuary mouth (230,120): low water shows a wider mud and sand strip along the near shore, with no water-plane artefacts.
  - A fishing-camp shot at year 34.4, hour 18.4 had no boat in view, so boats riding the tide were not checked on screen.
- **Limitations:** the change is small on the steep river banks. The sim (floods, dikes) does not know about the tide; it is view-only.

## 2026-11-27 — Update clip 17 published

- **Done:** 0:56, 4.8 MB. Three scenes:
  - the camp fire with smoke at dusk, year 3
  - a red deer hunt with the kill fading as the meat is carried off; cut from a 250 s `--watch-hunt` recording at 218–244 s
  - the first storehouse rising in scaffolding at year 6

  Published publicly (not made for kids) as "DeskCiv update 17 – camp fire smoke, the hunt's kill carried off, first storehouse rises": https://youtu.be/yzOTI8jAixQ. The description lists the camp smoke and glow, the band facing its fire, the hunt fixes, the night hunts, the storehouse rise, and the cave and dike fixes.
- **Limitations:**
  - The dusk scene is shot from behind the cave rock.
  - In the storehouse scene only the first ~20% of the rise is visible.

## 2026-11-27 — DikeOnField keeps the field when the dike cannot be reached

- **Changed:** `Simulation.DikeOnField` now looks for the road link to the dike's door before committing the change. With no link, the building goes back to the field it was (position, door, growth and grain kept) and nothing is logged. Workers are unassigned and the roads planned only once the link exists.
- **Verified:**
  - `dotnet build` succeeds.
  - SimTests: ALL PASSED, with the same era-end numbers as before. The no-link branch did not come up in these seeds, so it has not been exercised.

## 2026-11-27 — Hunted deer is carried off

- **Changed:** `Wildlife`. The fallen deer shrinks away between 9 and 12 s after the strike, as the hunters carry the meat off. When the hunt ends, that deer is moved back to its herd home instead of getting up at the kill site.
- **Verified:**
  - `dotnet build` succeeds.
  - `--watch-hunt` shot, seed 1000 at 250 s: same framing as the 238 s shot, and the clearing is empty with the won-hunt headline showing.
  - The fade itself was not recorded on video.

## 2026-11-27 — Camp fire smoke and glow

- **Changed:** `WorldView`. While the camp fire burns, it sends up 6 wood-smoke puffs (scale 1.5, taken from the shared smoke budget). A warm flickering `OmniLight3D` (range 10 m, no shadows) sits 1.1 m above the fire and is hidden when the fire goes out.
- **Verified:**
  - `dotnet build` succeeds.
  - Godot shots, seed 1000, year 3, focused on the camp at 109.7,179.6: at hour 10 faint smoke puffs rise above the cave rock; at hour 22 the flames show with a warm patch of ground around them.
  - The view was only checked in these shots. CPU and GPU cost were not measured, though a single unshadowed light should be negligible.
- **Limitation:** both camera angles look from behind the cave rock, so the fire is partly hidden. The hour-22 shot does not look strongly moonlit.

## 2026-11-27 — Fable review 10: wild-phase fixes, storehouse rise

- **Changed:**
  - `Simulation`:
    - `UnderCave` now uses the 3x3 rock tiles, so the corner tiles are covered too.
    - `EndWild` blocks the cave mouth and sleep tiles.
    - A won hunt with no hunter present is turned into a failed one.
    - Hunts may start at night once a year has passed since the last one.
    - `DikeOnField` no longer takes a field with ripe grain, and it keeps at least 4 + pop/25 fields.
    - The state hash now includes `HuntWon`, `NextHunt`, `HuntsWon`, `HuntX/Z` and `CaveX/Z`.
  - `Wildlife`: the deer that will fall grazes 8 m out from the start of the hunt, so it no longer jumps at the strike. It walks home when the hunt ends.
  - `SimHost`: at night the wild band faces its fire.
  - `WorldView`: when the band leaves its wild camp, the first storehouse rises over about 90 s inside scaffolding (view only, `StoreRiseYears` 0.05).
- **Verified:**
  - `dotnet build` succeeds.
  - SimTests: ALL PASSED. Always-night seeds now get 2–3 won hunts out of 5.
  - Godot shot, seed 1000, start year 5.97, shot at 100 s: the scaffolded storehouse site is shown with the street-founding headline.
  - `--watch-hunt` shot at 238 s: the deer lies 8 m out and the hunters carry meat home.
- **Not done:** Fable's other suggestions: smoke and glow over the camp fire, and hiding the carcass once it is carried off. `DikeOnField` still converts the field even when the road link to it fails.

## 2026-11-27 — Update clip 16 published

- **Done:** 0:28, 2.4 MB. Two scenes:
  - the estuary with fishing boats at 2,499 BC
  - the town centre at AD 1224

  Published publicly (not made for kids) as "DeskCiv update 16 – gulls trail the boats, children play together": https://youtu.be/_JH_AX42yF8. The description lists the gulls trailing the boats, children's play patches and the cave-rock fix.
- **Limitation:** at these camera distances the gulls are tiny and the children cannot be told apart from adults. The description says the changes are subtle.
- **Next clip due:** about 05:30. Fable #10 is due about 03:20.
## 2026-11-27 — Neighbourhood children play together by day

- **Changed:** `Sim/Simulation.cs` (`Wander`). By day, 70% of the time, children aged 4–13 with a home wander within 5 m of a shared play patch: the centre of their home's 40 m block, if it is open ground or road. Otherwise they stay round their home as before. Only the `Cosmetic` RNG is used, and gatherings and market day still take priority.
- **Verified:**
  - SimTests ALL PASSED (determinism and save/load included).
  - Seed 1000 at year 80: 7 children; by day 2 were at their play patch, by night 0. The effect is mild while the village is small.
  - Godot build: 0 errors. Not checked on screen.
## 2026-11-27 — Gulls trail the fishing boats by day

- **Changed:** `game/src/Wildlife.cs` (`StepGulls`). By day, every third gull eases its circling centre onto one fishing boat. It wheels around it at 40% of its usual radius and 60% of its height. At night, or with no boats out, it drifts back to its own haunt, which is kept in the agent's X/Z.
- **Verified:** with a temporary debug print (removed), seed 1000 at year 100 by day: 4 boats out, and the followers' circling centre stayed a mean 3.5 m from their boat over 80 s. Build: 0 errors.
## 2026-11-27 — Cave rock stays blocked when a polder drains

- **Changed:** `Sim/Simulation.cs`. New `UnderCave(tile)`: within 7 m of the rock centre, which is 5 m behind the mouth. `FinishDraining` no longer frees those Blocked tiles, so nothing can be built inside the cave's rock. This was Fable #9's last open point.
- **Not covered:** a polder ring placed across the rock could still pave it as a dike road. This was judged rare and left alone.
- **Verified:** SimTests ALL PASSED. Godot build: 0 errors.
## 2026-11-26 — Update clip 15 published

- **Done:** 0:46, 3.9 MB. Three scenes:
  - a won hunt with the deer lying in view (fresh seed 1000, from 3:38 of the run)
  - the fire at the cave mouth by night
  - the fire still burning after the "stone axes" news (`--start-year=6.05`)

  Published publicly (not made for kids) as "DeskCiv update 15 – the kill stays where it fell, and the fire moves to the cave": https://youtu.be/-3IsPT41sYw. The description lists the fallen deer, failed-hunt news, quiet nights, the fire at the cave, the cave-mouth path fix, and the gathering and fire at the end of the wild years.
- **Next clip due:** about 02:40. The next Fable review is due about 03:20.
## 2026-11-26 — Camp fire burns on until the first hut stands

- **Changed:** `game/src/WorldView.cs` (`UpdateWildCamp`). After the wild years the hearth keeps burning, still in front of the cave, until a hut is finished. This applies only in era 0 and only with a cave, so older saves are unaffected. Before, it went out the moment the wild years ended.
- **Verified:** Godot shot (seed 1000, `--start-year=6.05`) shows the "knap stone axes" news with the fire still burning at the cave mouth. Build: 0 errors.
- **Still not done:** a smooth rise for the storehouse when the wild years end.
## 2026-11-26 — The hunted deer lies where it fell

- **Changed:** `game/src/Wildlife.cs`.
  - New `WriteLying`: yaw, then a 90-degree roll, with the legs still.
  - When a hunt is won, deer 0 is placed 8 m beyond the hunt point, toward the herd, and lies there until the hunt ends. Before, it was scaled to nothing and vanished.
- **Verified:**
  - A temporary debug print (removed) showed the old fallen deer 33 m from the hunters, out of the hunt shot.
  - After the change, a Godot shot (seed 1000, fresh, 236 s, `--watch-hunt`) shows the deer on its side in frame, with the hunters carrying meat away.
  - Build: 0 errors.
## 2026-11-26 — Fable review #9 fixes to the wild years

- **Review:** Fable review #9 (read-only) of commit 16429ce.
- **Applied (`Sim/Simulation.cs`, `WorldView.cs`, `Main.cs`, `tests/SimTests/Program.cs`):**
  - **Cave path:** `PlaceCave` leaves the mouth tile and the sleeping spot open, so people path into the cave instead of walking straight and stopping at walls.
  - **Calm nights:** no hunt starts at night, and night foragers only dig or gather shellfish. The hunt camera shot is skipped outside 06:00–20:00.
  - **Fire by the cave:** new `CampX`/`CampZ`. Once the cave can be entered (`CaveDug` ≥ 0.3) the fire and the band's wandering centre move 6 m in front of the cave mouth; before that they are at the cache.
  - **Failed hunts** now log "The deer scatter before the hunters can close in", for 1 in 4 of them.
  - **`EndWild`:** clears `HuntWon`, so hunters still on their way bring no meat from a cancelled hunt. A gathering at the fire marks the first stone axes.
  - **Tests:** the "won hunt" check is skipped for the always-night pattern (nobody hunts by night). A new check: that pattern loses nobody living on shellfish alone.
- **Not done:**
  - showing the fallen deer lying down (no tilt in the herd writer)
  - walking the herd in instead of placing it
  - a rising storehouse at `EndWild`
  - a saved "counted" flag for `HuntsWon`
  - Fable also noted that a cave inside a later polder could get its rock tiles unblocked by `FinishDraining`. This is unchecked.
- **Verified:**
  - SimTests ALL PASSED. Always-night seeds: band 28–30, cave dug, 0 deaths, shellfish only.
  - Godot shots (seed 1000, 9,835 BC) at 11:00 and 22:00: the fire burns in front of the cave mouth.
## 2026-11-26 — Update clip 14 published

- **Done:** 1:04, 5.5 MB. Four scenes:
  - a bare-handed hunt (fresh seed 1000, from 3:28 of the run: hunters ring the deer, strike, the herd bolts, they carry the meat home)
  - the hand-dug cave
  - the camp fire with the band around it
  - the first huts at 9,427 BC (`--look=Hut`)

  Published publicly (not made for kids) as "DeskCiv update 14 – the Stone Age now opens with hunters and a hand-dug cave": https://youtu.be/9-_3Jyp3cwU. The description lists the wild years, the hunt, the cave, camp life, the move to huts, dikes on riverside fields, and herds huddling at night.
- **Next clip due:** about 23:40. Fable #9 is due about 21:15.
## 2026-11-26 — Farm animals huddle at night

- **Changed:** `game/src/Wildlife.cs` (`StepHerd`). At night, sheep, cows and chickens pick spots within a quarter of their usual roam around their resting place, and wait three times longer between moves. Deer are unchanged.
- **Tried and dropped:** drawing the herds toward the village at night. The targets land on field and house tiles, so they were refused and the animals stayed where they were.
- **Verified:** with a temporary debug print (removed after), seed 1000, year 100. Sheep mean distance from their resting place was 4.6 m at 23:00 and 8.0 m at 12:00. Build: 0 errors.
## 2026-11-26 — Old Stone Age starts as a wild hunter-gatherer band (user request)

- **Why:** The user asked that the Stone Age begin with hunting, cave digging and similar activities, with no tools, before the band starts felling trees and building huts.
- **Sim (`Sim/Simulation.cs`, `Sim/Model.cs`):**
  - `Wild` covers the first `WildYears` = 6 sim years (about 3 h real time, 10,000 → about 9,570 BC).
  - While wild, `Plan()` builds nothing and no street is laid out. The founding store stays as the band's food cache.
  - Adults choose `Act.Forage` (new, appended) via `TryForage`, with bare hands:
    - hunting (`A=0`)
    - picking berries (`A=1`)
    - gathering shellfish on the shore (`A=2`)
    - digging the cave (`A=3`)
  - **Hunts:** `StartHunt` runs every 8–15 min, in the most open of 12 spots 70–120 m out. Up to 5 men join; they ring the spot, wait until `HuntStrike`, then carry home 4 `Res.Meat` each when the hunt is won (60%).
  - **Cave:** `PlaceCave` uses no RNG. It puts the mouth at the foot of the steepest rise 24–74 m from camp and marks the rock tiles as `Blocked`. 45 digging stints open it (`CaveDug`).
  - **Living in the cave:** the band sleeps there (3/5 of people at night). A cave at least half dug keeps them warm in winter. Children are born there, up to 30 people.
  - **End of the wild years:** `EndWild` logs "The band learns to knap stone axes…" and seeds the first street; the normal village then follows.
  - **Saved data:** new resource `Res.Meat`, eaten after bread. Save version is now 18 and stores the wild, cave and hunt state. Older saves load with no meat column and `Wild=false`.
- **Bank fix found by the tests:** with the RNG path changed, seed 72271's bank had no room for a second dike, so it never got a tide gate.
  - New `DikeOnField()`: when no bank site exists, a dike takes the water-side row of a riverside field within 26 m of the water.
  - Seed 72271 now holds 38 surges, up from 21.
  - Tried and reverted: letting the gate replace the only dike (lost the dike), and giving the path search its own budget (no effect).
- **View:**
  - `MeshKit.Cave`/`CaveMouth`: a rock outcrop with an opening that grows as it is dug. It stays as a relic after the wild years.
  - `MeshKit.Hearth` and a 3-flame camp fire at the cache. The storehouse is hidden while wild.
  - Venison haunch carry load.
  - Tools are hidden while wild.
  - The deer group goes to the hunt, bolts at the strike, and one deer drops if the hunt is won (`Wildlife.SetHunt`).
  - Hunters face the deer.
  - Camera: hunt shots only in the last 90 s before the strike (`HuntStalk`), plus a cave shot; `--watch-hunt` holds on these.
- **Verified:**
  - SimTests ALL PASSED: determinism, save/load, 12 seeds and 4 full runs.
  - New wild-years checks: no buildings besides the store, cave dug, at least one won hunt with meat. Per seed: band 26–30, 0 deaths, 6–12 won hunts.
  - The year-50 bread check now runs to year 56, because the village starts 6 years later.
  - Godot shots (seed 1000):
    - camp fire with the band around it
    - the dug cave
    - 5 hunters ringing a stag before the strike, then carrying meat home with the deer gone
- **Limitations:**
  - The cave and hearth are procedural low-poly shapes; a realistic image-generated cave model is a backlog item.
  - Seed 40595 now ends without a boiler house (landmarks `0+5+1+3+4`, heated 0/62). This is RNG-path variance and is not covered by a check.
## 2026-11-26 — Update clip 13 published

- **Done:** 0:24, 2.0 MB. Two scenes:
  - the rainbow over the estuary (`--test-rainbow`, 212 BC)
  - a storm-dark rainy town (`--weather=storm`, AD 1601), re-recorded with the honest caption "heavy thunderstorms now roll in over the town" because no lightning strike was caught on film

  Published publicly (not made for kids) as "DeskCiv update 13 – rainbows, thunderstorms, a church bell and a train whistle": https://youtu.be/Qv6yd773Brk. The description lists the rainbow, storms with lightning and thunder, the bell and whistle, the opening shot, night dimming, the true harvest share and the boat pace.
- **Limitation:** lightning, thunder, bell and whistle are described but not demonstrated in the clip.
- **Next clip due:** about 20:45. Fable #9 is due about 21:15.
## 2026-11-26 — Lightning fixed to show: brighter, longer, survives UpdateSun

- **Changed:** `game/src/Main.cs`.
  - The lightning amount is kept in `_flash`. `ApplyFlash` adds it on top of the light `UpdateSun` sets: ambient +1.1, sun +1.2, and the sky whitened up to 70%. It runs both from `UpdateLightning` (every frame) and right after `UpdateSun`, which previously reset the ambient light in the same frame and erased the flash.
  - Each strike is now a 0.12 s stroke, a second stroke, then a 0.43 s fade, instead of two 80 ms blips.
  - `--weather=storm` strikes in every 13 s slot (normally 1 in 3).
- **Verified:**
  - `dotnet build`: 0 errors. A debug log confirmed strikes are scheduled in storm rain (for example at 9.0 s and 14.3 s).
  - A temporary always-on flash brightened a still shot strongly, so the mechanism works; the strength was then lowered.
  - **Not verified:** a natural strike has not been caught in a recording or still shot (two timed shots and three recordings missed it). Thunder has not been heard.
## 2026-11-26 — Thunderstorms: lightning flashes and thunder

- **Changed:**
  - `game/src/Main.cs`:
    - `UpdateLightning` runs every frame. When rain is above 0.9, each 13 s slot of wall time has a 1-in-3 chance (hashed from the slot, not the sim RNG) of a strike at a 0-5 s offset.
    - The scene's ambient light jumps by +1.4 for two 80 ms flashes 150 ms apart, then returns to the value `UpdateSun` set.
    - Thunder follows 1.5-3.9 s later.
    - `--weather=storm` gives cover 1 and rain 1; `--weather=rain` now gives rain 0.8 (was 1), so plain rain has no lightning.
  - `game/src/Soundscape.cs`: `Thunder()` on its own player at -8 dB, with slight pitch variation. `MakeThunder` builds 4 s of doubly low-passed noise with a sharp crack and a swelling, fading roll.
- **Verified:** `dotnet build`: 0 errors.
- **Not verified:** not seen or heard. The flash is too brief for a single test screenshot, and the thunder is untested by ear.
## 2026-11-26 — Sound cues: church bell and steam whistle

- **Changed:**
  - `game/src/Soundscape.cs`:
    - A cue player on the Ambient bus at -14 dB.
    - Two synthesised clips:
      - `MakeBell`: three strokes 1.8 s apart. A 392 Hz fundamental with inharmonic partials (0.5, 1, 1.19, 1.5, 2, 2.52, 3) and exponential decays.
      - `MakeWhistle`: about 1.6 s of C5 and E5 plus an octave, with a slight vibrato and breath noise.
    - Public `Bell()` (skipped at night) and `Whistle()`. Neither interrupts another cue, and both are silent when sound is muted, suppressed or yielding.
  - `game/src/Main.cs`:
    - The bell strikes each real hour from 08:00 to 20:00 once the village has a church.
    - It tolls when a wedding or a funeral headline appears.
    - The whistle sounds as the train leaves the tunnel (`TrainTick` crossing 60).
- **Verified:** `dotnet build`: 0 errors.
- **Not verified:** not listened to. The synthesised timbre and loudness are untested by ear, and the whistle and bell triggers were not exercised in a run.
## 2026-11-26 — A rainbow after a shower (view only)

- **Changed:** `game/src/Main.cs`.
  - New `UpdateRainbow` (called from `UpdateSun`). When the rain falls below 0.2 in daylight (`day > 0.6`), a rainbow shows for 120 s, easing in and out.
  - Mesh: `RainbowArc`, a 48-segment half ring, radius 55 m, band 4.5 m.
  - Shader: unshaded, additive, no depth write. Six hues from red outside to violet inside, soft-edged, 0.45 strength.
  - Placement: it stands 60 m beyond the focus and 22 m low, facing the camera squarely. Placing it 280 m out "behind" the scene, as first designed, left it hidden behind the hills or above the frame, because the wallpaper camera looks down steeply.
  - New `--test-rainbow` switch shows it at full strength.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, 212 BC, 15:00, cloudy, `--test-rainbow`: a clear multicoloured bow rises over the far bank, with its very top just at the frame edge.
- **Limitation:** it is placed relative to the camera, not the sun. Natural triggering (a shower ending in daylight) has not been seen in a real run.
## 2026-11-26 — Opening wide shot; smoke, rain and night sky follow the moonlit night

- **Changed:**
  - `game/src/Main.cs`:
    - **Opening shot:** on launch (not in `--shot` tests or with `--view`), the camera starts over the whole settlement at 1.3x the overview distance, azimuth 0, and eases into the first scene, which is held 20 ticks longer.
    - Rain streaks are dimmed by `0.35 + 0.65*daylight`.
    - The night sky and fog base colour are darker (0.17, 0.22, 0.35; was 0.24, 0.31, 0.46).
  - `game/src/WorldView.cs`: the unshaded chimney-smoke shader multiplies its tone by `0.35 + 0.65*daylight`, so smoke no longer glows pale against the dark night (Fable review #8 item c).
- **Verified:** `dotnet build`: 0 errors.
  - Not viewed on screen. The opening is skipped in shot tests by design, and the night smoke, rain and sky change was not rendered.
## 2026-11-26 — Fable review #8 and its quick fixes

- **Review #8 (Fable, read-only):**
  - **Bugs:**
    - `Harvested` used a reference yield and ignored fertility, drought, bumper years and floods.
    - The fleet sailed in convoy after the first night.
    - Unshaded smoke, rain and the sky ignore the darker night.
    - The Yule twinkle's float clock loses precision after days; static light transforms were set every frame; arrays were allocated every frame in `AnimateFestivities`, `AnimateYuleLights` and `StepLights`.
    - The fir position is not checked against buildings.
  - **Ideas:**
    - a storm with lightning and thunder, then a rainbow (top pick; design given)
    - an opening establishing shot
    - sound cues (church bell, train whistle, thunder)
    - gulls and herds going home at dusk
    - quiet-spell vignettes
- **Changed now:**
  - `SimHost`: per-field ripe-yield tracking (`_ripeYield`, reset when a field is not ripe); `Harvested = 1 - Yield/peak`.
  - `Wildlife`:
    - Boats sail at their own pace (1.9-2.7 m/s), so the fleet spreads out after mooring.
    - `StepLights` no longer allocates.
  - `WorldView`:
    - The Yule clock wraps at 10 min; light positions are set only when the fir moves, with colours each frame.
    - Bulb and firework palettes are static.
- **Verified:**
  - `dotnet build`: 0 errors.
  - Seed 1000, 198 BC autumn: only 1-2 stooks after a fresh start mid-harvest, because the peak is first seen at launch. In a long run it is captured as the field ripens.
- **Open from #8:** darken the smoke, rain and night sky; check the fir position against buildings; storm and rainbow; opening shot; sound cues; dusk wildlife; vignettes.
## 2026-11-26 — Update clip 12 published

- **Done:** 3 scenes recorded (free RAM about 14.6 GB): harvest stooks (199 BC), the December fir (`--test-december`, AD 1796), and a moonlit night with moored boats (211 BC). 0:32, 2.7 MB. Published publicly (not made for kids) as "DeskCiv update 12 – harvest stooks, a December fir and moonlit nights": https://youtu.be/DvHk7WDdX7M. The description lists the stooks, the December fir, darker moonlit nights with lanterns and lamps, and the traffic fixes.
- **Limitation:** the stooks and the fir are small in frame.
- **Next clip due:** about 17:30. Fable #8 is due about 16:00.
## 2026-11-26 — Harvest home: stooks rise as the fields are cut (view only)

- **Changed:**
  - `SimHost.cs`: `BuildingView.Harvested` is the share of a ripe field already cut. It is estimated as `1 - Yield / (56 * (1 + 0.28*(era-1)) * 1.1)`, clamped to 0-1.
  - `MeshKit.Stook()`: four leaning sheaves with a cap, about 1.4 m.
  - `WorldView`:
    - On a ripe field, up to 9 stooks stand in a 3x3 grid (rotated with the field), one more for each ninth harvested, until winter.
    - The standing crop shortens as it is cut, by up to 85%.
    - A fully cut field turns to pale stubble.
- **Why proportional:** fields are rarely cut bare before the new year. A first version shown only on fully harvested fields never appeared in autumn shots.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, 199 BC late autumn, `--look=Field`: golden stooks stand on the part-cut fields with farmers working between them, and the remaining crop is lower.
## 2026-11-26 — Moonlit nights: night lighting a little darker

- **Why:** night test renders looked almost like day, so lanterns, lamps, fireworks and fire glow did not read. At night the sun light was still 0.52 and the ambient light 0.8.
- **Changed:** `game/src/Main.cs` `UpdateSun`.
  - Sun energy is now `0.36 + 0.72*day` (was `0.52 + 0.56*day`): noon is unchanged (1.08) and full night drops from 0.52 to 0.36.
  - Ambient energy loses a further `0.2*(1-day)`: 0.8 at noon as before, 0.6 at night.
- **Visible:** the whole wallpaper looks bluer and somewhat darker at night; the town stays readable. Daytime is unchanged.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1795, `--focus=200,170`: at 23:00 the scene is noticeably darker and bluer than at 12:00, with streets and houses still clear.
- **To revert:** restore the two constants (0.52/0.56 and drop the `-(1f-day)*0.2f` term) if night is too dark on the user's screens.
## 2026-11-26 — Boat lanterns and vehicle lamps at night (view only)

- **Changed:** `game/src/Wildlife.cs`.
  - New additive `_lights` MultiMesh (spark mesh, per-instance colour), shown only at night.
  - Each visible fishing boat has a warm lantern 2.6 m up.
  - Each cart or lorry has a pair of lamps at the nose: warm on carts, white on lorries.
  - Boats now remember their drawn position and heading (`Z`, `Yaw`); vehicles remember their lane position (`HomeX`, `HomeZ`).
- **Verified:** `dotnet build`: 0 errors. `--hour=23`:
  - 211 BC: small lantern points on the moored boats.
  - AD 1939: no lamps were noticeable. Few lorries run at night, and the test render does not look dark, so the additive points do not stand out.
- **Limitation:** the effect depends on how dark the real night scene is; not checked in a true night render.
## 2026-11-26 — Traffic avoids the trench and turns smoothly; a lit fir in December

- **Changed:**
  - `Wildlife.cs`:
    - New `DigTile`; vehicles never turn into the street tile being dug.
    - Vehicle yaw eases into the new direction (`LerpAngle`, rate 5/s), so they no longer snap at corners and the lane offset follows smoothly.
  - `WorldView.cs`:
    - `UpdateWaterMains` passes `s.DigTile` to the wildlife.
    - In December by the PC's clock (or with `ForceDecember`), from the Industrial era, a fir 1.3x the normal pine stands beside the church (+10, +6 m).
    - 24 coloured lights (red, gold, blue, green) spiral up it and twinkle (`AnimateYuleLights`, 0.8 m additive sparks).
  - `Main.cs`: new `--test-december` switch sets `ForceDecember`.
- **Verified:**
  - `dotnet build`: 0 errors.
  - Seed 1000, AD 1796 winter, 21:00, `--test-december`: the snow-dusted fir stands by the church. Lights were small in that shot, so the bulbs were enlarged from 0.45 to 0.8 m (not re-shot).
  - Traffic changes not viewed on screen.
## 2026-11-26 — Update clip 11 published

- **Done:** 2 scenes recorded (free RAM about 15.2 GB):
  - an era celebration with bonfire and fireworks (`--test-fireworks`, AD 1235, 21:00);
  - boats moored off the village at night (211 BC).

  0:24, 2.1 MB. Published publicly (not made for kids) as "DeskCiv update 11 – fireworks for a new era, boats home at night, geese overhead": https://youtu.be/Cf7KHLwaqcY. The description lists the celebrations, night moorings, thinner night traffic, geese and the per-snapshot fixes.
- **Limitations:** the geese are described but not in the clip (their pass did not fall in the recorded window). The camera wanders wide during the fireworks.
- **Next clip due:** about 15:00. Fable #8 is due about 16:00.
## 2026-11-26 — Geese skeins in spring and autumn (view only)

- **Changed:**
  - `game/src/Wildlife.cs`:
    - New `Season` property and an 11-bird `_geese` flock using the gull mesh at 3x scale.
    - `StepGeese`: in spring and autumn, every 70 s, a V-formation crosses the map in about 35 s at 32 m. It passes near the village (z offset varies per pass), flying west in spring and east in autumn with a small slant. The leader is in front and the arms trail 5 m back and 4 m out per rank, with a gentle bob.
    - The cycle is offset by 15 s so a skein crosses soon after start.
  - `game/src/WorldView.cs` `UpdateSeason`: passes `s.Season` to the wildlife.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, 206 BC autumn, `--focus=165,150 --distance=150`, 12 s in: a clear V of birds over the village. Two earlier shots missed it because the wildlife clock starts at 0 and the skein was 260 m away.
- **Limitation:** the geese use the white gull model and colour.
## 2026-11-26 — Night: boats moor off the village, streets empty (view only)

- **Changed:**
  - `game/src/Wildlife.cs`:
    - New `Night` setter (it calls `ApplyCounts` when it changes).
    - At night each fishing boat sails to a mooring off the village (`centerX + 10 + i*7`), drifts to the village side of the channel and lies still (`moving = 0`). By day it returns to its own lane and fishes as before. The frozen-river hiding still applies.
    - Traffic at night is 30% of the daytime count.
  - `game/src/WorldView.cs` `SetNightGlow`: sets `_wildlife.Night = amount > 0.4`.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, 211 BC, `--hour=23`: the three boats lie in a row close to the village bank. The test render did not look dark at 23:00, but the night flag took effect.
- **Not done:** boat lanterns and lorry headlights (Fable's design used an emissive quad).
## 2026-11-26 — Fireworks and bonfire at celebrations; per-snapshot view updates fixed

- **Changed:**
  - `Sim/Simulation.cs`:
    - New `FestiveUntil` and `FestiveKind` (1 a feast, 2 a new era). They are not saved or hashed and drive view effects only.
    - Set when a bumper-harvest feast actually gathers and at each era celebration.
    - `FireworksForTest()` backs the `--test-fireworks` switch: it starts an era celebration now and logs "The village celebrates (test)".
  - `SimHost.cs`: snapshot `Festive`, `FestiveX` and `FestiveZ` (the gathering point).
  - `MeshKit.Spark()`.
  - `WorldView`:
    - `AnimateFestivities` puts a 3-flame bonfire beside the gathering point.
    - For a new era from the Middle Ages, 5 rockets fire in turn on a 3.6 s cycle. Each climbs with a short trail to about 30 m and bursts into 28 coloured stars (golden-angle sphere) that slow, drop and fade, in a 5-colour palette. At most 140 additive spark instances, only during the celebration.
  - `Main.cs`: an era-celebration event is framed 16 m higher and from 2.6x distance.
- **Bug fixed (important):**
  - `UpdateWaterMains`, `UpdateRailway`, `UpdateMarket` and the festive state were called from the road-texture refresh, which runs only when the road network changes.
  - As a result, the trench could lag behind the diggers, the market stalls could stay up or not appear, and the train clock re-sync and festivities did not follow the sim.
  - They now run on every new snapshot, after `UpdateSeason`.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1235, `--test-fireworks`:
  - At 21:00 coloured bursts and a white rocket are over the village, with the bonfire by the church.
  - At 12:00 the bursts are fainter but visible.
  - Market stalls are correctly absent at night and present by day.
- **Limitations:** the sparks are small cubes and read as pastel by day. Feasts get only the bonfire.
## 2026-11-26 — Fable review #7 and its quick fixes

- **Review #7 (Fable, read-only):**
  - **Traffic per-frame cost is fine.**
  - **Bugs:**
    - allocations at every junction
    - placement retried every frame
    - teleport when a tile stops being road
    - yaw and lane snaps at corners
    - lorries drive through the open trench
    - traffic ignores the hour
    - skaters out before the ice spreads (`harsh > 0.3`)
    - skater loops could leave narrow channels
    - train newcomers could spawn inside the station
    - the gate shot misses the 30 s dusk stream (slot every 3-4 min)
    - the train shot missed the arrival
  - **Ideas, ranked:**
    1. fireworks and a bonfire at celebrations
    2. dusk and dawn (boats moor with lanterns, traffic thins, headlights)
    3. geese skeins
    4. harvest home
    5. tide and mudflats
    6. real-calendar moments (December tree, New Year fireworks)
    7. school run
  - Designs are given for fireworks and for dusk and dawn.
- **Changed now:**
  - `WorldView`: skaters only when `harsh > 0.8`.
  - `Main`: the train camera stop starts at `TrainTick` 250, as the train pulls in.
  - `Wildlife.StepTraffic`: a 2 s cool-down after a failed placement; junction choice uses no per-arrival arrays.
  - `Simulation.ArriveMigrants`: a newcomer whose spot is a building tile appears at the gate point instead (the same RNG draws).
- **Verified:** `dotnet build`: 0 errors. SimTests ALL PASSED. Seed 1000 era-end: 30 68 113 149 188 214 249 290. Train arrivals 12, 0, 11, 11.
- **Next:** fireworks and a bonfire; dusk and dawn; then the remaining traffic polish (trench, hour, corner smoothing).
## 2026-11-26 — Update clip 10 published

- **Done:** 3 scenes recorded (free RAM 15.6 GB). The cart scene was cut because the carts were too small to read. Final clip: lorries on 20th-century streets and the mill gate at dawn (`--watch-gate`), 0:20, 1.7 MB. Published publicly (not made for kids) as "DeskCiv update 10 – carts and lorries on the streets, and a look at the mill gate": https://youtu.be/IUWKHSeFYoY. The description lists the traffic, the gate camera, the tunnel fix and the wedding path fix.
- **Limitation:** carts are described but not clearly shown; no hands are visible at the gate.
- **Next clip due:** about 12:20. Fable #7 is due about 10:55.
## 2026-11-26 — Camera at the mill gate at the shift change; `--watch-gate`

- **Changed:** `game/src/Main.cs` `NextShot`.
  - Slot 5 of every 7, from 06:00 to 07:30 and 19:30 to 20:30 (when mill hands go in and come out), frames the mill gate from the gate side. The azimuth is the factory `Yaw`, at 1.5x distance with a little lift.
  - The new test switch `--watch-gate` holds the camera there.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1795, 07:00: the mill's gate façade (arched gateway, chimney) faces the camera, the first time the gate side has been seen. No hands were at the gate in that frame.
## 2026-11-26 — Tunnel only into hills; wedding couple paths

- **Changed:** `game/src/Sim/Simulation.cs`.
  - `RailEnd`: the line may end in a tunnel only where the first unwalkable tile is ground higher than 3 m (hills), not at a river bank or creek (Fable review #6).
  - The bride and groom walk to the church with the normal path limit (9000 expansions) instead of 300, so a couple from across town no longer walks straight into the first wall.
- **Verified:** SimTests ALL PASSED.
  - Seed 40595's line moved to a new station lot (`38,110`, 129 tiles, was `194,10`, 146 tiles); its old line probably ended at water. The other seeds' lines are unchanged.
  - Era-end (seed 1000): 30 68 113 149 188 214 249 293.
- **Not done from #6:** stopping porter dispatch from taking the couple during the wedding (left; it only shortens their stay).
## 2026-11-26 — More street traffic, kept to the town

- **Changed:** `game/src/Wildlife.cs`.
  - Traffic capacity raised to 16 carts and 24 lorries. The count shown is `4 + houses/5`, capped at capacity.
  - Vehicles are placed on a random road tile within 120 m of the village centre when one can be found (300 tries), otherwise anywhere.
  - With about 830 road tiles, the earlier maximum of 8 vehicles spread over the whole map meant a frame rarely showed one.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1939, `--focus=220,175 --distance=130`: at least three lorries (green cab, canvas bed) are on the town's asphalt streets. The first on-screen check of lorries.
## 2026-11-26 — Street traffic: horse carts, then lorries (view only)

- **Changed:**
  - `game/src/MeshKit.cs`: new `Cart()` (horse and two-wheeled loaded cart) and `Lorry()` (cab and canvas-covered bed), both facing local +Z.
  - `game/src/Wildlife.cs`:
    - Two new flocks using a new rigid shader style 3: 8 carts at 2.6 m/s and 8 lorries at 6.5 m/s.
    - Carts show in eras 4-6 and lorries in era 7. The count is `2 + houses/12`, capped at 8.
    - `StepTraffic`: each vehicle drives from road tile to a neighbouring road tile, keeping 0.9 m to the right. At each junction it picks a random next street, not back the way it came unless at a dead end. It is placed on a random road tile when its tile is not road. The wildlife RNG is used, never the sim.
- **Verified:**
  - `dotnet build`: 0 errors.
  - Seed 1000, AD 1601, `--focus=200,170`: zoomed crops show horse carts on the cobbled streets.
  - The AD 1939 crop showed no lorry in that area. Lorries are not yet confirmed on screen.
- **Limitations:** vehicles drive through each other and through walkers; no turning arcs (they pivot at tile centres).
## 2026-11-26 — Update clip 9 published

- **Done:** 3 scenes recorded (free RAM about 16.3 GB); the station scene was cut because the train was away during the kept window. Final clip: skaters on the frozen estuary and a wide frozen-river view with no boats (`--test-harsh`, 194 BC), 0:20, 1.7 MB. Published publicly (not made for kids) as "DeskCiv update 9 – a frozen estuary with skaters, and a train that keeps time": https://youtu.be/8_bV9Gkrq14. The description lists the ice, skaters, boats ashore, the sim train timetable with newcomers, the camera stops and the review #6 fixes.
- **Limitation:** the train timetable and train newcomers are described, not shown.
- **Next clip due:** about 09:45.
## 2026-11-26 — Skaters on the frozen river (view only)

- **Changed:**
  - `game/src/MeshKit.cs`: new `Skater()`, a small figure (dark coat, red scarf).
  - `game/src/Wildlife.cs`: a flock of 8 skaters, shown only while the river is frozen. Each glides in a wide oval (5-15 m) on the ice off the village bank, about a third of the way out towards mid-river, facing along its loop. It uses the wildlife RNG, never the sim.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, `--start-year=150.86 --test-harsh --focus=160,110`: six or more skaters stand out on the ice with their red scarves.
- **Limitations:** the figures are rigid (no skating stride) and do not interact with villagers.
## 2026-11-26 — Fishing boats stay ashore when the river freezes

- **Changed:**
  - `game/src/Wildlife.cs`: new `Frozen` setter. While it is set, the fishing-boat flock is hidden (`ApplyCounts`).
  - `game/src/WorldView.cs`: sets `_wildlife.Frozen = harsh > 0.3f` alongside the water `ice` parameter.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, `--start-year=150.86 --test-harsh`: the frozen river no longer has a boat sitting in the ice.
## 2026-11-26 — Camera visits the standing train and the frozen river; `--test-harsh`

- **Changed:**
  - `game/src/Main.cs` `NextShot`:
    - Slot 2 of every 7 frames the station while the train stands (`TrainTick` 400-900), at 1.8x close distance.
    - Slot 4 takes a wide (3.5x) look over the river beside the village late in a harsh winter (`ActiveEvent == 2`, year fraction above 0.8).
  - New `--test-harsh` switch (`Simulation.HarshWinterForTest` via `SimHost`) makes the current year a harsh winter.
- **Verified:**
  - `dotnet build`: 0 errors.
  - Seed 1000, `--start-year=150.86 --test-harsh`: the estuary shows the grey-white broken ice across the shallow river, the first on-screen check of the ice. A fishing boat still sits on it.
  - The station camera stop is not yet seen on screen.
- **Sim unchanged** apart from the test-only method, so SimTests were not re-run.
## 2026-11-26 — The train runs on the sim's timetable; newcomers step off it (save v17)

- **Changed:**
  - `game/src/Sim/Simulation.cs`:
    - `TrainTick = Tick % 2400` when there is a railway. The train is out of the tunnel at 0-400, standing at 400-1000, back at 1000-1400 and away after that.
    - Migrant arrivals that fall due while a railway exists set `MigrantsOnTrain`. They step off at the station gate when `TrainTick == 600`, while the train stands.
    - In the 20th century a yearly train arrival comes at tick 3000 of the year, if the town is 8 or more below its cap and has room for at least 6 (`HousingRoom()`).
    - Save v17 adds `MigrantsOnTrain` (v9-16 still load). Included in `StateHash`.
  - `SimHost.cs`: snapshot `TrainTick`.
  - `WorldView.UpdateRailway`: the view's train clock re-syncs to the sim's timetable whenever it drifts more than 2 s, and runs on between snapshots.
- **Verified:**
  - `dotnet build`: 0 errors.
  - SimTests ALL PASSED. Train arrivals in the final event log: 11, 0, 11, 9.
  - Eras 0-6 are unchanged. Era 7 ends slightly higher: 290, 292, 291, 291.
  - Not viewed on screen.
## 2026-11-26 — Fable review #6 and its quick fixes

- **Review #6 (Fable, read-only):**
  - **Bugs:**
    - Fields on the rail row could be urbanised into houses on the track.
    - The tunnel could open at water.
    - Train migrants never meet the view-only train.
    - Wedding couples could be handed loads or get no path.
    - District heating did not count as warmth.
    - The quay lot's ±10-tile search could slide under the ship crowd.
    - Market day pulled working adults (porters, builders, bakers) up to 70 m away.
  - **Drift cause:** the market adult pull and porters waiting at the quay for the whole 40%-of-year `TraderVisiting` window.
  - **Ideas:**
    - a sim-side train timetable that migrants meet
    - a ship unloading line
    - view-only skaters
    - carts and lorries on the streets
    - a mill whistle moment
    - a points-of-interest list for the camera
    - `--test-harsh` and `--train-at` switches
- **Changed now (`game/src/Sim/Simulation.cs`):**
  - The market draws only free villagers (children, elders, jobless non-porters).
  - Quay porters: only `Id % 4 == 0`, only during the crowd window (ticks 2400-4800 of a ship year).
  - `Warm` is true with wood or district heating.
  - `UrbanizeField` skips any field the rail row crosses.
  - The quay lot must lie at or beyond `CenterX + 36`.
- **Verified:** SimTests ALL PASSED.
  - Seed 1000 era-end: 30 68 113 149 188 **214** 253 281 (era 5 recovered from 187).
  - Other seeds' era 5: 221, 225, 225. Rails 160/172/146/162 tiles with 0 buildings on the line; quay lots on 3 of 4 seeds.
- **Open from #6:** tunnel-at-water check; train timetable in the sim; wedding couple protection; camera points of interest; test switches; carts; skaters; ship unloading line.
## 2026-11-26 — Porters wait on the quay while the trading ship is in

- **Changed:**
  - `game/src/Sim/Simulation.cs` `Wander`: while the trading ship lies at anchor (`TraderVisiting`) and the quay has its sim lot (kind 5), idle porters with an even id wander on the quay (6 m) instead of at the storehouse door.
  - `game/src/WorldView.cs`: during the visit, three barrel/goods piles (`GoodsPile(3)`) stand on the bank at the quay.
- **Verified:** `dotnet build`: 0 errors. SimTests ALL PASSED. Seed 1000 era-end: 30 68 113 149 185 187 255 262. Era 5 is down again (199 to 187), probably because porters wait further from the stores during visits.
- **Not verified on screen:** the AD 1569 ship shot framed other houses, not the quay.
- **Watch:** seed 1000 era-5 population has dropped from 220 to 187 over the market, quay-lot and quay-porter changes; still within test limits.
## 2026-11-26 — Update clip 8 published

- **Done:** 2 scenes recorded (free RAM about 16.5 GB) and trimmed: market stalls by the church (AD 1224, first 7 s; then the camera moved on) and a 20th-century winter town with few smoking chimneys (district heating). 0:17, 1.4 MB. Published publicly (not made for kids) as "DeskCiv update 8 – market day, district heating and newcomers by train": https://youtu.be/x_uAK8Pa7Ko. The description lists the market, district heating, train newcomers, wedding couples, the quay lot and the digger fix.
- **Limitations:** short clip; train newcomers and weddings are described, not shown.
- **Next clip due:** about 06:40. Fable #6 due about 05:10.
## 2026-11-26 — Quay gets its own sim lot (landmark kind 5)

- **Changed:**
  - `game/src/Sim/Simulation.cs` `PlaceLandmarks`: from era 5, a 5x1-tile strip on the village bank near `CenterX + 41` (just downstream of the ship crowd) is reserved as landmark kind 5. It is the nearest free strip along the bank (±10 tiles) or up to 2 rows inland; its tiles become `Building`, so nothing else is built where the quay stands.
  - `WorldView`: the quay is drawn at the reserved lot's X; older saves fall back to `CenterX + 41`.
- **Verified:**
  - SimTests ALL PASSED. Quay lot reserved on 3 of 4 seeds (1000, 16838, 72271). 40595 found no free strip, so its quay is still render-only.
  - Seed 1000 era-end: 30 68 113 149 185 199 245 256 (later eras lower than before, since the strip removes bank land).
  - Not viewed on screen.
- **Not done:** porters unloading the ship at the quay.
## 2026-11-26 — Market stalls keep clear of the churchyard

- **Changed:** `game/src/WorldView.cs` `UpdateMarket`. Stalls skip any spot within 3 m of a grave. If the inner 7.5 m ring has too few free spots, they try an offset outer ring at 11 m.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1224: five stalls on the open green beside the church, none among the graves.
## 2026-11-26 — Market day by the church (Fable review #5 idea 4)

- **Changed:**
  - `Sim/Simulation.cs`:
    - New `MarketPlace()`. From the Middle Ages, outside winter and by day, the first 90 s of every sixth of a year is market day at `ChurchDoor()`.
    - In `Wander`, villagers with free time (children, elders, the jobless) and every third adult who reaches `Wander` mill round the market within 8 m, if they are within 70 m. They use the cosmetic RNG as before; the economy RNG is untouched.
  - `SimHost.cs`: `MarketOpen`, `MarketX`, `MarketZ`.
  - `MeshKit.MarketStall()`: a trestle table of goods under a red-and-cream striped awning.
  - `WorldView.UpdateMarket`: up to 8 stalls in a 7.5 m ring round the church door, only on open walkable ground, never on building or rail tiles or inside the church.
  - `Main.cs`: shot slot 1 of every 7 visits the market when it is on.
- **Verified:**
  - `dotnet build`: 0 errors.
  - SimTests ALL PASSED. Seed 1000 era-end: 30 68 113 149 185 205 257 278 (era 5 dipped from 220).
  - Game, seed 1000, AD 1224: the camera frames six striped stalls with goods beside the church.
- **Limitations:**
  - The ring can put stalls among the churchyard graves.
  - Only a few villagers were at the stalls in that shot.
## 2026-11-26 — District heating follows the boiler house and the sewer dig

- **Changed:**
  - `game/src/Sim/Simulation.cs`: new `DistrictHeated(b)`, true for a house in era 7 when a boiler house exists (landmark kind 2) and the house door is within 2 tiles of a sewered tile (`Pipes >= 2`). The hot-water pipes go down with the sewers, so heating spreads street by street, outwards from the water tower. Winter wood burn: ×0.35 on district heating, ×0.7 from the Industrial era otherwise, ×1 before. Previously every house got ×0.35 in era 7, even with no boiler house.
  - `SimHost.cs`: `BuildingView.DistrictHeated`.
  - `WorldView.cs`: hearth smoke uses the per-house flag instead of "era 7 and a boiler exists". A heated house rarely smokes; others smoke as usual.
  - `tests/SimTests`: the report adds `heated=heated/houses`.
- **Visible:** In 20th-century winters chimney smoke fades out district by district as the sewer/heating dig spreads.
- **Verified:**
  - `dotnet build`: 0 errors.
  - SimTests ALL PASSED. Heated houses at the end: 51/58, 50/56, 60/73, 62/69. Seed 1000 era-end: 30 68 113 149 190 220 257 289.
  - Not viewed on screen.
- **Still era-keyed:** the sickness reach before the mains (eras 4-5).
## 2026-11-26 — Mains diggers must reach the trench

- **Changed:** `game/src/Sim/Simulation.cs`. A water-main or sewer stint (`Act.Road`, phase 8) counts only if the digger ends within 4 m of the trench tile. Before, a digger whose walk was blocked by a wall still finished the stint from wherever they stopped (Fable review #5).
- **Skipped:** children skating on the harsh-winter ice. Water tiles are unwalkable, so it needs path changes; left for later.
- **Verified:** SimTests ALL PASSED.
  - Mains (water/sewer/roads): 735/697/825, 835/771/895, 676/651/676, 789/771/851.
  - Seed 1000 era-end: 30 68 113 149 190 220 257 283.
## 2026-11-26 — Newcomers by train; couples go to their own wedding

- **Changed:**
  - `game/src/Sim/Simulation.cs` `ArriveMigrants`: when the railway runs, a migrant group appears at the station gate instead of at the western bank, with the headline "Newcomers step off the train at the station". The same RNG draws are used; the gate must be walkable.
  - `Family()`: when a wedding gathering starts, the bride and groom (if idle or wandering) walk to the church door and stand there for 60 s.
  - `tests/SimTests`: the report adds `bytrain=`.
- **Verified:** SimTests ALL PASSED. Weddings in the final log: 8, 10, 10, 8. Seed 1000 era-end: 30 68 113 149 190 220 258 279.
- **Limitation:** `bytrain=0` on all seeds. In the 20th century migrants arrive only when the town falls behind its target population, which rarely happens, so train arrivals are rare. Not seen on screen.
## 2026-11-26 — Update clip 7 published

- **Done:** 3 scenes recorded (free RAM about 17 GB). Two were cut: the "line enters a tunnel" scene did not show the portal, and the fire scene was too distant to show the adults helping. Final clip: the train pulling into the station (AD 1914, `--focus=150,232`), 0:14, 1.2 MB. Published publicly (not made for kids) as "DeskCiv update 7 – the railway arrives, river ice and better fire crowds": https://youtu.be/mUSTSxtPJyY. The description lists the railway and train, harsh-winter river ice, the fire fixes, mill-hand walk-home and the quay move.
- **Limitations:** a short clip. Ice, fire crowds and the tunnel portal are described but not shown.
- **Next clip due:** about 03:35.
## 2026-11-26 — Railway line and steam train (Fable review #5 design, adapted; save v16)

- **Changed:**
  - `Sim/Model.cs`: new `TileKind.Rail = 6`. It is walkable, nothing can be built on it, and streets may cross it on the level (`CanEnter` with `toRoad`).
  - `Sim/Simulation.cs`:
    - The station scan (era 7) first looks within 60 tiles for a 7x3 lot whose back row runs west, clear of buildings, for at least 25 tiles before the ground becomes unwalkable (`RailEnd`). The line enters a tunnel there.
    - Otherwise the old rule applies and there is no line.
    - `LayRail` turns `Free` tiles on the row into `Rail`; roads, fields and walkable `Blocked` ground keep their kind. It forces a gathering at the station gate, with the headline (importance 3) "The railway reaches the town: the first train pulls into the new station".
    - Each New Year, `Free` tiles on the row become `Rail`.
    - `RailRow`, `RailX0` and `RailX1` are saved (v16; v9-15 still load) and hashed.
  - `SimHost.cs`: the snapshot carries the rail row and its ends.
  - `MeshKit`: `RailTrack()` (ballast, sleepers, rails per 4 m tile), `Train()` (green locomotive with tender and 3 red carriages, about 40 m), `TunnelPortal()`.
  - `WorldView`:
    - `UpdateRailway` draws the track tiles (skipping any built-over tile) and the tunnel portal.
    - `AnimateTrain` runs a view-only 4-minute cycle: 40 s out of the tunnel to the station, 60 s standing, 40 s back, then gone.
  - `tests/SimTests`: the report adds `rail=stationX,Z:tiles/buildingsOnLine`.
- **Design change from Fable:** Fable proposed running the line to the west map edge. Every map edge is unwalkable (hills or water), so no lot qualified on any seed; diagnostics showed thousands of candidate rows blocked by unwalkable ground. Ending the line at a tunnel into the hills fixed it.
- **Verified:**
  - SimTests ALL PASSED. All 4 seeds lay a line: 160, 172, 119 and 162 tiles, with 0 buildings on it.
  - Eras 0-6 are unchanged; seed 1000 is 30 68 113 149 190 223 251 288.
  - Game, seed 1000, AD 1914, `--focus=150,232`: the track runs past the station platform, and 55 s in the train stands at the station.
- **Not done:** migrants stepping off the train; crowds on the platform at train time; crossing gates. The train is not tied to the time of day.
## 2026-11-25 — Fable review #5 and its quick fixes

- **Review #5 (Fable, read-only):**
  - **Bugs:**
    - Gatherings only drew children, elders and the jobless, and every jobless adult is a porter or builder, so the bucket chain had no adults and couples missed their own weddings.
    - A fire had no room check, so the family could be left homeless for the winter.
    - A burnt pre-industrial house was marked `Renewed` (+2 capacity, larger mesh).
    - Fires began in era 3 (Iron Age) although the notes and headline say the Middle Ages.
    - `IgniteForTest` could reset a live fire.
    - Mill hands stepped straight back in when a shift ended, and vanished 4.5 m from the gate.
    - Diggers with no path could finish a stint from the first wall they hit.
  - **Railway design (for later, save v16):** a `Rail` tile row behind the station, running west to the map edge. The kind-4 lot is accepted only if that row is clear of buildings; roads stay as level crossings. The train is view-only (08:00 and 17:00). Migrants step off it. A headline and gathering mark the opening.
  - **Next ideas:** the right people at gatherings; the quay as a sim lot with unloading; heating and sickness keyed to what is built; a weekly market; children skating on harsh-winter ice.
- **Changed now:** `game/src/Sim/Simulation.cs`.
  - During a fire every villager within 40 m who reaches `Wander` joins the crowd, adults included.
  - Fires begin in era 4 (Middle Ages).
  - If the other homes cannot take the family, the bucket chain saves the house ("The bucket chain saves the house just in time"; counts as put out).
  - A house that burns down is rebuilt larger only from the Industrial era.
  - `IgniteForTest` does nothing while a fire is burning.
  - Mill hands enter within 2.5 m of the gate (was 4.5 m). When the shift ends they walk home for 40 s.
- **Verified:** SimTests ALL PASSED on 4 seeds.
  - Seed 1000 era-end: 30 68 113 149 190 223 251 285.
  - Shifts: 3317, 3468, **4594 (seed 40595, was 0)**, 1827.
  - Fires (started/saved): 7/4, 9/6, 4/2, 14/3.
- **Still open from #5:** couples walking to their wedding; the digger no-path stint; the railway; the quay as a sim lot; heating keyed to the boiler house; a market; children skating on the ice.
## 2026-11-25 — River ice in harsh winters (Fable review #4 item 6, visual part)

- **Changed:** `game/src/WorldView.cs`. The water shader gains `uniform float ice`. Ice spreads out from shallow water (depth below `0.9*ice+0.05`), but not on the open sea (`world.x` 140-220 fade). It is broken up by noise, tinted grey-white with cracks, rougher, and without ripples. It is driven by the existing harsh-winter curve `harsh` (late autumn to the end of a harsh winter).
- **Tried and dropped:** ice in every winter (`snowLevel*0.5`, then `*0.3`). The estuary is shallow, so even a thin setting frosted most of the river and the ship looked icebound. Normal winters now have no ice.
- **Verified:**
  - `dotnet build`: 0 errors.
  - Winter shots at 192 BC (seed 1000, normal winter) with the trial settings showed the frosted river; with the final setting a normal winter is unchanged.
  - Not verified: the harsh-winter look itself (no harsh-winter year was framed). At `ice=1` the whole shallow river should read as frozen.
- **Not done:** children sliding on the ice (Fable's wander target).
## 2026-11-25 — Quay moved off the ship crowd (Fable review #4 item e)

- **Changed:** `game/src/WorldView.cs` `UpdateWaterSupply`. The quay now stands at `CenterX + 41` instead of `+ 25`, just downstream of where the sim gathers the crowd for the trading ship (`CenterX + 25`, radius 7). The ~18 m quay no longer covers the onlookers. Render-only; the sim is unchanged.
- **Verified:** `dotnet build`: 0 errors. Seed 1000, AD 1569 trading ship: the quay sits at the water's edge in the foreground, and villagers walk down to the bank beside it, not into it.
- **Open:** the quay is still render-only (not a sim lot), so a bank building could still be placed under it.
## 2026-11-25 — Fire camera framing; update clip 6 published

- **Changed:** `game/src/Main.cs`. When the headline event is a burning house, the director aims 5 m higher and stands 1.9x close distance back (instead of 1.35x), so the flames over the roof stay in frame. In the first recording they were cut off at the top edge.
- **Clip 6:** 3 captioned scenes:
  - a Middle Ages fire (AD 1224, `--test-fire`)
  - a 20th-century fire (AD 1914)
  - the new quay (AD 1598)

  0:34, 2.9 MB. Published publicly (not made for kids) as "DeskCiv update 6 – house fires, church weddings, mill shifts and a new quay": https://youtu.be/MU2O_PmKKvU. The description lists the fires, weddings, mill shifts, the quay and the camera change.
- **Verified:** `dotnet build`: 0 errors. Clip contact sheet: flames in frame in both fire scenes, with the quay along the bank.
- **Limitations:** Weddings and mill shifts are described but not shown in the clip. Neither has been seen on screen yet.
- **Next clip due:** about 00:30.
## 2026-11-25 — Quay model rebuilt (third try)

- **Changed:**
  - New Gemini isometric view, then a 2x2 grid with each feature's position stated per view; dividers removed. Saved as `landmark-quay3.png`.
  - `landmark_quay` rebuilt: IoU 0.686 (old 0.636), 17.8 x 4.5 x 3.7 m. A cut-stone quay with bollards, a wooden crane at one end and steps at the other.
  - At 7.4 m tall (the old height) it came out 29 m long, so it was rebuilt at 4.5 m.
  - The old files are backed up in the scratchpad `quay-old` folder.
- **Verified:** seed 1000, AD 1598, `--focus=187,125`: the quay sits along the village bank with the trading ship beyond, and the steps and crane are readable.
- **Limitations:**
  - The block is shallow (3.7 m deep), so its top reads as a narrow wall-top from some angles.
  - The crowd for the trading ship still gathers beside the quay; the overlap Fable flagged (item e) is not fixed.
## 2026-11-25 — Church weddings (Fable review #4 item 4)

- **Changed:**
  - `game/src/Sim/Simulation.cs` `Family()`: when a couple pairs up by day and there is a parish church, the village gathers at the church door for 90 s. The gathering uses the normal half-year spacing (not forced), so funerals and feasts still get their turn. Headline (importance 2), logged only if the gathering starts: "Wedding bells: the village gathers at the church".
  - `tests/SimTests`: the report adds `weddings=` (counted in the capped event log).
- **Visible:** In the Middle Ages and later the camera sometimes shows a crowd outside the church for a wedding.
- **Verified:**
  - SimTests ALL PASSED. Weddings in the final event log: 13, 13, 6, 6.
  - Seed 1000 era-end: 30 68 113 149 186 227 254 285.
  - Not viewed in game; it uses the same gathering path as church funerals, which have been seen on screen.
## 2026-11-25 — Mill shifts (Fable review #4 item 3)

- **Changed:**
  - `game/src/Sim/Model.cs`: new `Act.Shift`.
  - `game/src/Sim/Simulation.cs`:
    - From the Industrial era, by day, idle adults with `p.Id % 4 == 0` (any trade, but not porters or builders) walk to the mill gate. Once within 4.5 m they go inside (`Indoors`) for a shift of up to 120 s. They come out at dusk, when hungry or when the shift ends.
    - Jobless adults with an even id still stand at the gate.
    - New helper `FactoryGate()`. Diagnostic `ShiftsWorked` (not saved).
  - `tests/SimTests`: the report adds `shifts=` and `jobless=`.
- **Finding:** in all 4 full runs there were **0 jobless adults** at the end and 0 shifts under a jobless-only rule. The earlier "factory-gate jobless crowd" therefore almost never appeared; that is why shifts now draw on idle workers.
- **Verified:**
  - SimTests ALL PASSED. Shifts per full run: 4689, 1629, 0 (seed 40595; the gate is probably not reachable there), 2520.
  - Seed 1000 era-end: 30 68 113 149 186 231 250 300.
  - Game shots of the mill at 07:00, 10:00 and 13:00 (AD 1793) showed the long south façade but not the gate side, so the walk-in has **not** been confirmed on screen.
- **Open:** find out why seed 40595 has no shifts. Consider a `--look=Mill gate` view.
## 2026-11-25 — House fires with a bucket chain; water mains save houses (save v15)

- **Changed:**
  - `game/src/Sim/Simulation.cs`:
    - A new New Year band (roll 0.52-0.58, Middle Ages and later, one fire at a time). The same roll picks an occupied house and a time between late spring and autumn, so no extra RNG draw.
    - `UpdateFire`: the house burns for 150 s. A forced gathering at its door draws the neighbours in a bucket chain. Headline (importance 3): "Fire! A house is burning and the neighbours form a bucket chain".
    - At the end, a house with a water main within 2 tiles is saved: "A hydrant on the new water main saves the burning house". Otherwise it burns down: the family is rehoused, and the house becomes a renewal site (rebuilt larger, +2 capacity).
    - Counters `FiresStarted` and `FiresPutOut`; `Burning`. `IgniteForTest()` backs `--test-fire`.
    - Save v15 (v9-14 still load). `StateHash` includes the fire state.
  - `SimHost.cs`: the snapshot gains `Burning`, `FireX` and `FireZ`, plus `IgniteForTest`.
  - `MeshKit.Flame()`: two crossed upright quads, hot yellow fading to nothing at the top, drawn additively.
  - `WorldView`:
    - 5 flame tongues flicker every frame over the roof. Their height follows the era's house height.
    - 14 large smoke puffs take the smoke budget first.
  - `Main.cs`: new `--test-fire` switch (the occupied house nearest the centre catches fire after 20 s).
  - `tests/SimTests`: the report adds `fires=started/put out` and `fireyears=` (from the capped event log).
- **Visible:** In the Middle Ages and later, now and then a house goes up in flames. Smoke rises, the camera goes there, and villagers crowd round. Before water mains the house burns down and is rebuilt. On piped streets the hydrant saves it.
- **Verified:**
  - SimTests ALL PASSED on 4 seeds. Fires per full run (started/put out): 8/5, 12/3, 16/4, 13/5. Seed 1000 era-end: 30 68 113 149 186 231 256 279.
  - `--test-fire` seed 1000: at AD 1224 and AD 1793, large flames over the roof, the smoke column and the headline are visible, and the camera frames it.
- **Limitations:** The bucket chain is the generic gathering crowd, with no buckets carried. The flames are stylised. Fire timing in the game drifts from the tests when started with `--start-year`.
## 2026-11-25 — Update clip 5 published on YouTube

- **Done:** 6 captioned scenes recorded in two halves (free RAM 17.9-18.3 GB). The fish-house scene was cut because the building was not identifiable. Final clip: 0:56, 4.7 MB, mean -22.7 dB. Scenes: water mains dig (AD 1773), the ship crowd walking to the bank, the sewer dig (1914), the greengrocer with its glasshouse, and the camera looking over rooftops at the mill. Published publicly (not made for kids) as "DeskCiv update 5 – water mains and sewers, a busier ship crowd and a better camera": https://youtu.be/yFgVkT_QQdA. The description lists all changes since update 4.
- **Limitations:** In the sewer scene the trench is hard to see. In the ship scene the crowd is still walking to the bank.
- **Next clip due:** about 21:30.
## 2026-11-25 — Camera visits the water-mains dig; `--watch-dig`

- **Changed:** `game/src/Main.cs`. Every 7th director shot (slot 6) frames the street tile being dug, if there is one: 1.2x close distance, least-blocked view, 24 s. The new test switch `--watch-dig` holds the camera on the dig and follows it every 15 s.
- **Verified:** `dotnet build`: 0 errors. Seed 1000 with `--watch-dig`: at AD 1773 the trench, spoil and a digger are in the middle of a street, with the water tower behind. At AD 1914 (sewers) the street is framed, but the trench is hard to make out.
## 2026-11-25 — Water mains and sewers in the simulation (save v14)

- **Changed:**
  - `game/src/Sim/Simulation.cs`:
    - New `Pipes` per tile (0, 1 water main, 2 plus sewer), with `DigTile`, `DigWork`, `MainsWaitUntil`, `MainsLogged` and `MainsVersion`.
    - `TryMains` runs after road and dike work, for builders and idle seasonal workers. Up to 3 diggers (`Act.Road`, phase 8, 20 s stints, standing offset from `p.Id`) dig one street tile at a time. Three stints finish a tile.
    - `NextDigTile` picks a road tile next to a finished one, nearest the water tower (or the centre), lowest index on ties, with no RNG. The network spreads outwards, and new suburbs get dug later. When every street is done it waits a month before looking again.
    - Water mains are laid in the Industrial era and sewers in the 20th century. Headlines (importance 2): "Workers dig up the streets to lay the first water mains" and "...again to lay sewers".
    - Sickness from era 6: a home whose door is within 2 tiles of a pipe has reach 0.05 (water) or 0.03 (sewer); otherwise 0.12. Still one `Random.F()` per person.
    - Save version 14 appends the pipes data. Versions 9-13 still load, with no pipes. `StateHash` includes the pipes.
  - `SimHost.cs`: the snapshot carries `Pipes`, `DigTile` and `MainsVersion` (copied only when it changes).
  - `WorldView.UpdateWaterMains`: the trench is drawn at the sim's `DigTile` instead of a fake based on the year.
  - `tests/SimTests`: the report adds `mains=water/sewer/roads`. New check: at least 70% of road tiles have water mains by the end.
- **Visible:** In the Industrial era the trench moves street by street out from the water tower, with real diggers. In the 20th century it comes back for sewers. Piped streets suffer much less in outbreaks.
- **Verified:**
  - SimTests ALL PASSED on 4 seeds. Eras 0-5 are bit-identical: seed 1000 is 30 68 113 149 189 225 248 284.
  - Mains coverage (water/sewer/roads): 733/643/830, 814/797/896, 665/665/665, 792/761/892.
  - A game shot at AD 1763 did not frame the dig (the camera does not visit it yet).
- **Not done:** Fable's fix (c), moving the `TryBuild` standing offset to the cosmetic RNG, was tried. It reshuffled every seed and failed the year-50 "bread chain" test on seeds 56433 and 32676, so it was reverted. Heating and sickness still key on era rather than on the boiler house being built.
## 2026-11-25 — Fable review #4; ship crowd always gathers

- **Review #4 (Fable, read-only):**
  - **Bugs:** (a) the ship gathering was not forced, so a recent feast or funeral cancelled it silently while the headline was still logged; (b) feast and funeral headlines were logged even when no one gathered; (c) the `TryBuild` standing offset uses the economy `Random` (`Simulation.cs` ~696); (d) heating ×0.35 and sickness reach key on era, not on what was built; (e) the render-only quay overlaps the ship crowd. The "ice patch" item could not be found; Fable thinks it was the frozen `Warm` (already fixed).
  - **Water mains design:** per-tile `Pipes` (1 water, 2 + sewer). Diggers work outward from the tower: water in era 6, sewer in era 7. `TryMains` uses `Act.Road` phase 8, up to 3 diggers, 3 stints per tile. Sickness reach is 0.05 on piped streets and 0.03 when sewered (0.12 otherwise). Save v14; tests.
  - **Next ideas:** house fire with a bucket chain (put out on piped streets); railway track and train; factory shifts at dawn and dusk; church weddings; the quay as a sim lot with unloading; an ice rim in harsh winters.
- **Changed now:** `game/src/Sim/Simulation.cs`. `Gather` returns whether a gathering started. The ship gathering is forced. Feast and funeral headlines are logged only when the village actually gathers.
- **Verified:** SimTests ALL PASSED on 4 seeds. Era-end populations moved slightly; seed 1000 is now 30 68 113 149 189 225 244 282 (before: ...191 223 253 281).
## 2026-11-25 — Two Fable review #3 sim fixes

- **Changed:** `game/src/Sim/Simulation.cs`.
  - `UrbanizeField`: the three new corner houses plan road links before the old field becomes the fourth house, so a link could cross that lot. When the lot was claimed, its tiles became Building but stayed in `RoadQueue`, so a builder could later pave over the house. Claiming the lot now removes those tiles from the queue.
  - `RenewOldHouse`: clears `Warm` when the house is pulled down, so the flag no longer stays frozen while it is rebuilt.
- **Visible:** none expected; this prevents a rare road being laid over a new house.
- **Verified:** SimTests ALL PASSED on 4 seeds. Era-end populations are unchanged (seed 1000: 30 68 113 149 191 223 253 281). Portable build repackaged.
- **Open:** the "ice patch" item from Fable review #3 could not be recovered from notes; it will be raised again at the next consult.
## 2026-11-25 — Camera framing: pick the least-blocked view and rise when needed

- **Changed:** `game/src/Main.cs` `ClearAzimuth` now tries 16 directions. For each, it scores the buildings (assumed 18 m tall) and trees (9 m) that rise above the camera's sight line out to 45 m, picks the lowest score, and keeps the front view on ties. A new `_lift` (0-1, eased) raises the camera by up to half its distance when even the best direction is blocked. Each new shot resets it. `--look` aims 3 m above the building's base; landmark shots skip 8 m and look shots 5 m of their own footprint. The old code checked only 25 m, assumed nothing was taller than the sight line, and fell back to the front view, so in Industrial streets `--look` shots (and three scenes in clip 4) stared into a neighbouring roof.
- **Visible:** Close director shots of workers, landmarks and events in dense towns now look over the rooftops instead of into a wall.
- **Verified:** `dotnet build` Debug and Release: 0 errors. Seed 1000, AD 1793, `--look` Gatherer, Mill and Woodcutter at distance 45: before, all three were blocked (a roof, a gable, a façade); after, the grocer with its whitewashed glasshouse, the yellow-brick mill and the sawmill street are all in view. `tools/package_portable.ps1` re-run, dist 467 MB.
- **Note:** Godot `--path game` runs the **Debug** assembly. A Release-only build leaves test shots on the old code.
## 2026-11-25 — Industrial greengrocer and glasshouse (gatherer) model

- **Changed:** `gatherer_t3` (IoU 0.834, 11.1 x 7.0 x 7.1 m): a brick grocer's shop with produce crates next to a whitewashed glasshouse. The first Gemini sheet had clear glass. Green showed through the panes, giving IoU 0.709 and a mangled mesh in game, so it was moved to the scratchpad `rejected` folder (removed from `game/assets` and `dist`). The second sheet asked for opaque frosted glass. Every Industrial craft now has its own model; `workshop_t3` is only a fallback.
- **Verified:** `fisher_t3` seen in game at AD 1793 (timber fish house with an open counter). The `--look=Gatherer` shot at distances 45 and 80 was blocked by nearby town houses, so the final grocer is not yet confirmed visually.
- **Lesson:** ask for opaque or whitewashed glass in any image for the green-key pipeline.
## 2026-11-25 — Industrial fish house (fisher) model

- **Changed:** Gemini isometric view, then a 2x2 grid; dividers removed. `fisher-t3.png`, model `fisher_t3` IoU 0.911, 10.7 x 9.0 x 8.0 m (two-storey timber-and-brick fish market and smokehouse with an open counter, crates and barrels). Only the gatherer still uses `workshop_t3` in the Industrial era.
- **Verified:** built and published to `dist`; not yet viewed in game.
## 2026-11-25 — Update clip 4 published on YouTube

- **Done:** 8 captioned scenes recorded in two halves (free RAM 18.7-18.9 GB, free commit 13.3-13.6 GB). Frame review cut three weak scenes (ship crowd with few people; smithy/mill and sawmill hidden behind houses). Final clip: 0:50, 4.2 MB, HEVC 720p + AAC, mean -22.4 dB: textile mill, railway station, a street dug up for water mains, Industrial bakery and winery. Published publicly (not made for kids) as "DeskCiv update 4 – textile mill, railway station, water mains and Industrial-era workshops": https://youtu.be/Y8DCzn8Qibs. The description lists all changes since update 3.
- **Next clip due:** about 18:40. Next Fable consult about 17:30.
## 2026-11-25 — Industrial sawmill (woodcutter) model

- **Changed:** Gemini isometric view, then a 2x2 grid; divider lines removed. `woodcutter-t3.png`, model `woodcutter_t3` IoU 0.846, 14.1 x 6.9 x 8.1 m (timber-clad sawmill shed on a brick base, corrugated roof, open saw bay, plank stacks); texture coverage 0.54 (lower than usual). Only fisher and gatherer still share `workshop_t3` in the Industrial era.
- **Verified:** `--look=Woodcutter` at AD 1793: the corrugated-roof sawmill is visible in the background and at the frame edge; the new yellow-brick mill is also in the shot.
## 2026-11-25 — Industrial mill model

- **Changed:** Gemini isometric view, then a 2x2 grid (clean, no dividers or labels this time). `mill-t3.png`, model `mill_t3` IoU 0.892, 11.3 x 13.0 x 11.9 m (three-storey yellow-brick steam flour mill with a sack-hoist gable). With bakery, smithy and winery, the Industrial-era crafts now have their own models except woodcutter, fisher and gatherer (still `workshop_t3`).
- **Verified:** `--look=Mill` at AD 1793: the yellow-brick mill is visible behind a stepped-gable house (mostly occluded). The animated quern from `MeshKit.Moving(Mill)` is still placed at its old offset in front of the building (not checked against the new footprint).
## 2026-11-25 — Industrial winery model

- **Changed:** Gemini isometric view, then a 2x2 grid; divider lines removed with `unline.py`. `winery-t3.png`, model `winery_t3` IoU 0.875, 11.4 x 10.0 x 8.0 m (two-storey limestone winery with arched cellar door, barrels and a hoist beam). Used from the Industrial era by the tier-3 own-model lookup.
- **Verified:** `--look=Winery` at AD 1793 (camera very close) shows the textured limestone gable with arched windows and a tiled roof; the same shot shows a porter pushing a wheelbarrow of grain.
## 2026-11-25 — Industrial smithy model

- **Changed:** Gemini isometric view, then a 2x2 grid; the grid came with thin dark divider lines, painted over with the background green (`scratchpad/unline.py`, 16 px around the centre lines). Front and right views are both gable ends, so the views are only partly consistent. `smithy-t3.png`, model `smithy_t3` IoU 0.869, 10.3 x 8.0 x 9.7 m (brick forge hall, double doors, roof ventilator, forge chimney). Picked up by the tier-3 own-model lookup; it replaces the procedural brick factory smithy from the Industrial era on.
- **Verified:** `--look=Smithy` at AD 1793: the new smithy is at the edge of the frame (brick wall and door). The same shot also shows a house being rebuilt standing in its scaffold at 20%, i.e. the knock-down phase start.
## 2026-11-25 — Industrial bakery model; crafts may have their own tier-3 model

- **Changed:** `MeshKit.RealisticModel`: from tier 3 a craft first tries `<type>_t3` and only then the shared `workshop_t3`. Gemini (isometric first, then a 2x2 grid with chimney positions per view; this time no labels): `bakery-t3.png`, model `bakery_t3` IoU 0.891, 9.1 x 10.0 x 8.4 m (brick corner shop with shopfront, awning and oven chimney).
- **Verified:** Game build succeeded. `--look=Bakery` at AD 1793 shows the new brick bakery (back side with chimneys) among Industrial houses instead of the shared workshop. Render-only.
## 2026-11-25 — Jobless adults wait at the factory gate (Fable review #3, item 7, part)

- **Changed:** `Simulation.Wander`: by day, from the Industrial era (once the factory lot exists), half of the jobless working-age adults who are not porters or builders wander within 5 m of a point in front of the factory gate (lot centre + yaw direction). Uses only the cosmetic random stream; gatherings still take priority.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations: 1000 = 30 68 113 149 191 223 253 281; 16838 = 29 54 117 147 190 230 250 286; 40595 = 28 49 120 149 194 223 260 282; 72271 = 24 56 120 155 183 226 253 281. Game build succeeded. A shot of the factory at AD 1793 after 55 s showed only one or two people nearby; few adults are jobless at that time, so the effect is small on screen.
## 2026-11-25 — Knock-down phase for houses being rebuilt; quay redo dropped

- **Changed:** `WorldView.UpdateBuildings`: a renewed house that is still a site is drawn standing at full height when work starts (20%) and sinks to the ground as builders take it to 30%, then the new house rises over the remaining 70%. Render-only.
- **Verified:** Game build succeeded; at AD 1787 the overlay reads "Rebuilding an old house 20%", but the site itself was hidden behind houses in the shot, so the knock-down is not confirmed on screen.
- **Quay redo:** a new Gemini isometric view of a solid stone quay with a crane house was good, but the follow-up "2x2 sheet" came back with five views in a broken layout, so it was not used. The existing `landmark_quay` (IoU 0.64) stays.
## 2026-11-25 — Railway station landmark (Gemini; labels removed locally)

- **Changed:** Gemini isometric view, then a 2x2 sheet of the same station (brick-and-stone two-storey station house, clock gable, iron platform canopy). Gemini again added text labels; instead of spending another generation, the labels were painted over with the background green (`scratchpad/unlabel.py`, rows 43.5-49% and 89.5-95% of the height) to give `landmark-station.png`. Model `landmark_station` IoU 0.854, 22.4 x 12.0 x 7.7 m. `PlaceLandmarks` adds kind 4 from era 7 on the nearest free 7x3 lot, entrance facing the centre; `MeshKit.Station()` (model only), drawn by `WorldView` and added to the camera landmark list.
- **Verified:** `tests/SimTests` ALL PASSED; all seeds get `0+1+3+4+2`. Era-end populations: 1000 = 30 68 113 149 191 223 252 280; 16838 = 29 54 117 147 190 230 262 283; 40595 = 28 49 120 149 194 223 254 287; 72271 = 24 56 120 155 183 226 249 284. AD 1914 (seed 1000): station at (146, 218) beside the factory and the boiler house chimney; textured brick and the canopy are visible.
- **Limitations:** No tracks or trains. The industrial landmarks cluster together in seed 1000 (nearest free lots).
## 2026-11-25 — Factory landmark (Gemini isometric-first sheet; sim lot)

- **Changed:** Gemini: one isometric view of a 19th-century brick textile mill, then a 2x2 orthographic sheet of the same mill with the chimney position spelled out per view (the first sheet had text labels, so Gemini was asked to redo it without). Sheet `landmark-factory.png`; model `landmark_factory` IoU 0.865 at 16 m (built first at 28 m: 40.6 m long, too big), 23.2 x 16.0 x 7.8 m. `Simulation.PlaceLandmarks` adds kind 3 from era 6: the nearest free 7x3 lot (non-corridor first), gate facing the centre. `MeshKit.Factory()` loads the model only (no fallback); `WorldView` draws it on its lot, adds it to the camera's landmark list and emits 10 large smoke puffs from the mesh's highest point.
- **Verified:** `tests/SimTests` ALL PASSED; all four seeds get landmarks `0+1+3+2`. Era-end populations: 1000 = 30 68 113 149 191 223 252 282; 16838 = 29 54 117 147 190 230 262 283; 40595 = 28 49 120 149 194 223 254 278; 72271 = 24 56 120 155 183 226 249 283. In game (seed 1000, AD 1793) the factory stands at (146, 206) among brick houses, with skylights and a tall chimney; chimney smoke was not visible in that shot. Free RAM 18.7-18.8 GB during the build.
## 2026-11-25 — Streets dug up for water mains (Fable review #3 item 6 / user item 4, render-only first step)

- **Changed:** `MeshKit.Trench()` (dark open trench, spoil heaps, a length of grey pipe, plank barriers). `WorldView.UpdateWaterMains` (from `UpdateGroundUse`): in the first 30 years of the Industrial era, road tiles sorted by distance from the centre count as "laid" in proportion to the years passed; the next four are shown dug up, aligned with the street. Render-only; refreshed whenever roads or era change.
- **Verified:** Game build succeeded. Debug print (removed): seed 1000 at year 262 a trench at (266, 170). `--focus=266,170` at AD 1793 shows the trench with pipe, spoil and barriers in a cobbled street between brick houses.
- **Limitations:** No workers dig; drain covers are not yet limited to piped streets.
## 2026-11-25 — Real demolition: families move out; rebuilt houses hold more (Fable review #3, item 5, part)

- **Changed:** `Simulation.Capacity(b)` = `HomeSize` + 2 for a renewed house once it is built; used by `AssignHomes`, `FreeHut` (which now also skips unbuilt huts), `RehouseCouple`, births and the housing want in `Plan`. `RenewOldHouse` first checks that other built homes have room for the family; if so the family moves out (`Home = -1`, `AssignHomes` rehouses them) before the house is pulled down, otherwise the renewal waits a year. Log text: "A family moves out and their old house is pulled down to be rebuilt larger". `AssignHomes` no longer sends someone to a spouse's or mother's home while it is unbuilt.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations: 1000 = 30 68 113 149 191 223 257 284; 16838 = 29 54 117 147 190 230 260 283; 40595 = 28 49 120 149 194 223 258 286; 72271 = 24 56 120 155 183 226 255 283; renewals 37/40/45/40 (unchanged, so families always found room). Game build succeeded. Not watched on screen.
- **Not done (Fable item 5):** an animated knock-down phase (walls shrinking before the rebuild) and salvaged stone.
## 2026-11-25 — StateHash covers more state (Fable review #3, item 8)

- **Changed:** `Simulation.StateHash` now includes each person's `Home` and `Job` and each building's `Built` and `Renewed` flags, so the determinism and save/load tests would catch a lost flag or assignment. No behaviour change.
- **Verified:** `tests/SimTests` ALL PASSED (determinism and save/load included); era-end populations unchanged from the previous run.
## 2026-11-25 — Church as the social centre (Fable review #3, item 3)

- **Changed:** `Simulation.ChurchDoor()` returns a point in front of the church tower (lot centre + yaw direction, half size + 1.2 tiles). With a church, elder funerals and the era celebration gather at the church door instead of the grave or village centre (log positions follow), and new graves go in a churchyard spiral around the church lot (free, walkable tiles only); before the church the old graveyard rules apply. `WorldView.InsideChurch` is kept, because the church lot can be laid over older graves.
- **Bug caught:** the first version used `Landmarks.Find(...) is { Kind: 0 }`, which matches the default record (Kind 0) when there is no church, so every village behaved as if it had one from the start (seed 8919 bread chain failed, early-era populations shifted). Fixed with `FindIndex`.
- **Verified:** `tests/SimTests` ALL PASSED after the fix. Era-end populations: 1000 = 30 68 113 149 191 223 258 285; 16838 = 29 54 117 147 190 230 260 281; 40595 = 28 49 120 149 194 223 256 283; 72271 = 24 56 120 155 183 226 250 286. Game build succeeded. A funeral at the church was not observed on screen.
## 2026-11-25 — Update clip 3 published on YouTube

- **Done:** 8 captioned scenes recorded in two halves (free RAM 18.8-19.1 GB, free commit 13.5-13.8 GB). Frame review: the ship-crowd scene showed only a few people and the lamp scene looked like daytime, so both were cut (lamps were already in clip 2). Final clip: 1:04, 5.4 MB, HEVC 720p + AAC, mean -22.8 dB: church in the graveyard, wells, fountain, house being rebuilt, water tower and heating plant (top-down), widened streets. Published publicly (not made for kids) as "DeskCiv update 3 – church, wells and fountains, water tower, district heating and rebuilt houses": https://youtu.be/1AzB4pZ_MtE. The description lists the changes since update 2.
- **Next clip due:** about 15:40.
## 2026-11-25 — Landmark lots owned by the simulation (Fable review #3, item 2)

- **Changed:** `Simulation.Landmark` record (kind 0 church, 1 water tower, 2 boiler house; tile centre, half sizes, yaw) and `Landmarks` list. `PlaceLandmarks()` at each New Year, deterministic scans with no random draws: church from era 4 on the free, walkable lot nearest the centre within 40 tiles (3x7 or 7x3; non-corridor land first, corridor land as a fallback), turned so the tower faces away from the centre; water tower from era 6 on the highest free walkable 3x3 within 45 tiles (minus a distance penalty); boiler house from era 7 on the free 3x3 nearest the tower (3-10 tiles away). `AddLandmark` marks the tiles `Building`, removes them from `RoadQueue` and clears trees, so villagers path around and nothing is built there. Save version 13 writes the list (9-12 still load; lots appear at the next New Year). Added to `StateHash`. `WorldSnapshot.Landmarks`; `WorldView` now draws church, tower and boiler from these lots instead of re-scanning every ground update (they used to jump). `Program.cs` prints `landmarks=` kinds.
- **Verified:** `tests/SimTests` ALL PASSED; all four seeds get church + tower + boiler (`0+1+2`; the first version without the corridor fallback and orientations gave only one landmark in seeds 16838 and 40595). Era-end populations: 1000 = 30 68 113 149 190 224 257 278; 16838 = 29 54 117 147 194 227 253 288; 40595 = 28 49 120 149 194 231 259 286; 72271 = 24 56 120 155 190 225 261 280. In game (seed 1000, AD 1938): church at (130, 182), tower at (138, 250), boiler at (138, 238); shots show the church spire and the tower top beside the boiler house.
- **Limitations:** Funerals and celebrations do not yet use the church (Fable item 3). The quay stays render-only.
## 2026-11-25 — Heating lowers firewood use (Fable review #3, item 4)

- **Changed:** `Simulation.UpdateWorld`: a hut's winter firewood burn is x0.7 in the Industrial era (coal stoves) and x0.35 in the 20th century (district heating); the hut wood stock target is unchanged.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations: 1000 = 30 68 113 149 192 225 257 286; 16838 = 29 54 117 147 190 227 260 265; 40595 = 28 49 120 149 191 222 256 284; 72271 = 24 56 120 155 182 219 250 284. Woodcutters are still built in all four seeds (the never-built check passed). The visible effect (fewer porters carrying wood in late winters, forest regrowth) was not checked on screen.
## 2026-11-25 — Ship crowd confirmed on screen; --shot-at option

- **Found:** `--shot` saves after 75 frames whatever `--duration` says, so earlier "ship crowd" screenshots were all taken before the event. A host trace (removed) confirmed the ship news is published at tick 2400.
- **Changed:** `Main`: `--shot-at=SEC` delays the screenshot until that many seconds have passed. `Simulation.Director`: the ship news is logged at the bank point where the crowd gathers (was mid-river), so the story camera frames the crowd.
- **Verified:** Seed 1000, start 127.132, `--shot-at=80`: headline "A trading ship sails up the estuary" and a crowd of villagers walking onto the bank; the ship itself is out of frame in that shot (camera looks inland). Log coordinates only; SimTests not re-run for this change (the previous run with the gathering passed).
## 2026-11-25 — Ship crowd timed to the ship (Fable review #3, item 1)

- **Changed:** The trading-ship gathering and its log line moved from `NewYear` to `Director`, at the 2400th tick of a ship year (240 s in, when the ship is in sight; it enters at 5% of the year), lasting 240 s instead of 150 s; the log line's importance is 3 (was 2), so a drought or sickness headline logged at New Year no longer wins over it.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations: 1000 = 30 68 113 149 192 225 254 287; 16838 = 29 54 117 147 190 227 257 275; 40595 = 28 49 120 149 191 222 256 285; 72271 = 24 56 120 155 182 219 254 272. A temporary trace (removed) showed the block running at year 127.133 (era 3) in game. Three screenshots near that moment still showed the sickness headline and no crowd in frame, so the on-screen result is NOT confirmed; possibly the first event after a `--start-year` load is treated as "first" by the camera, or the news selection keeps the older item.
## 2026-11-25 — Quay model from Gemini (isometric first); Fable review #3 received

- **Changed:** Following the user's tip, Gemini was asked for one isometric view of the quay first, then a 2x2 orthographic sheet "of exactly this same quay" (`landmark-quay2.png`). The sheet kept one design, though both side views still show the crane on the same side. Model `landmark_quay` rebuilt from it: IoU 0.636 (side views weak), 13.7 x 9.0 x 7.4 m; free RAM 18.9-19.0 GB. `--focus=187,125` at AD 1598 shows the quay on the village bank with stone facing, timber piles and the crane tower over the water; the crane carries a greenish smear left from the chroma key and the deck top is untextured. Kept for now; redo when image quota allows.
- **Fable review #3 (read-only):** bugs: the ship crowd gathers at New Year and leaves before the ship arrives (yf 0.05 + sail time); importance-3 events in the same tick pre-empt the ship shot; render-only landmarks re-pick their spot on every ground update and can jump; `UrbanizeField` does not remove the first house's tiles from `RoadQueue`; a house under renewal keeps a frozen `Warm`; `StateHash` omits `Built`, `Renewed`, `Home`, `Job`. Backlog: ship crowd timed to the ship; landmark lots owned by the sim (no new BType, save v13); church as the social centre; heating lowers wood burn; real demolition with families moving out and real extra capacity; water mains street by street; quay/factory/station lots with wander targets; harden `StateHash`.
## 2026-11-25 — Quay landmark prepared, model rejected; ChatGPT image quota used up

- **Changed:** `MeshKit.Quay()` (loads `landmark_quay`, low-poly fallback) and quay placement in `WorldView.UpdateWaterSupply`: from era 5 on the first walkable point of the village bank at the trading ship's anchor (`CenterX + 25`), front facing the water; added to the camera landmark list. The quay is shown only when an image-made `landmark_quay` model exists.
- **Rejected:** ChatGPT sheet `landmark-quay.png` (timber quay with treadwheel crane) built at IoU 0.769, texture coverage 0.56; in game the crane had smeared textures, broken faces and loose pieces floating over the water. The model files were moved out of `game/assets/models` and `dist` to the session scratchpad (`rejected/`), so no quay is drawn for now. The sheet stays in `assets-candidates`.
- **Quota:** ChatGPT says image generation is used up until 21:00 (2026-11-25). Gemini not tried yet today.
- **User suggestion (saved to memory):** ask ChatGPT/Gemini for an isometric view of the building first, then the 2x2 orthographic sheet of the same building, to get consistent views.
## 2026-11-25 — Parish church landmark (Fable review #2 item 7, render-only first step)

- **Changed:** `MeshKit.Church()` (loads `landmark_church`, procedural fallback). ChatGPT sheet `landmark-church.png` (stone parish church, nave + west bell tower with spire); the first sheet showed the long side in all four views, so ChatGPT was asked for a corrected set (narrow tower end front, east gable back). Model `landmark_church`: IoU 0.81, 10.3 x 19.9 x 24.1 m, long axis Z with the tower at +Z. `WorldView.UpdateWaterSupply` places one church from era 4 on the free, walkable 3x7-tile block nearest the centre, tower end turned away from the centre; the church joins the camera's landmark list. Graves under the church's walls are hidden (`InsideChurch`, 6 x 12.8 m half-size), and grave placement is refreshed when the church appears or moves.
- **Verified:** Debug print (removed): seed 1000 church at (130, 182) in AD 1224. `--focus=130,182` shots in AD 1224 and AD 1914: the textured stone church stands in the existing graveyard; after the fix no stones stick through its walls. Render-only; the simulation does not know the church (villagers can walk through it; funerals still gather at the grave).
- **Built:** one at a time; free RAM 17.8 GB, free commit 12.0-12.1 GB.
## 2026-11-25 — Rubble beside houses being rebuilt; overlay says "Rebuilding"

- **Changed:** `MeshKit.Rubble()` (heaped brick and plaster with broken beams). `WorldView.UpdateBuildings` places one rubble heap (up to four) beside every renewed house that is still a site. `Main.CurrentProject` shows "Rebuilding an old house N%" for a renewed site instead of "Building a home".
- **Verified:** Game build succeeded. Debug print (removed): seed 1000 at year 260.05 has a renewal at (196, 152), progress 0.20, rubble at (192, 150). `--focus=195,151` shows the scaffold of the house being rebuilt among Industrial brick houses; the rubble itself was hidden behind roofs. Render and overlay only.
## 2026-11-25 — Story camera visits town landmarks

- **Changed:** `WorldView.Landmark(n)` cycles through up to two each of the water towers, boiler houses, fountains and wells currently shown. `Main.NextShot`: every seventh shot (`_shotNumber % 7 == 3`) frames the next landmark at 1.6x close distance with a clear azimuth for 20 s, so viewers see the new water and heating buildings.
- **Verified:** Game build succeeded; a 100 s run at AD 1914 with the normal camera logged 0 errors. Not confirmed on screen that a landmark shot came up within that run.
## 2026-11-25 — Image-made district heating boiler house

- **Changed:** `MeshKit.BoilerHouse()` tries `LoadModel("prop_boilerhouse")`; `WorldView` uses the model's material and puts the chimney smoke at the mesh's highest vertex (`HighestPoint`) instead of a fixed offset. ChatGPT sheet `prop-boilerhouse.png` (1930s brick boiler house with a round brick chimney at a back corner); model `prop_boilerhouse` built at 14 m total height (the sheet's hall is wide relative to the chimney, so 24 m made a 21 m hall): IoU 0.752, 12.5 x 14.0 x 11.3 m. Free RAM 18.0-18.3 GB, free commit 12.1-12.5 GB.
- **Verified:** `--focus=152,236` at AD 1940 winter shows the textured boiler house (standing-seam roof, brick, arched steel windows) beside the water tower; the camera was too close to judge the chimney or smoke. Close up, a few window areas show grey untextured patches.
## 2026-11-25 — Wells and fountains placed on open ground

- **Changed:** `WorldView.UpdateWaterSupply` now tries eight spots around each storehouse (front corners, back corners, 9 m front/sides/back) and uses the first whose tile is `Free`; a store with no free spot gets none. Before, the fixed front-right spot often landed inside a neighbouring house (seed 1000: hidden at 169.6, 154.6).
- **Verified:** Temporary debug print (removed): seed 1000 well at (169.6, 145.4) in 3000 BC; fountains=2 in AD 1224. `--focus=169.6,145.4` shots: the image-made well (shingled roof, windlass) stands on the grass beside the store's stone pile in the Bronze Age; the carved stone fountain stands in the paved square in the Middle Ages. Both models are kept despite IoU ~0.75. Render-only.
## 2026-11-25 — Image-made water props (tower, fountain, well)

- **Changed:** `MeshKit.Well/Fountain/WaterTower` first try `LoadModel("prop_well" / "prop_fountain" / "prop_watertower")`; `WorldView` gives those multimeshes the model's own textured material (`SurfaceGetMaterial(0) ?? vertexColors`). New ChatGPT sheets `prop-watertower.png`, `prop-fountain.png`, `prop-well.png` and Hunyuan models: `prop_watertower` IoU 0.94 (7.8 x 17.0 x 7.9 m), `prop_fountain` IoU 0.746 (2.5 x 2.6 x 2.5 m; thin water jets lower the score), `prop_well` IoU 0.749 (2.5 x 2.7 x 1.8 m, texture coverage 0.55). Built one at a time; free RAM 18.1-18.7 GB, free commit 11.8-12.6 GB.
- **Verified:** `--focus=138,246` at AD 1914 shows the textured brick water tower with arched door and windows. The fountain and well were not caught on screen (`--look=Storehouse` ignores `--distance` and the houses hid them).
- **Limitations:** Fountain and well have low IoU; check them in game before keeping.
## 2026-11-25 — Clean water limits sickness; --focus camera option

- **Changed:** `Simulation.NewYear` sickness event: the share of villagers struck is 18% before the Middle Ages, 12% from era 4 (fountains) and 8% from era 6 (water tower and mains); the number of random draws is unchanged. `Main`: `--focus=x,z` keeps the camera on a world point (for test shots of props that are not buildings).
- **Verified:** `tests/SimTests` ALL PASSED with era-end populations identical to the previous run (sickness rarely kills, so the change is small). `--focus=144,246` at AD 1940 winter shows the water tower (brick shaft, grey tank) and the boiler house with its chimney beside it.
- **Limitations:** The `--focus` shot came out top-down at `--distance=60`. The water tower and boiler house are flat-coloured procedural meshes.
## 2026-11-25 — Heating by era, visual first step (approved infrastructure item 5)

- **Changed:** `SmokeShader` gets a `tone` uniform: dark coal smoke (0.42) in the Industrial era, light (0.8) in the 20th century, pale wood smoke before. `MeshKit.BoilerHouse()` (brick hall with windows and a 24 m chimney). From era 7, `UpdateWaterSupply` places one district-heating boiler house on a free tile 14 m from the water tower (first free of four directions); its chimney emits 8 large (2.2x) puffs, and only one occupied hut in four still shows hearth smoke.
- **Verified:** Game build succeeded; runs at 1823 winter and 1940 winter logged 0 errors. Neither the darker smoke nor the boiler house was clearly visible in the wide shots. Render-only; SimTests not re-run.
- **Limitations:** No coal deliveries or stoves in the sim; heating does not affect cold/wood demand yet.
## 2026-11-25 — Water supply by era, visual first step (approved infrastructure item 4)

- **Changed:** `MeshKit.Well()` (stone ring, posts, windlass, little gable roof), `MeshKit.Fountain()` (octagonal basin, column, upper bowl, water surfaces), `MeshKit.WaterTower()` (~17 m brick shaft, riveted tank, conical roof, window strips). `WorldView.UpdateWaterSupply` (called from `UpdateGroundUse`): from era 1 a well 7.6 m right / 4.6 m front of every built storehouse, replaced by a fountain from era 4; from era 6 one water tower on the highest free 3x3 tile block within 25 tiles of the centre (height minus a small distance penalty). Render-only (the sim has no water yet).
- **Verified:** Game build succeeded. A temporary debug print (removed before commit), seed 1000: 3000 BC wells=1; AD 1914 fountains=3, towers=1 at (138, 31.6, 246), about 100 m from the centre on a 31 m hill. None of the three props was clearly visible in the screenshots taken. SimTests not re-run (render-only).
- **Next:** pipes/sewers and a sim role for water (e.g. sickness lower with fountains/mains) would need tests.
## 2026-11-25 — Old central houses are pulled down and rebuilt larger (approved infrastructure item 3)

- **Changed:** `Building.Renewed`. `Simulation.RenewOldHouse()` at each new year from era 6: if no renewal is in progress, the built, not-yet-renewed house nearest the centre (within 80 m) is set `Renewed`, `Built = false`, `Progress = 0.2`, logged "An old house is pulled down and rebuilt larger" (importance 1); builders and porters finish it like any site, and its family stays on. Save version 12 writes the renewed building ids after the cosmetic stream (9-11 still load). `BuildingView.Renewed`; `WorldView` draws a renewed house 1.12x wider and 1.22x taller and swaps it to the other model variant. The fix also restored a missing line break after `WidenBusyStreets()` in `NewYear`. `Program.cs` prints `renewed=`.
- **Verified:** `tests/SimTests` ALL PASSED (determinism and save/load included). Renewed over a full run: 1000 = 39, 16838 = 41, 40595 = 45, 72271 = 40. Era-end populations: 1000 = 30 68 113 148 188 222 263 286; 16838 = 29 54 117 147 189 225 253 283; 40595 = 28 49 120 152 197 221 263 282; 72271 = 24 56 120 153 185 232 252 280. AD 1914 screenshot (0 errors): asphalt streets with sidewalks and lamps, the new goods depot, mixed house heights.
- **Limitations:** Not a real demolition (no rubble or moving out); the house becomes a scaffolded site in place. Capacity is unchanged; "larger" is visual only.
## 2026-11-25 — Update clip 2 published on YouTube

- **Done:** 8 captioned scenes recorded in two halves (free RAM 18.6-19.1 GB, free commit 12.5-13.0 GB). Reviewing frames showed that three scenes did not show what their captions claimed (storehouse piles barely visible; no ship or crowd in the "trading ship" scene, which showed a sickness headline; the fisher scene was weak), so they were cut instead of publishing misleading captions. Final clip: 1:00, 5.1 MB, HEVC 720p + AAC, mean -22.9 dB: Iron Age workshop models, widened streets with kerbs, Industrial sidewalks/gutters/drain covers, lamps at night, 20th-century goods depot. Published publicly (not made for kids) as "DeskCiv update 2 – wider streets, sidewalks, street lamps and new Iron Age workshops": https://youtu.be/03eNxgFVFeo. The description lists all changes since update 1 and marks piles, wheelbarrows and the ship crowd as "not shown in this clip".
- **Next clip due:** about 12:40 (3 hours).
- **Lesson:** `--look` scenes recorded with `--duration = seconds + 30` drift away from the state seen in an 8 s screenshot; check a frame of every scene before publishing.
## 2026-11-25 — Busy central streets widen to two lanes (approved infrastructure item 2)

- **Changed:** `Simulation.WidenBusyStreets()`, called at each new year from era 4: if fewer than 25 road tiles are queued, road tiles within 24 tiles of the centre whose wear cell is 90 or more are sorted by wear (ties by tile index); for up to six of them that are still one lane wide, the first free, walkable tile across the street is planned as road (builders lay it like any road). `StreetsWidened` counts planned tiles (not saved). `tests/SimTests/Program.cs` prints `widened=` in the full-run rows.
- **Verified:** `tests/SimTests` ALL PASSED. Widened tiles over a full run: 1000 = 119, 16838 = 154, 40595 = 118, 72271 = 121. Era-end populations: 1000 = 30 68 113 148 188 222 262 284; 16838 = 29 54 117 147 189 225 252 289; 40595 = 28 49 120 152 197 221 259 284; 72271 = 24 56 120 153 185 232 255 285. AD 1671 screenshot: a two-lane cobbled street with kerbs through the centre.
- **Limitations:** Widening uses free tiles only; it never demolishes (that is item 3). Wear comes from the cosmetic stream, so changing walking can change which streets widen, but not the economy's random draws.
## 2026-11-25 — Street lamps; Iron Age mill model

- **Changed:** `MeshKit.LampPost()` (cast-iron post with arm) and `MeshKit.Lantern()`. `WorldView.UpdateStreetLamps` (called from `UpdateGroundUse`): from era 6, on road tiles where `(x*7 + z*3) % 5 == 0`, a lamp stands 1.75 m toward the first non-road, non-water neighbour with its arm over the street. Lanterns use an unshaded material that `SetNightGlow` lerps from dark glass to warm light; a ground light pool (the additive glow disc, 1.3x) under each lamp is shown only at night. Approved infrastructure backlog item 1 is now complete (kerbs, sidewalks, gutters, drain covers, lamps).
- **Also:** ChatGPT sheet `mill-t1.png` (timber-framed thatched grinding house with open door, sacks and rotary querns); model `mill_t1` (IoU 0.862). Free RAM 18.7 GB, free commit 12.5 GB before and after. All tier-1 crafts now have their own models (woodcutter, fisher, gatherer, mill).
- **Verified:** Game build succeeded; AD 1787 `--look=Storehouse` at 22:00 shows a lit lantern; at 12:00 the same lantern is dark. 0 errors logged. Render-only; SimTests not re-run. `mill_t1` not yet viewed in game.
## 2026-11-25 — Street dressing in the ground shader; Iron Age gatherer model

- **Changed:** Terrain shader uniform `street_style` (set in `UpdateGroundUse`: 1 from era 5, 2 from era 6). Style 1 draws a stone kerb band where the road mask `use.r` is 0.43-0.53 (the soft road edge). Style 2 draws paved sidewalk slabs (1.4 m joints) at 0.42-0.63, a kerb at 0.6-0.67, a dark gutter at 0.66-0.71, and round cast-iron drain covers (rim + lid) on a 13 m grid where both the cell centre and the pixel are deep inside a road (`use.r` >= 0.95). Done in the shader so it follows the smooth road edge instead of tile squares. Approved infrastructure backlog item 1 (lamps still to do).
- **Also:** ChatGPT sheet `gatherer-t1.png` (round wattle hut, herb bundles, baskets, reed rack); model `gatherer_t1` (IoU 0.916). Built in the foreground; free RAM 18.6-18.8 GB, free commit 12.5-12.6 GB.
- **Verified:** Game build succeeded. AD 1787 (seed 1000): a street with pale slab sidewalks and kerb lines on both sides of the cobbles. Drain covers and the Renaissance kerb-only style were not caught on screen. Render-only; SimTests not re-run. `gatherer_t1` not yet viewed in game.
## 2026-11-25 — Approved backlog: towns that evolve (no code change)

- **Context:** The user asked whether the sim demolishes houses, replans or widens roads, and gives later roads sidewalks and drain covers. Current state: buildings are never removed; roads are one 4 m tile wide, laid from each new door to the network and never widened or realigned; road surfaces change by era (dirt, cobbles from the Middle Ages, asphalt in the 20th century); the only replanning is `UrbanizeField` (era 4+, a central field becomes four houses and a lane). No kerbs, sidewalks, drains or street lamps.
- **Approved order:** 1) era street dressing, render-only (kerbs from the Renaissance; sidewalks, drain covers, gutters and lit street lamps from the Industrial era); 2) widen the busiest central roads to 2 tiles from the Middle Ages; 3) demolish small old central houses and rebuild larger houses/terraces from the Industrial era. Added by the user: 4) water supply and drainage by era (wells, fountains/cisterns, aqueduct-like supply, water towers, mains and sewers); 5) heating by era (hearths, chimneys, stoves and coal deliveries, central/district heating).
## 2026-11-25 — Iron Age fisher model

- **Changed:** ChatGPT sheet `fisher-t1.png` (thatched timber-and-wattle hut, net-and-fish drying rack, eel traps); model `fisher_t1` (IoU 0.869, 8.3 x 5.5 x 6.6 m). Built in the foreground; free RAM 18.2-19.2 GB, free commit 12.7-12.8 GB. Bronze/Iron Age fishers no longer borrow the Stone Age model.
- **Limitations:** Not viewed in game yet. The net is see-through on the sheet, so it keys out partly; the rack appears on both side views.
## 2026-11-25 — 20th-century storehouse and Iron Age woodcutter models

- **Changed:** New ChatGPT sheets (green screen) `storehouse-t4.png` (mid-century brick-and-concrete depot with loading ramp) and `woodcutter-t1.png` (timber-framed thatched hut with a log lean-to); models `storehouse_t4` (IoU 0.894, 13.3 x 9.0 x 15.1 m) and `woodcutter_t1` (IoU 0.787, lowest so far: the sheet shows the lean-to on both side views). Built one at a time in the foreground; free RAM 17.2-18.3 GB, free commit 12.7-12.9 GB. Before, the 20th-century store borrowed `storehouse_t3` and Bronze/Iron Age woodcutters borrowed the Stone Age `woodcutter_t0`.
- **Also:** ChatGPT made a 1950s house sheet first; `hut-t4.png` already existed (built yesterday), so Chrome asked to overwrite it. The agent answered No via UI Automation and saved the new sheet to the scratchpad instead; the original `hut-t4.png` is unchanged (1,735,566 bytes).
- **Verified:** `--look=Woodcutter` at 2500 BC shows the new thatched woodcutter among Bronze Age houses. `storehouse_t4` not yet viewed in game.
- **Still borrowing Stone Age models in eras 2-3:** fisher, gatherer, mill (`*_t1` missing).
## 2026-11-25 — Villagers watch the trading ship come in (Fable review #2, item 6, second half)

- **Changed:** `Simulation.NewYear`: in a trading-ship year, before the log line, `Gather` (150 s) at the first walkable point on the village side of the river by the anchor (`CenterX + 25`), found by stepping 2 m at a time up to 120 m from the river centre, plus 4 m inland. Only children, elders and the jobless go; the camera holds on it like other gatherings.
- **Verified:** `tests/SimTests` ALL PASSED with the cosmetic-stream fix in place (it failed before that fix). Era-end populations: 1000 = 30 68 113 148 182 220 261 283; 16838 = 29 54 117 147 192 214 255 273; 40595 = 28 49 120 152 189 222 253 286; 72271 = 24 56 120 153 192 232 246 280. Game build succeeded. Not viewed in the running game.
- **Limitations:** In a ship year the bumper-harvest feast is skipped (gatherings are at least half a year apart unless forced).
## 2026-11-25 — Walkable vineyards tried again and dropped

- **Tried:** vineyard tiles as `TileKind.Field` on top of the cosmetic-stream fix. SimTests passed, but seed 1000's population collapsed in the last two eras (era-end 30 68 116 147 194 227 177 135 instead of ~255/278). Reverted. The cause was not investigated (vineyard tiles as Field may change urbanisation, hedges or road planning around them).
## 2026-11-25 — Separate cosmetic random stream; polders can start from the shore (Fable review #2, item 8 + root cause of the 40595 failures)

- **Changed:** `Simulation.Cosmetic` (`Rng`, own seed) now drives idle wandering targets and timers (`Wander`) and path wear (`UpdateWear`), so who walks where no longer shifts the economy's `Random`. Added to `StateHash` and saved: save version 11 (writes `Cosmetic.State` after the gathering fields; versions 9-10 still load and keep a fresh cosmetic stream). `ConnectRing` now tries up to 16 ring tiles (was 4), then falls back to `ShoreStart`: the first ring tile beside a road, or beside free land that a road can reach.
- **Root cause found:** a temporary diagnostic showed that in seed 40595 every polder slot ring is wholly sea (`Blocked`) plus three dike tiles, with no free or road tile on the ring, so `ConnectRing` never found a start and no polder was ever begun. Earlier passes depended on a road happening to touch the ring, which is why unrelated walking changes flipped this seed.
- **Verified:** `tests/SimTests` ALL PASSED (determinism and save/load included). Era-end populations: 1000 = 30 68 113 147 186 230 255 278 (polders 6/6); 16838 = 29 54 117 148 192 224 251 291 (6/6); 40595 = 28 49 120 153 188 223 258 288 (4/4); 72271 = 24 56 120 151 187 228 257 288 (8/8). The diagnostic code was removed.
- **Next:** the dropped walkable vineyards and the ship-watching crowd can be retried now.
## 2026-11-25 — Walkable vineyards tried and dropped

- **Tried:** Vineyard tiles as `TileKind.Field` (walkable) so pickers can reach the inner 2x2 (Fable review #2 bug "unreachable work spots"). `tests/SimTests` failed "full 40595: no sea was reclaimed" (third time this seed fails after a change to who walks where). Reverted.
- **Note:** Fable review #2 item 8 (a separate `Rng` for `UpdateWear` and `Wander`, save version 11) is the way to make such fixes safe; it reshuffles every seed once and needs a re-tune.
## 2026-11-25 — Night flag kept current while the wallpaper is covered (Fable review #2 bug)

- **Changed:** `Main`: `CurrentHour()` and `IsNight(hour)` helpers; when rendering is off (`_renderEnabled` false, e.g. a fullscreen app covers the desktop) `_Process` still sets `_sim.Night` every 3 s. Before, `Night` changed only in `UpdateSun`, which runs only while rendering, so a covered wallpaper kept day or night for hours.
- **Verified:** Game build succeeded; a short run logged 0 errors. The covered state itself was not exercised. SimTests not needed (no simulation code changed).
## 2026-11-25 — Dike workers spread over their tile (Fable review #2 bug)

- **Changed:** `Simulation.TryPolderWork` sends each worker to the ring tile centre plus a ±1.2 m offset from `p.Id` (3x3 pattern) instead of the exact centre, where all of them used to stand on one spot for 28 s.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations: 1000 = 31 55 119 151 196 225 261 281; 16838 = 30 49 115 152 184 227 250 284; 40595 = 30 68 118 141 184 225 247 291 (polders 4/4); 72271 = 25 57 110 152 187 226 259 283.
- **Limitations:** Not viewed in the running game.
## 2026-11-25 — Wheelbarrows from the Middle Ages (Fable review #2, item 3, first step)

- **Changed:** `MeshKit.Barrow()` (wheel built as a prism turned onto the X axis with `Turned`, tray, sides, legs, handles reaching back to the hands). `WorldView.WritePeople`: from era 4 (`_clothEra >= 4`) adult heroes carrying goods get a barrow (`_barrows`, shadowed) and their load is drawn 0.55 m further forward and 0.4 m lower, in the tray.
- **Verified:** Game build succeeded. A temporary debug build (every adult given a load, reverted before commit) at AD 1224 showed a woman pushing a barrow of logs, handles at her hands. No real porter with a barrow was caught in a normal shot.
- **Limitations:** Same barrow for eras 4-7 (no horse cart or lorry yet). Logs overhang the tray. The carry animation is unchanged.
## 2026-11-25 — Goods piles beside storehouses (Fable review #2, item 2)

- **Changed:** `BuildingView` gets `WoodPile`, `StonePile`, `SackPile`, `BarrelPile` (0-4, `1 + stock/40`, capped), filled in `SimHost` for built storehouses from `Building.Stock` (sacks = grain + flour + berries + fish + bread; barrels = wine + grapes). `MeshKit.GoodsPile(kind)`: log stack, stone blocks, sacks, hooped barrels. `WorldView.UpdateBuildings` draws one unit per level: logs along the left side, stone along the right, sacks and barrels behind (shadow-casting multimeshes `_piles`).
- **User-visible:** Stores show their stock: piles grow and shrink as goods come and go.
- **Verified:** Game build succeeded; `--look=Storehouse` at 617 BC shows a stone pile beside the store. Render-only (sim read-only); SimTests not re-run.
- **Limitations:** Piles use fixed local offsets and may overlap a neighbouring road or building on tight plots. The `--look=Storehouse` shot at AD 1224 was blocked by a watermill wheel in front of the camera (camera clearance only checks trees).
## 2026-11-25 — Update clip 1 published on YouTube

- **Purpose:** The user asked the loop to post a clip every 3 hours that states the improvements.
- **Done:** `tools/record-showcase.ps1` run twice (5 + 4 scenes, one at a time; free RAM 17.3-17.4 GB, free commit 12.8-12.9 GB throughout), joined with `ffmpeg -c copy`: 1:46, 9.1 MB, HEVC 720p + AAC, mean -23.1 dB, English captions on every scene. Published publicly in YouTube Studio (not made for kids) as "DeskCiv update 1 – droughts, dikes, vineyards, scaffolding and new medieval workshops": https://youtu.be/zQYC4vSZo9g. The description lists every improvement since the first video, with music/sound credits and the GitHub link.
- **Limitations:** Some scenes show their feature weakly (night glow is faint; the renovation scaffold is small in a wide shot). YouTube flagged that external links in descriptions need channel verification, so the GitHub link may not be clickable.
- **Next clip due:** about 09:55.
## 2026-11-25 — Middle Ages craft models (Hunyuan, run one at a time)

- **Changed:** New models from existing sheets: `woodcutter_t2` (built in the killed batch), `fisher_t2` (IoU 0.883), `winery_t2` (0.864), `gatherer_t2` (0.923), `mill_t2` (0.906), `storehouse_t2b` (0.834). All published to `dist` by `make_building.ps1`. Local repo only (Hunyuan models are not published).
- **How run:** The user asked for one job at a time with RAM watched. Each build ran in the foreground; free RAM stayed 17.0-17.2 of 31.8 GB and free commit 12.4-12.6 GB before and after each build. (A background batch plus a parallel game recording had been reaped by the harness for low memory; the commit limit, not free RAM, looks like the constraint.)
- **Verified:** `--look=Winery` at AD 1224: the new half-timbered winery with the grounded press in front. Mill shot did not show the mill clearly. Woodcutter, fisher, gatherer and the second storehouse not yet seen in game.
## 2026-11-25 — Seasonal vines and dike grass; showcase clip recorder

- **Changed:** `FieldShader` gets `green_tint` and `snow` uniforms, set from `UpdateSeason` (the same season tint and snow level as the land). Green vertex colours (vines, dike grass) take the autumn tint and are snowed over in winter; soil and furrows are unchanged. New `tools/record-showcase.ps1`: records each scene of a JSON list with `--stream-test` (game sound via the Dummy driver works), keeps the last N seconds (loading a late year eats part of `--duration`), burns in an English caption (Segoe UI), encodes parts with `h264_mf` and the final clip with `hevc_mf -hw_encoding 1` at 640 kb/s video + 64 kb/s AAC so ~100 s stays under the 10 MB browser-upload limit.
- **Verified:** Game build succeeded; `--look=Vineyard` at 596 BC winter shows snow-covered vines. A one-scene test clip: 8.0 s, HEVC 1280x720 + AAC, mean -23.9 dB, caption visible. The first full 8-scene clip was killed by the harness for low memory before finishing (together with a Hunyuan build batch that had finished `woodcutter_t2` and `fisher_t2`).
- **Limitations:** Grapes still hang on the snowy vines.
## 2026-11-25 — Door glow, smoke priority, reused people slots

- **Changed:** `MeshKit.Glow` is now a ground-level 16-segment fan, white at the centre and black at the rim, drawn with an additive, unshaded material at 40% (`WorldView`, `_glowMaterial`), at the hut door 0.06 m above ground. `SetNightGlow` hides the glow node when the night amount is 0.15 or less. Previously it was an opaque hexagonal prism: near-black by day and a solid orange block by night, floating beside the walls of the realistic house models. Smoke puffs (`MaxPuffs` 260) now go to bakeries and smithies first, then huts. `WritePeople` stops interpolating a slot whose position jumped more than 5 m (a death and a birth reusing the slot in one publish). (Fable review #2 bugs.)
- **User-visible:** No dark or orange blocks beside houses; at night a soft pool of firelight lies before occupied doors. Workshops keep their smoke in big winter towns.
- **Verified:** Game build succeeded. AD 1604 winter (seed 1000): the black block at noon is gone; the orange block at 21:00 became a soft glow (faint on snow). Runs logged 0 errors. Render-only; SimTests not re-run.
- **Limitations:** The glow is subtle on bright snow. The glow offset (2.72 m from centre) is not matched to each realistic model's door.

## 2026-11-25 — Camera stays for gatherings; ship crowd tried and dropped (Fable review #2, item 6)

- **Changed:** `WorldSnapshot.Gathering` (`GatherUntil > Tick`, set in `SimHost`). `Main` event shots now pick a clear azimuth (`ClearAzimuth`) and hold 75 s instead of 30 s while a gathering (funeral, feast, era celebration) is on, so the crowd has time to walk in.
- **Tried and dropped:** `NewYear` calling `Gather` at the river bank by the trading ship's anchor. `tests/SimTests` failed "full 40595: no sea was reclaimed" (the same marginal seed that failed with lethal sickness). Reverted; suite ALL PASSED again. Seed 40595 is the canary for any change to who walks where (wear draws share the main `Random`, see Fable review #2 item 8).
- **Verified:** Game build succeeded; SimTests ALL PASSED with the final code. The longer hold was not observed in a running game.

## 2026-11-25 — Herds and fishing boats grow with the village (Fable review #2, item 5)

- **Changed:** `Wildlife.SetVillage(fields, fishers, huts)` and `ApplyCounts`: from the New Stone Age the visible sheep are 4 + 3 per field (max 30), cows 1 + 1 per field (max 12), chickens 4 + 1 per two huts (max 22); from the Bronze Age the fishing boats equal the fisher huts (1 to 5). `WorldView.UpdateBuildings` passes the counts (built and unbuilt) each update. Previously all 30/12/22/5 appeared at once.
- **Verified:** Game build succeeded; a run at 6269 BC logged no errors. Herd sizes were not counted on screen. Render-only.
- **Limitations:** Agents are revealed in index order, so the first herd group fills before the second appears.

## 2026-11-25 — Smooth dike banks and leafy vineyard rows

- **Purpose:** In-game shots showed dikes as four stacked boxes (green stepped "ziggurats") and vineyard rows as solid green hedge boxes.
- **Changed:** `MeshKit.Building(Dike)`: one bank with a rounded trapezoid profile (8 profile points, muddy sea-side toe, grass slopes and flat crest, end caps); revetment stones and crest path kept. `MeshKit.Building(Vineyard)`: each wire span holds four vines (short trunk, two leafy prisms with hashed size and shade) instead of one box; grapes kept. `WorldView.UpdateEraLook` gives dikes the field material (vertex colour × CC0 soil photo detail) so the bank has texture.
- **Verified:** Game build succeeded. `--look=Dike` and `--look=Vineyard` shots at 617 BC (seed 1000): smooth textured grass bank; vineyard rows read as separate plants. Render-only; SimTests not re-run.
- **Limitations:** Small gaps remain between neighbouring dike segments where their ends do not meet. Vines do not change with the seasons.

## 2026-11-25 — Renovation scaffolds; winter pines no longer purple

- **Changed:** `WorldView.UpdateBuildings`: during a renovation wave a house or workshop stands in the building-site scaffold (`_sites`) while the wave passes its turn (within 0.03 of its hash, about 0.7 sim-year, ~20 real minutes) — Fable review #2 item 4. Realistic-tree shader (`WorldView.cs` ~188-204): the winter leaf colour (pale grey) used to multiply green needles by about 2.5 in red and blue, turning every realistic pine purple in winter (pre-existing bug); now a `frost` factor from the leaf colour's blue channel blends the crown toward dark needles with snow on the lighter parts.
- **User-visible:** Houses being rebuilt after an era change get scaffolding. Winter pines are dark green with snow instead of purple/pink.
- **Verified:** Game build succeeded. Seed 1000 start 167.8 at x200: AD 503, Middle Ages, one house in scaffold in the village centre. Purple pines seen at AD 496 winter before the fix; at 597 BC winter after the fix the pines are dark green. Render-only; SimTests not re-run.
- **Limitations:** Broadleaf realistic trees keep full crowns in winter (no bare branches).

## 2026-11-25 — The year's event shows on the land (Fable review #2, item 1)

- **Purpose:** `ActiveEvent` was in the snapshot but nothing drew it; a drought year could even show rain.
- **Changed:** `WorldView.UpdateSeason`: drought (event 1) yellows the ground tint (up to 70% toward parched) and dulls leaves from late spring to autumn and hides flowers; a harsh winter (event 2) brings snow from late autumn (up to 0.96 instead of 0.76) and whitens leaves. `UpdateBuildings`: crops stand at 62% height and brown in a drought, 122% and golden in a bumper year (event 4). Every occupied hut smokes from late autumn in a harsh winter. `Main.Weather` caps cloud cover at 0.3 (no rain) in a drought year between 10% and 80% of the year.
- **User-visible:** Drought summers look dry with stunted fields and no rain; harsh winters are snowier and smokier; bumper years have tall golden fields.
- **Verified:** Game build succeeded. Seed 1000 at 2976 BC (drought, summer): before the change it rained and the grass was normal green; after, no rain, yellower grass, shorter crops (compared side by side with a stashed baseline build). Harsh winter and bumper year were not captured in a screenshot. Render-only; `tests/SimTests` not re-run.
- **Limitations:** The effect on grass is moderate. The Fable suggestion to announce the harsh winter at the start of winter was not done (sim change).

## 2026-11-25 — Grounded mill and winery parts; building sites with scaffold

- **Purpose:** An in-game review at 633 BC showed a pale square slab with a post floating beside a house. It was the mill's animated quern: a square stone 0.86 m above ground in front of the mill, with nothing under it, which looked wrong next to the realistic models. The winery press plate floated the same way.
- **Changed:** `MeshKit.Turned(Basis)` rotates the mesh built so far. `MeshKit.Moving`: the mill part is now a round runner stone and bedstone on a round stone plinth that reaches the ground, with a handle. The winery part is the screw and bar over a round press plate and a vat of must standing on the ground. Both are built upright and laid into the `SpinFlat` frame; round parts look still while they spin. `MeshKit.Site()` (stakes with marking line, four scaffold poles with ledgers, timber stack, stone pile; sized for an 8 m plot). `WorldView.UpdateBuildings` draws a site (shadow-casting multimesh `_sites`, scaled by `Footprint/2`) under every unbuilt building except fields, vineyards, quarries, dikes and tide gates, from the New Stone Age; those buildings now rise from 3% instead of 18% of their height.
- **User-visible:** No floating slabs beside mills and wineries. Houses under construction stand inside scaffolding with materials beside them instead of appearing as a flat pale slab.
- **Verified:** Game build succeeded (0 errors). Screenshots at 633 BC and 2990 BC (seed 1000) show the quern standing on the ground. A temporary debug build drew the site on every hut to check its look (scaffold, line, timber and stone visible); the debug line was removed before commit. Render-only change; `tests/SimTests` not re-run (the simulation was not touched).
- **Limitations:** No unbuilt site was caught in a normal screenshot yet. The site props are flat-shaded low-poly, not image-made models. The scaffold is the same height for every building type.

## 2026-11-25 — Night rhythm tried and dropped (no code change)

- **Purpose:** Fable backlog item 4 (people go home at night).
- **Tried:** `Simulation.RestsNow` resting all children and elders plus 40% of working-age villagers at night: 3 failures (seed 8919 and others "bread chain never ran" in the night-only runs; seed 40595 fell to 106 people in the Industrial Age). Then children and elders plus the existing 20%: 4 failures (bread chain; seeds 1000 and 72271 "never built TideGate").
- **Result:** Reverted to `Night && p.Id % 5 == 0`; `tests/SimTests` ALL PASSED again. A night rhythm needs compensating work speed or batch sizes and a re-tune over the four full seeds, not a one-line change.

## 2026-11-25 — Visiting trading ship (decorative)

- **Purpose:** Fable backlog item 8, in a low-risk form: a full trading economy would have to pass the goods-conservation test, so this first version moves no goods.
- **Changed:** `Simulation.TraderVisiting` (pure function of era and time, no saved state): from the Iron Age (era 3), every third year (`(int)Year % 3 == 1`) between 5% and 45% of the year. `NewYear` logs "A trading ship sails up the estuary" at the anchor point (centre X + 25 on the river centre line), importance 2. `WorldSnapshot.TraderVisiting` is passed by `WorldView.SyncWorld` to `Wildlife.TraderVisiting`; `Wildlife.StepTrader` sails one boat at 2.2x scale from 175 m past the coast to the anchor at 3 m/s, swings gently at anchor, then sails out and hides. Logs "DeskCiv: trading ship arriving, anchor at X,Z" once per visit.
- **Verified:** `dotnet build` succeeded; `tests/SimTests` ALL PASSED. Seed 1000 at year 127.1: log printed the anchor 187,71; a 150 s shot there shows the large ship coming in, 0 logged errors.
- **Limitations:** No porters unload goods and no trade happens. The ship was seen approaching, not yet at anchor. A same-tick drought or surge outranks the ship's news line.

## 2026-11-25 — Renovation wave after era changes

- **Purpose:** Fable backlog item 7: the whole town used to switch style in one frame at an era change.
- **Changed:** Render-only, in `WorldView`. A third multimesh per building type (`_older`) keeps the previous tier's mesh and material when the model tier changes during play (not on the first frame, a model reload or a new world, which builds a new `WorldView`). For `RenovationYears` = 12 simulated years (about 6 real hours) each finished home, storehouse or workshop (`Renovates`: Hut, Storehouse, Bakery, Smithy, Winery, Woodcutter, Fisher, Gatherer, Mill) keeps the old model until its turn, chosen by a position hash, so the town is rebuilt house by house. Works (windmills, water mills, tide gate, dikes, quarry) and fields switch at once. No simulation or save change.
- **Verified:** `dotnet build` succeeded. A run from year 167.97 across the Middle Ages boundary, shot at AD 504, shows mostly Iron Age roundhouses with one new timber-framed house and the new `storehouse_t1` granary, 0 logged errors.
- **Limitations:** Old buildings use only the base shape of the old tier (no second variant). A house built after the era change is not tracked, so it may still show the old style until its hash turn. Builders do not visibly work on renovations.

## 2026-11-25 — No walking through house walls

- **Purpose:** Fable bug list: without a path (short wanders use a 300-node search limit, or no route), `Move` walked in a straight line through buildings.
- **Changed:** In the straight-line branch of `Simulation.Move`, a person stops (arrives) at the edge of a `Building` tile unless that tile is their destination or they already stand on a building tile.
- **Verified:** `tests/SimTests` ALL PASSED.
- **Tried and dropped:** Making sickness fatal for ill elders (age 55+, MaxAge pulled to within months) failed the suite ("full 40595: no sea was reclaimed"; the Middle Ages population fell to 167), so it was reverted. Sickness and cold stay mild.
- **Limitations:** Not watched in the running game; wanderers may now stop short more often near houses.

## 2026-11-25 — Dike breaches and news priority

- **Purpose:** Fable backlog: floods had become text-only once dikes saturated protection, and two events logged in one tick showed only the last.
- **Changed:** `Simulation.StrikeFlood`: from the Middle Ages (era 4+), with at least two finished dikes, a quarter of surges are great ones that break one dike (chosen by `Floods % dikes`, one extra random draw). The dike becomes an unfinished site at 40% progress, so porters re-deliver its wood and stone and builders repair it through the normal construction path; protection is computed after the breach. New events "A great storm surge breaks a dike and floods N fields" / "...; builders hurry to mend it", logged at the breach. `SimHost.NewsEvent` publishes the most important event logged since the last snapshot (latest on a tie), tracked by tick because the event list is capped at 64.
- **Verified:** `dotnet build` succeeded; `tests/SimTests` ALL PASSED. Surges held fell (seed 40595: 22 of 42, was 36 of 49).
- **Limitations:** A repaired breach was not observed directly (no test asserts that broken dikes finish again) and nothing was watched in the running game.

## 2026-11-25 — Gatherings: funerals, harvest feasts, era celebrations

- **Purpose:** Fable backlog item 5, so the village visibly comes together at moments that matter.
- **Changed:** `Simulation.Gather(x, z, seconds, force)` sets `GatherX/Z`, `GatherUntil` and `LastGathering` (gatherings at least half a year apart unless forced). While one runs, `Wander` sends children, elders and jobless non-porter, non-builder villagers within 90 m to mill about within 7 m of the spot (same random draws as before). Triggers: an elder's death (age 50+) holds a 70 s burial at the new grave with the event "The village gathers to bury one of its elders"; a bumper harvest holds a 120 s feast at the first storehouse ("A bumper harvest: the village holds a feast"); an era change gathers the village at the centre for 150 s. Save format is now version 10 with the four gathering fields appended at the end; version 9 saves still load. The fields are in `StateHash`.
- **Verified:** `tests/SimTests` ALL PASSED, including determinism and save/load. A first version that also pulled in idle workers failed ("seed 32676: bread chain never ran", seed 1000 fell to 132 people in the 20th century), so workers, porters and builders are excluded.
- **Limitations:** Not watched in the running game; the crowd may be small because only people without duties come.

## 2026-11-25 — Second house shapes, medieval crafts, seasonal workers, grave placement

- **Purpose:** `/loop` tick with image quota available again on ChatGPT and Gemini, then gameplay.
- **Changed:** New green-screen sheets in `assets-candidates/concepts/buildings/`: `hut-t0b` (Neolithic plank house), `hut-t1b` (Iron Age farmhouse), `hut-t2b` (jettied timber-framed town house), `hut-t3b` (Dutch stepped-gable brick house), `storehouse-t1` (Iron Age granary on stone posts), `bakery-t2`, `smithy-t2`, `woodcutter-t2`, `fisher-t2`, `winery-t2`, `gatherer-t2`, `mill-t2`, `storehouse-t2b`. Built with `tools/make_building.ps1` (run it from pwsh, not Windows PowerShell 5, whose Stop preference treats Hunyuan's stderr log as an error): `hut_t0b` (IoU 0.951), `hut_t1b` (0.945), `hut_t2b` (0.93), `hut_t3b` (0.949), `storehouse_t1` (0.906), `bakery_t2` (0.888), `smithy_t2` (0.855). Sheet download: ChatGPT image blob via an `<a download>` click, Gemini via its download button; both open Chrome's Save As dialog, filled by `finish-save-dialog.ps1` (session scratchpad). The localhost receiver is blocked by the page (fetch to 127.0.0.1 fails). Gameplay: workers whose job has nothing to do (winter, full stores) now lay roads and help with polder dikes (`Simulation.Choose`). `AddGrave` walks along its spiral until the stone is on walkable `Free` ground; `WorldView.UpdateGraves` hides stones on tiles later built over and refreshes when the list is full (it used to stop updating once `MaxGraves` was reached). `Simulation.TileAt` is public.
- **Verified:** `dotnet build` succeeded; `tests/SimTests` ALL PASSED (seed 72271 era-end populations 28 60 118 160 191 231 248 294).
- **Limitations:** The harness stopped the second build batch for low system memory after `smithy_t2`; `woodcutter_t2`, `fisher_t2`, `winery_t2`, `gatherer_t2`, `mill_t2`, `storehouse_t2b` are not built and must not be rebuilt until the user asks. None of the new models or the grave fix were viewed in the running game. The gatherer sheet has slight perspective in its side views.

## 2026-11-25 — Gameplay loop, first pass: pump windmills, event locations, project line

- **Purpose:** The user started a `/loop`: improve looks when ChatGPT/Gemini image quota is available, otherwise improve gameplay, and ask a Fable session for gameplay advice every 6 hours. The first Fable review (read-only) produced a prioritised backlog; these are the first items.
- **Changed:** `Simulation.Plan` no longer counts polder pump windmills as grinders (they grind nothing and take no workers), so villages keep building grinding mills and windmills after the first polder; pump count is a loop instead of a `FindAll` allocation. Drought and bumper-harvest events are logged at the first finished field, sickness at the first person it strikes, storm surges at the first flooded field or else the first dike (new `PlaceOf`; no extra random draws, so outcomes are unchanged), so the event camera films the place. `SimHost.BuildSpots` starts at a rotating villager, so the camera no longer favours the first 64 people. The overlay hides news older than one simulated year and shows the current project: "Draining the polder N%" (new `WorldSnapshot.PolderDrain`) or "Building a windmill N%" / "Marking out a home".
- **Verified:** `dotnet build` succeeded. `tests/SimTests` ALL PASSED; era-end populations moved slightly with the mill fix (seed 72271: 28 55 119 144 196 194 260 277). Overlay screenshots at years 30, 180 and 230 show the new lines in English.
- **Limitations:** The event camera itself was not watched in a running game. Remaining Fable backlog: winter/surplus workers doing dike and road work, gatherings (funerals, harvest feast, era celebration), day/night rhythm, floods that breach dikes, per-house renovation, trading ship; bugs: two events in one tick show only the last, graves placed by fixed offset can land on buildings, straight-line walks through buildings when no path is found, sickness and cold are almost harmless.

## 2026-11-25 — Recorded sound and music, English text, sound in streams, YouTube clip

- **Purpose:** The user asked for open-source sound in the app, and for the GitHub README, the YouTube title and all in-game text in English. Earlier the same day a 1:45 trailer was recorded (three `--stream-test` segments: year 0, 150, 300) and published publicly on YouTube at the user's request: https://youtu.be/JDI6xkTNL-4 (720p HEVC because the Chrome upload tool accepts files under 10 MB; it has no sound and its burned-in overlay is still Vietnamese).
- **Changed:** `game/assets/audio/*.dcsnd` (Ogg Vorbis under an extension the Godot importer ignores, 24 MB): `surf`, `wind`, `rain` loops, `bird_1..3`, `gull_1..2`, `music_0..7` (one Kevin MacLeod CC BY 4.0 piece per era, at most 5 min). Built by `tools/make_audio.py` (trim, seamless crossfade loops, loudnorm) and `tools/blender_ogg.py` (Vorbis via Blender's audaspace, because ffmpeg's native Vorbis encoder accepts only stereo and ignores the quality setting (about 220 kb/s) and installing `soundfile` was refused). Credits in `THIRD_PARTY_NOTICES.md`. `Soundscape` loads them with `AudioStreamOggVorbis.LoadFromBuffer`, falling back to generated sound for any missing file; rain sound follows the weather, birds sing only by day and not in rain, music plays a whole piece then waits 3-6 minutes. `Main` passes `Rain` and `Daylight` to `Soundscape`. All on-screen text is English: overlay ("AD 1914 · 20th Century · Spring", "Population · Food"), era names, seasons, BC/AD calendar with en-US numbers, event lines, history replay cards, tray menu and volume window (`tools/functional-test.ps1` window titles updated). `StreamOutput` now also sends the game's sound: an `AudioEffectCapture` on the Master bus feeds a named pipe (`\\.\pipe\deskciv-sound-<pid>`) as ffmpeg's second input; both inputs use wall-clock timestamps, output is CFR 30 fps, `aresample=async`; `.mp4` test targets are supported. While streaming, audio yielding to other apps is off. `stream-youtube.ps1` no longer passes `--silent` (the Dummy audio driver keeps speakers quiet). `README.md` rewritten (features, screenshots `docs/images/town-1914.jpg` and `stone-age.jpg`, requirements, streaming, credits). YouTube title/description changed to English in Studio.
- **Verified:** `dotnet build` succeeded. `tests/SimTests` ALL PASSED (event text changed only). A 36 s `--stream-test=sound-test.mp4` at year 300: H.264 + AAC stereo, mean -24.6 dB, max -6.7 dB; spectrogram shows surf from the start and music from about 20 s. Screenshots at 1600x900 show the English overlay. YouTube Studio saved the English title. Local commit 49c0310; public repo commit 9bcbd86 pushed to https://github.com/tuanduongvl/DeskCiv main (the worktree has no remote; push by URL).
- **Limitations:** The recordings were chosen by licence, length and spectrogram only; nobody has listened to them, so quality and mix levels need a human check. The surf loop is 16.7 s and may sound repetitive. Music lasts at most 5 minutes per piece. Screenshots in the README show Hunyuan-made models (the models themselves are still not published). The trailer was not re-recorded with sound or English text. Desktop (non-stream) playback with the new sound was not run on the real audio device.

## 2026-11-24 — Public GitHub repository and YouTube streaming

- **Purpose:** The user asked to push the repository publicly and stream the game to YouTube. Choices made by the user: public without Hunyuan models, merge to main.
- **Changed:** `feature/village-logistics` fast-forwarded into local `main`. `WorldView.TryTextured` skips pipeline characters whose files are missing, so the game runs without Hunyuan assets (falls back to baked low-poly villagers and Kenney buildings). Added `README.md`. Public repo https://github.com/tuanduongvl/DeskCiv was created from a clean orphan snapshot (worktree `D:/2026/101.DeskCiv-public`, branch `public` pushed as `main`): no history, and without `game/assets/models` Hunyuan models, `game/assets/characters/hq_*` and `art/characters-painted`; generated tree, sail and sluice-door cut-outs are kept. New `StreamOutput` (`--stream-key-file=<file>` or `--stream-test=<file.flv>`, `--ffmpeg=<path>`): the game copies its own frames to ffmpeg, which encodes H.264 on the GPU via Media Foundation (`h264_mf -hw_encoding 1`, 4.5 Mb/s, silent AAC) and sends RTMP to YouTube; the key is read from a file and never logged. `tools/stream-youtube.ps1` and `stream-youtube.cmd` start a separate off-screen 1280x720 world with `--no-save`.
- **Verified:** Public snapshot built and ran with 0 logged errors (screenshot). 40 s test recording: valid 1280x720 30 fps H.264+AAC FLV, correct picture, game CPU about 2% while encoding. Commit authors use the GitHub noreply address.
- **Not done:** A live YouTube stream was not started: the agent may not read or enter the stream key. The user must save the key in `%USERPROFILE%\.deskciv\youtube-stream-key.txt` and run `stream-youtube.cmd`; YouTube live streaming must be enabled on the account. The public repo has no open-source licence. Later public updates need copying changed files into the worktree and committing there.

## 2026-11-24 — Green-screen sheets, second house shape, chicken, roundhouse, harbour warehouse, quarry

- **Purpose:** Continue realistic models after ChatGPT's limit reset; the user suggested asking for green backgrounds so cut-outs are easier.
- **Changed:** `tools/prepare_grid_views.py` keys saturated green or magenta backgrounds on the dominant channel, clears enclosed gaps and despills the fringe; grey sheets cut byte-identically to before. `MeshKit.RealisticVariant` loads `<type>_t<tier>b`; `WorldView` has a second multimesh per type and sends every other building (position hash) to it, with the same recolouring material. New models: `animal_chicken`, `hut_t1` (Bronze/Iron Age roundhouse), `storehouse_t3` (stepped-gable harbour warehouse, also used for tier 4), `quarry_t0` (cut limestone outcrop, used for all tiers), `hut_t4b` (second 20th-century house, first green-screen sheet). `docs/building-pipeline.md` updated.
- **Verified:** Game built with 0 errors; the green-screen sheet cut cleanly (white walls kept, no fringe); shots of a 1962 street with both house shapes and of a Bronze Age village with roundhouses and the quarry, 0 logged errors. Outline match: chicken 0.89, roundhouse 0.94, warehouse 0.92, quarry 0.89, second house 0.91.
- **Limitations:** Only tier 4 homes have a second shape so far. Roundhouse side views lack the porch. Performance not re-measured (two extra small multimeshes per type, mostly empty).

## 2026-11-24 — Live reload of generated models

- **Purpose:** The user asked whether models can be updated while the app runs, without a restart.
- **Changed:** `WorldView` watches `assets/models` with a `FileSystemWatcher`; 2 s after the last file event, `ReloadModelsIfChanged` (called from `SyncWorld`) clears `MeshKit`'s model cache and re-applies buildings for the current era, moving parts (`ApplyMovingPart`), trees, hedges and felled trees (`ApplyTree`), and animals and boats (`Wildlife.ReloadModels`, which now also picks up `animal_chicken`). New `tools/publish_models.ps1` copies new or changed model files to `dist/DeskCiv/app/assets/models`; `tools/make_building.ps1` runs it at the end, so a running portable app shows a new model about 2 s after it is built.
- **Verified:** Game built with 0 errors. Live test: started the game, waited until frames were rendering, replaced `hut_t2.dctex` with a colour-swapped copy; the log printed "generated models reloaded" and the last frame showed the medieval houses with the new texture, 0 logged errors. The original texture was restored afterwards.
- **Limitations:** Characters (`assets/characters`), Kenney kit models and ground textures are not watched. A model type that did not exist at start (for example a first moving part for a building that had none) needs a restart. Cut-out cards and textures each allocate new GPU memory on reload; old ones are freed by Godot reference counting.

## 2026-11-24 — House colour variety and weather

- **Purpose:** The user agreed to more house variety and weather, and suggested reusing one house model with per-part colours ("dùng chung mẫu nhà chỉ đổi màu từng part").
- **Changed:** `WorldView.BuildingShader` replaces the realistic models' surface material (not mills, sluice or quarry): roof = upward faces in the upper half of the model, recoloured from 5 roof tints; walls get one of 6 tints, strongest on bright, low-chroma plaster texels, mild on stone, brick and timber; plus a small brightness jitter. The choice is a hash of the building position, so no extra instance data. Weather (`Main.Weather`): cloud cover drifts with the wall clock (periods 7-53 min), rain above about 0.8 cover; `--weather=clear|cloudy|rain` fixes it. Global shader parameters `cloud_cover`, `rain`, `daylight` drive drifting cloud shadows and wet darkening in the terrain and water shaders. Sun and sky turn orange at dawn and dusk, light and shadows soften under cloud, fog thickens for morning mist (5-9 h) and rain, and 3,000 GPU rain streaks fall in a 90 m box over the camera focus. The sun update now runs every 3 s.
- **User-visible behavior:** Neighbouring houses of one model have different roof and wall colours; the wallpaper has clear, cloudy and rainy spells, cloud shadows crossing land and river, golden mornings and evenings with mist.
- **Verified:** Game built with 0 errors; shots of a 1962 and a 1598 town (varied colours), rain, cloud shadows, dawn mist and a cloudy night, 0 logged errors after removing an editor-only call (`GlobalShaderParameterGetList`). 2560x1440 off-screen: 30 FPS, GPU 3.67 ms in rain and in clear weather, CPU 0.51-0.57%.
- **Limitations:** Clouds are shadows only; the sky behind is still a flat colour (rarely visible from this camera). No snow particles; rain shows only near the camera focus. Recolour masks are heuristic, so some timber or stone picks up a tint.

## 2026-11-24 — Photo textures on the ground

- **Purpose:** The user asked for ground textures ("Làm texture cho mặt đất đi").
- **Changed:** Downloaded seven CC0 1K diffuse textures from Poly Haven (source copies in ignored `assets-source/polyhaven/`, game copies in `game/assets/ground/*.dctex`, JPEG bytes loaded at runtime). `WorldView.TerrainShader`: grass and sand keep the biome and season colour but take detail from `leafy_grass` and `coast_sand_01` (photo divided by its own average, read from the smallest mip so it works in the Compatibility renderer's colour space); wear, yards and dirt roads use `dirt_aerial_02`; cobbled roads and town paving `cobblestone_floor_01`; 20th-century roads `asphalt_02`; quarry spoil `rocks_ground_02`. Each photo is sampled at two scales, the second rotated, blended by noise to hide tiling. Fields and vineyards use a new `FieldShader` with `dry_mud_field_001` detail. Tree crown cards are hidden when seen edge-on. `THIRD_PARTY_NOTICES.md` lists the textures.
- **User-visible behavior:** Grass, sand, earth, cobbles and asphalt show real photographic detail instead of flat colour.
- **Verified:** Game built with 0 errors; shots of a Neolithic village, a Renaissance field and town, a 1962 street and a wide view, 0 logged errors. 2560x1440 off-screen: 30 FPS, 0.52% CPU, 404 MB RAM, GPU 3.6 ms (was 3.45 ms).
- **Pitfall found:** dividing by a linear-space mean computed in C# made the ground twice as bright, because the Compatibility renderer samples `source_color` textures without the expected conversion; the shader now uses `textureLod(tex, uv, 12.0)` as the mean.
- **Limitations:** Fields keep their low-poly furrow shape. Snow, polder land and slopes use the same grass detail. Tiling can still be seen on large asphalt areas.

## 2026-11-24 — Walkers face their path, animal walk cycle, more human marks on the land

- **Purpose:** The user asked for animation, said villagers walked backwards ("nhân vật đi giật lùi") and wanted more human impact on the terrain.
- **Cause of backward walking:** `SimHost` aimed each walker at `TX/TZ`, the final destination, while people follow road paths through waypoints, so they often faced sideways or back. Checked that character meshes, rigs and the walk/carry clips face +Z and play forward (planted foot moves backward).
- **Changed:** `SimHost` turns each walker toward its actual movement since the last published frame, smoothed (teleports over 5 m fall back to the destination). `Wildlife` herd shader: diagonal leg pairs swing about the hips (lowest 45% of the mesh) while moving, a small body bob, and a slow grazing head nod when still. Terrain (`WorldView`): the road texture is now RGB: roads, worked ground around buildings, quarry spoil. Roads are dirt, then cobbles from era 4, then asphalt in era 7; yards are trampled earth, paved between later town houses; quarries leave a widening patch of broken stone. Felled trees leave a stump on open ground until they regrow (`MeshKit.Stump`). From era 4 fields get hedgerows of small oaks on two sides. Tree models gain a level crown card (top-view crown made from the side image) in an atlas texture, so trees no longer look like stars from above.
- **Verified:** Game built with 0 errors; shots of Bronze Age, Renaissance and 20th-century towns show yards, cobbles, asphalt, hedgerows, stumps and round crowns from above, 0 logged errors. 2560x1440 off-screen: 30 FPS, 0.57% CPU, 381 MB RAM, GPU 3.45 ms. Simulation code unchanged in this step.
- **Motion check:** New test flag `--burst=N` saves N extra frames 4 frames apart. A 15-frame burst at a Neolithic storehouse shows a baker walking toward the camera facing it, a woman climbing the steps with her back turned, and a carrier facing her direction of travel.
- **Limitations:** The animal leg walk was not watched in motion. Leg swing also bends any wool or belly below 45% height. Quarry rocks are still Kenney low-poly. Hedges skip only road and building tiles.

## 2026-11-24 — Realistic trees, animals, boats, sluice door, gatherer and industrial workshop

- **Purpose:** Continue the user's request for realistic generated models (Gemini sheets while ChatGPT's free limit was used up).
- **Changed:** Trees: `tree_pine`, `tree_broad`, `tree_birch` are three crossed cut-out cards each (`build_cutout_model.py --cross 3`, which now crops to trunk base and crown top) drawn with `WorldView.TexturedTreeShader` (sway, near dither, seasonal leaf colour); felled trees use the same pine. Animals `animal_cow`, `animal_sheep`, `animal_deer` and `boat` from Hunyuan; `Wildlife` shader multiplies a texture, and real boats sink 0.55 m. `tidegate_door` cut-out slides in the realistic sluice. `gatherer_t0`. One brick `workshop_t3` is shared by every craft workshop from the industrial era (`RealisticModel`). `build_hunyuan_model.py` gives upward faces that no view sees (boat deck, flat roofs) the median colour of well-seen surfaces at the same height.
- **User-visible behavior:** Realistic conifers, oaks and birches change colour with the seasons; Holstein cows, sheep, red deer and clinker-built sailing boats; industrial and 20th-century workshops are brick instead of thatch.
- **Verified:** Game built with 0 errors; in-game shots of the town, fields, shore and tide gate with 0 logged errors; review renders of all new models. 2560x1440 off-screen: 30 FPS, 0.52% CPU, 392 MB RAM, GPU 3.4 ms.
- **Limitations:** Chickens and gulls are still low-poly (Gemini limit reached before the hen). Deer outline match 0.76, boat 0.66 (thin rigging). Gravestones, fields, crops, dikes and quarries remain simple. Era tier 1 homes and later storehouses borrow other tiers. Converted house blocks repeat one model.

## 2026-11-24 — Denser town: homes cluster and inner fields become house blocks

- **Purpose:** The user approved making the town look dense: homes grouped around the centre, fields pushed outward.
- **Changed:** `Simulation.FindSite` (from era 4): homes score for being near the centre and near other homes (`HomesNear`, 20 m); fields and vineyards score away from homes. New `UrbanizeField` runs each new year from era 4: the inner field nearest the centre with 3+ homes within 32 m, no grain in stock, no reservations, nobody working on it right now, and only while grain is in surplus (`HasSurplus(Field)`) and at least 4 fields exist, becomes four 2x2 houses facing a new middle lane. The field object becomes the first house so ids stay valid; its idle farmers are released. Diagnostics `Urbanized`/`UrbanSkips` are printed by `tests/SimTests` as `urban=`.
- **User-visible behavior:** Medieval and later towns grow rows of houses where the oldest inner fields were; farmland moves to the edge.
- **Verified:** `tests/SimTests` ALL PASSED (including determinism, save/load and grain conservation). Era-end populations stay close to before (e.g. seed 72271: 28 55 119 144 199 192 255 278); buildings 163-222 versus 151-164; 6-16 fields urbanized per run. In-game wide shot of seed 1000 at 1962 shows new four-house blocks in the centre.
- **Rejected tries:** applying clustering from era 1 broke the early bread chain and polder tests; clearing field stock broke grain conservation; converting without a grain surplus dropped some seeds' population to 127.
- **Limitations:** Converted blocks look regular (grid of identical homes). Seed 1000 converts only 6 fields, so its outskirts still look sparse.

## 2026-11-24 — More, smaller homes in late eras

- **Purpose:** The user looked at the town and said there were too few houses ("Trông ít nhà quá nhỉ"). Homes held 7 people in the Middle Ages, 9 in the industrial era and 12 in the 20th century.
- **Changed:** `Simulation.HomeSize` is now 8 in era 0, 6 in eras 1-6 and 7 in era 7. Also: textured tree shader, wildlife texture support (earlier commits today).
- **User-visible behavior:** Later towns build more houses; the 20th-century town has roughly 40 homes for about 285 people instead of about 24.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations (4 seeds) stay close to before, e.g. seed 72271: 28 55 119 144 190 198 248 288 (before 28 55 119 144 192 208 257 283); total buildings 165-175 versus 151-164. A first try with 5 people per medieval home failed the bread-chain test (6 in era 1) and later cut Renaissance population to 110 in seed 1000, so it was rejected.
- **Limitations:** The town still looks sparse from far away because homes are spread between 18 m fields and a wide road grid; the layout itself was not changed.

## 2026-11-24 — Realistic image-generated buildings with animated parts; painted villagers restored

- **Purpose:** The user reversed the low-poly restyle: "Dùng chatgpt và gemini để generate ra các model theo phong cách thực tế. Ngoài ra các model cối xay gió, bánh xe nước... phải có animation".
- **Changed:** Painted villager textures restored (`stylize_character_textures.py --restore`) and faceted shading removed. New pipeline (see `docs/building-pipeline.md`): `tools/prepare_grid_views.py`, `tools/build_hunyuan_model.py`, `tools/build_cutout_model.py`, `tools/make_building.ps1`; `generate_hunyuan_shape.py` takes four views. `MeshKit.LoadModel`/`RealisticModel` load `game/assets/models/*.dcmodel` + `.dctex` with a textured material; `WorldView` clears the vertex-colour override for those meshes and uses a textured, alpha-cut moving-part shader. The windmill body now faces the same fixed sea-wind yaw as its sails. Test flag `--look=<BType>` aims the camera at a built building of that type. Review scene `tools/character-review/model.tscn`. Sheets saved in `assets-candidates/concepts/buildings/`.
- **Models:** windmill body + cut-out sails, watermill body + Hunyuan wheel (both turning), homes for tiers 0, 2, 3, 4, storehouse 0 and 2, bakery, smithy, winery, grain mill, woodcutter, fisher, tide gate piers (door still the procedural sliding panel). Workplaces without later-era models borrow the nearest tier.
- **Verified:** Game built with 0 errors; in-game shots of the windmill (sails turning, shadows), watermill (wheel on the water side), Neolithic village and medieval town with 0 logged errors; review renders of every model. `tests/SimTests` ALL PASSED. 2560x1440 off-screen, 259 people: 30 FPS, 0.55% CPU, 377 MB RAM, GPU 3.6 ms per frame (was 3.3 ms).
- **Limitations:** Trees, animals, boats, fields, dikes, quarries, the gatherer hut and the sluice door are still low-poly. Hunyuan dropped thin parts (fisher's net rack) and the tide gate outline match is only 0.76. Small moving parts of the mill, winery and smithy are still procedural boxes. Side textures may be mirrored where the generator's side views disagree. ChatGPT (until 20:59) and Gemini (until 17:44) free image limits were reached. Hunyuan3D licence excludes EU/UK/South Korea.

## 2026-11-24 — Villagers restyled as flat-colored low-poly to match the Kenney world

- **Purpose:** The user asked to unify the model style. Villagers had painterly, semi-realistic textures (faces, fabric detail) while buildings, trees and props are flat-colored Kenney low-poly.
- **Changed:** New `tools/stylize_character_textures.py`: for every `hq_*` character it samples the painted texture under each triangle (median of 24 interior points), blends touching triangles of similar color, snaps all triangle colors to a 10-color palette (slightly brighter and more saturated), and repaints the texture as one solid color per triangle. The painted originals are kept byte-identical in `art/characters-painted/`; `--restore` puts them back. `CharacterKit.SkinShader` now uses faceted normals from screen derivatives, for both bodies and tools. `docs/character-pipeline.md` gained the step.
- **User-visible behavior:** Villagers look like faceted toy figures with clean color patches (skin, hair, tunic, trousers, boots) instead of painted faces and fabric. Era clothing dye and the Stone Age hide look still apply.
- **Verified:** Game built with 0 errors. 960x540 close-ups of a medieval woodcutter and a carrier before and after, with 0 logged errors. Overviews of the industrial (1820) and 20th-century (1962) towns showed procedural brick and plain houses already in the same flat style.
- **Limitations:** Faces no longer show eyes or beards up close. Performance was not re-measured (same texture size and triangle count; the shader adds two derivative calls). `tests/SimTests` not rerun (no simulation change). Animals and boats are still procedural boxes, consistent in style but simple. Not every role was viewed close up.

## 2026-11-24 — Sun shadows, ground-contact shading, ground noise and swaying grass

- **Purpose:** The user said the scene did not look real and asked whether shaders or ray tracing would help. Agreed plan: cheap lighting and shader work on the existing GL Compatibility renderer, no ray tracing.
- **Changed:** `Main.cs`: the sun casts an orthogonal shadow map whose range follows the zoom (`_distance * 1.7`, 90-400 m); shadows turn off at auto-quality level 2+; fog slightly denser with aerial perspective; MSAA 2x in `project.godot`. Test flags `--no-shadows`, `--no-msaa`, `--shadow-splits=1|2`, `--shadow-size=N`; metrics CSV gains `gpu_ms` and `cpu_render_ms`. `WorldView.cs`: buildings, trees, falling trees, windmill and wheel parts and villagers cast shadows; terrain uses a FastNoiseLite texture for damp and dry patches and clumps (the old fine sine pattern is gone); grass, flowers, pebbles and reeds use a swaying `ScatterShader` that keeps the seasonal tint. `tools/build_kit_buildings.py` darkens kit surfaces within 1.6 m of the ground (not rotors or scatter); all kit models rebaked.
- **Important finding:** In the Compatibility renderer a sun with shadows enabled lights several times brighter than the same sun without shadows. `UpdateSun` scales sun energy by 0.35 when shadows are on and lowers ambient more by day, so the lit grass matches the old brightness while shadows read clearly. AgX and ACES tone mapping washed the image out in this renderer and were removed.
- **User-visible behavior:** Buildings, trees and people have sun shadows; grass and flowers move in the wind; meadows show broad colour variation; building bases look grounded.
- **Verified:** Game built with 0 errors. Before and after 960x540 shots of the era-0 camp, an era-1 village and an era-4 town, plus a night town shot, all with 0 logged errors. At 2560x1440 off-screen with 259 people: 30 FPS, 0.5-0.65% CPU, 382 MB RAM, GPU frame time 3.3 ms (about 10% of the 33 ms frame) versus 2.5 ms without shadows; 4-split shadows cost 5.4 ms and were rejected. `nvidia-smi` and Windows GPU counters could not attribute load to the game, so the in-engine GPU timer was used. `tests/SimTests` not rerun because no simulation code changed.
- **Limitations:** A single shadow map is soft-edged when zoomed far out. When the sun is behind the camera, shadows hide behind objects. Shadow brightness compensation was tuned by pixel samples at noon and 4 pm only.

## 2026-11-24 — Props, rocks and ground scatter from Kenney's CC0 Nature Kit

- **Purpose:** Continue replacing primitive models with open-source assets.
- **Changed:** `tools/build_kit_buildings.py` also reads `assets-source/kenney/nature-kit` (colors mapped by material name to the game palette) and writes `prop_*.dckit` and `scatter_*.dckit`. `MeshKit.TryKit` places a baked prop inside a procedural model, with the old geometry as fallback. Camp hut fire ring, camp storehouse bowl, camp fisher dugout canoe, camp woodcutter log stack and stump, camp and later quarries (natural rock outcrop), dike stone revetment, and grass, flower and pebble scatter now use Nature Kit pieces. Added `LICENSE-Kenney-Nature-Kit.txt` and a notice row.
- **User-visible behavior:** Stone Age camps and quarries look natural; meadows have Kenney grass, flowers and pebbles.
- **Verified:** Game built with 0 errors. In-game 960x540 shots reviewed the era-0 camp, shore, an era-1 village quarry and an era-4 town. `tests/SimTests` ALL PASSED. Grass was shrunk after the first shot showed it too large.
- **Limitations:** Performance was not re-measured; scatter triangles are similar to before except flowers (146 vs about 84 per cluster, 2,200 clusters). The dike was not viewed close up. The earlier "Chrome blocked downloads" diagnosis was wrong: kenney.nl's download endpoint returns HTTP 503 after one download (rate limit), still failing 10+ minutes later. At the user's request a Chrome user policy `HKCU\Software\Policies\Google\Chrome\AutomaticDownloadsAllowedForUrls\1 = [*.]kenney.nl` was added; it may make Chrome show "managed by your organization". Survival, Castle, City, Watercraft and graveyard kits are still missing.

## 2026-11-24 — Buildings, rotors and trees from Kenney's CC0 Fantasy Town Kit

- **Purpose:** The user asked to improve the primitive 3D models using open-source assets.
- **Changed:** Added `tools/build_kit_buildings.py`, which composes kit pieces into `game/assets/buildings/*.dckit`. `MeshKit.LoadKit` loads them. Work buildings and storehouses use kit models for tiers 0-4, homes for tiers 1-3, and windmills, water wheels and all three tree kinds use kit parts. Added `THIRD_PARTY_NOTICES.md`. The source zip and extraction are in the git-ignored `assets-source/kenney/`.
- **User-visible behavior:** Buildings have real walls, doors, shutters, gabled roofs and chimneys, changing material by era. Windmill sails, water wheels and trees are more detailed.
- **Verified:** Game built with 0 errors. In-game shots reviewed eras 1, 3 and 5 plus a polder. With 281 people at 2560x1440 off-screen: 30 FPS, about 0.5% CPU, 317 MB RAM, and 3-7% GPU in 10 samples. See the commit for the `tests/SimTests` result.
- **Limitations:** Animals, boats, tents, dikes, the tide gate, the windmill tower, round Neolithic huts, year-2000 homes and storehouses, and the factory are still procedural. Chrome stopped further automatic downloads from kenney.nl, so Nature, Survival, Castle, City, Watercraft and Cube Pets kits were not obtained. OpenGameArt refuses automated fetches, and Quaternius downloads go through itch.io, which was not used.

## 2026-11-24 — Stone Age camp and land reclamation from the year 500

- **Purpose:** The user asked for no clothes or houses in the first era, only a few tents, and Dutch-style sea reclamation with windmills from about the 5th-6th century.
- **Changed:** `MeshKit.CampBuilding` adds hide tents and open work spots. Era 0 is capped at 32 people, hides laid roads, farm animals and sailing boats, and draws villagers with bare arms and legs under a simple animal-hide wrap. `Polder` in `Model.cs` and the polder code in `Simulation.cs` add ring dikes, wind pumps, draining and new farmland from era 4. `WorldMap` keeps base and reclaimed heights. The terrain shader reads a height texture so land rises live. Added `--view=X,Z,DISTANCE`. Windmills keep their classic look and sails face the south-east. Rebaked `hq_laborer` with a harvest clip. Save format is now 9, so older saves are set aside and a new world starts.
- **User-visible behavior:** A run opens with a small tent camp. From about year 500 villagers build dikes into the sea, windmills drain the enclosed water, and the land turns into fields.
- **Verified:** `tests/SimTests` ALL PASSED before the final hide-wrap and animal-gating edits, which are render-only: four full runs each reclaimed 4-8 polders and ended at 277-294 people. In-game 1120x630 shots reviewed the camp, close-up villagers, a medieval polder and a later polder landscape, with 0 logged errors.
- **Limitations:** The polder is raised land rather than ground below sea level. The landward edge of a polder has a jagged mud patch. Draining was seen at finished stages, not watched continuously. The portable build with 275 people at 1120x630 ran at 30 FPS, 0.5-0.9% CPU and 356 MB RAM with 0 logged errors; the test stopped it during its run, so a clean exit was not rechecked. Performance was not re-measured at 2560x1440.

## 2026-11-24 — Hand tools for miner, fisher, farmer and builder

- **Purpose:** Make more jobs readable on screen.
- **Changed:** `tools/rig_ual_character.py` builds a pickaxe for the miner and one-handed rod, sickle and hammer props. The fisher's work clip is now `Farm_Watering`. Rebaked `hq_miner`, `hq_fisher`, `hq_farmer` and `hq_builder`, and rebuilt the portable folder.
- **User-visible behavior:** These workers show their own tool while working.
- **Verified:** Clip sheets for all four roles show the tool in the hand. The game and portable build ran with 0 logged errors.
- **Limitations:** Fingers do not close around one-handed tools. The fishing line is not modeled. Children are still scaled adults. Bakers have no tool.

## 2026-11-24 — Era looks, generated audio, auto quality, Windows startup and portable folder

- **Purpose:** Finish the remaining product items from the agreed requirements.
- **Changed:** `MeshKit.Retint` recolors all work buildings by era, and `CharacterKit` dyes clothing by era. `Soundscape.cs` now generates sea, wind, birds and one music piece per era. New `AudioYield.cs` fades DeskCiv out while another app makes sound. `Main` lowers render scale when FPS stays low and adds `--restart-every=SEC` for testing. The tray has a "Chạy cùng Windows" toggle, off by default. `tools/package_portable.ps1` builds `dist/DeskCiv` (ignored by git) with the engine and .NET runtime; its saves stay in its `data` folder.
- **User-visible behavior:** Towns and clothes change through the eras, sound is richer and yields to other apps, and the app can run from a portable folder through `DeskCiv.cmd`.
- **Verified:** Game built with 0 errors. A year-1986 shot shows modern buildings and dyed clothes. 8 forced world restarts plateaued near 385 MB. With 274 people at 2560x1440 off-screen: 30 FPS, 0.6% CPU, 322 MB RAM, about 5% GPU from few samples. The portable build ran from its bundled runtime with no errors. In real wallpaper mode its window is a child of Progman, not topmost, at 30 FPS and 0.5% CPU.
- **Limitations:** Nobody has listened to the audio. Audio yielding and the startup toggle were compiled but not exercised, so the registry was not changed. Auto quality was not triggered under real load. The portable folder is not a single exe and was not tried on a second PC.

## 2026-11-24 — Stable population through the year 2000

- **Purpose:** Remove the boom-and-famine waves in eras 6-8.
- **Changed:** `game/src/Sim/Simulation.cs`. Harvests, mill and bakery batches and carry loads grow by era. Trades have worker caps, so processors no longer drain farmers. Births slow near the era's limit and need a larger food reserve. Homes hold more people from era 4. Streets and sites may use more land. A tide gate can replace a dike on a crowded bank.
- **User-visible behavior:** Towns grow steadily to nearly 300 people instead of collapsing late in a run.
- **Verified:** `tests/SimTests` ALL PASSED. Four full runs with a day-night cycle ended at 282-296 people and built every building type.
- **Limitations:** Only four seeds were run for the full 336 years.

## 2026-11-24 — Era architecture, visible surges, history replay and new world

- **Purpose:** Make era changes visible and complete the run loop.
- **Changed:** `MeshKit.EraBuilding` adds timber, stone, brick and concrete versions of homes and storehouses, plus a factory for the smithy from the Industrial era. `WorldView` swaps these models and raises the water during a surge. `Simulation.History` records each era and is saved. `Main` plays a history replay, default 120 seconds, then starts a new world. `--replay-seconds=N` shortens it for testing. Save format is now 8, so older saves are set aside and a new world starts.
- **User-visible behavior:** Houses change with the era. Water rises during storm surges. After the year 2000, the camera circles the town with one card per era, then a new world begins.
- **Verified:** `tests/SimTests` ALL PASSED, including history for all eight eras in four full runs. A 1120x630 run from year 336 showed the modern town and an era card, then restarted at 20 people and year 0 with no logged errors, at 30 FPS and about 0.5% CPU.
- **Limitations:** Only homes, storehouses and the smithy change by era, so other buildings keep thatch in the year 2000. People do not change clothes by era. The surge rise was not reviewed on screen. RAM rose from about 389 MB to 456 MB after the world restart and was not tested over several restarts. Population still dips in eras 6-8. Real audio and audio yielding, automatic quality, Windows startup, portable export, a 300-person performance test and a direct wallpaper-mode check remain.

## 2026-11-24 — Eight-era progression through the year 2000

- **Purpose:** Replace the single Neolithic era with the agreed eight eras and keep villages alive for a whole run.
- **Changed:** `Eras` in `game/src/Sim/Model.cs` holds names, calendar (10,000 BCE to 2000), population caps (45 to 300) and building unlocks. `Simulation.cs` advances eras every 42 years, keeps street corridors clear so roads can grow, bounds site-search pathfinding, staffs each core trade first, moves workers from surplus trades to shortages, and raises field yields by era. `SimHost` and `Main` show the era and calendar. `tests/SimTests` adds four full runs and `diag <seed> <years> [mixed]`. Save format is now 7, so older saves are set aside and a new world starts.
- **User-visible behavior:** New worlds start in the Old Stone Age with only basic buildings, then unlock farming, crafts, water works, windmills and the tide gate. The overlay shows the current era and year.
- **Verified:** `tests/SimTests` ALL PASSED: 12 worlds for 50 years and 4 worlds for all 336 years with a day-night cycle. Each full run built every building type. Era-end population reached 200-240 by era 5-6, and final population was 89-230.
- **Limitations:** Later eras do not change how buildings or people look yet. Population still dips in eras 6-8 and does not reliably reach 300. No end-of-run replay or new-world restart exists yet.

## 2026-11-24 — Economy recovery, craft effects and storm surges

- **Purpose:** Fix the population collapse before adding later eras, and give wine, tools, dikes and the tide gate a real effect. Also records commit `17298b1`, which lays vineyards along the ground slope.
- **Changed:** `game/src/Sim/Simulation.cs`. Workers stop when their output pile is full and help carry goods. Mills and bakeries hold larger input buffers. Trees last longer and woodcutters settle near forest. One in five villagers rests at night instead of two in five. Wander pathfinding is bounded, and paths to blocked tiles are skipped. Tools speed work, wine raises births, and yearly storm surges damage riverside fields unless dikes and a tide gate hold them. Save format is now 5, so older saves are set aside and a new world starts.
- **User-visible behavior:** Villages grow larger and rarely starve. Event text reports storm surges and whether the dikes held.
- **Verified:** `tests/SimTests` ALL PASSED on 12 worlds for 50 years, with final population 68-155 (previously 43-132) and no world out of food. The game project built with 0 errors.
- **Limitations:** The surge has no visual yet, because the water level does not rise on screen. The change was not viewed in the running game. Population is still below the 200-300 target at year 50. Seven later eras, history replay, real audio, automatic quality, Windows startup and portable export remain.

## 2026-11-23 — Coastal water, trees, reeds, animals and boats

- **Purpose:** Complete the requested order of models, then ground, trees and animals.
- **Changed:** Added `game/src/Wildlife.cs` for sheep, cows, chickens, deer, gulls and sailing boats. `WorldView` now has a wave and shoreline-foam water shader, salt-grass and beach coloring, reeds, and three wind-swayed tree kinds. `MeshKit` has the new meshes.
- **User-visible behavior:** The sea moves and foams at the shore, trees sway, animals wander near the village, gulls circle, and boats sail the estuary.
- **Verified:** The game built, and wide and close in-game shots were reviewed at 1120x630. See the commit's test result for `tests/SimTests`.
- **Limitations:** Animals and boats are decorative and do not affect the simulation. Still not done: the seven later eras, history replay, real recorded audio and audio yielding, automatic quality, Windows startup, portable export, LOD or VAT, flood gameplay for dikes and the tide gate, uses for wine and tools, and a 300-person performance test.

## 2026-11-23 — All villager roles use textured characters

- **Purpose:** Finish the character models before ground, trees and animals.
- **Changed:** Generated T-pose concepts in ChatGPT for woman, laborer, builder, baker, fisher, miner and elder. Added `tools/build_roles.ps1`, baked `game/assets/characters/hq_*`, and routed every adult, woman, elder and child to a textured model. Workers now stand on field plates.
- **User-visible behavior:** No old block villagers remain among people drawn near the camera. Children use smaller laborer and woman models.
- **Verified:** All seven clip sheets were reviewed. The game built and ran at 1120x630 with 56 people: 30 FPS, 0.70% CPU, 304 MB RAM.
- **Limitations:** Children are scaled adults. Fisher and elder work clips are a folded-arms idle. Miners use the axe mesh rather than a pickaxe. Farmers, bakers and fishers have no hand tools. Not measured at 300 people.

## 2026-11-23 — Estuary map and animated working buildings

- **Purpose:** The user asked for a finished app with animated works, an estuary so windmills make sense, then ground, trees and animals, without further approvals.
- **Changed:** `WorldMap` now has a sea to the east and a widening river mouth. Added Windmill, Watermill, Vineyard, Winery, Smithy, Dike and TideGate, plus Grapes, Wine and Tools. Added bank placement, moving parts (`MeshKit.Moving`, `MovingShader`) and `game/debug/BuildingPreview`. Save format is now 4, so older saves are set aside and a new world starts.
- **User-visible behavior:** New worlds start at a river mouth. Sails, water wheel, millstone, wine press, smith's hammer and tide gate move once built.
- **Verified:** `tests/SimTests` passed on 12 worlds for 50 years, and the game built. An in-game wide shot shows the estuary with dikes. The off-screen preview shows all new building models.
- **Limitations:** Buildings are procedural low-poly, not image-generated. Average population is lower than before the estuary change (roughly 45-110 at year 50 versus up to 160). Wine and tools are produced but not yet used by villagers. Dikes and the tide gate have no flood gameplay yet. Only the Neolithic era exists.

## 2026-11-23 — Farmer added through the character pipeline

- **Purpose:** The user asked the agent to generate images through ChatGPT in Chrome and continue on its own. The agent chose the farmer as the next role. Woodcutter integration is commit `1845b7e`.
- **Changed:** Added `assets-candidates/concepts/farmer-tpose/` and `game/assets/characters/hq_farmer.*`. The prepare, build and rig scripts now take role and version. Hats use rigid head weights and a recolored brim. Added `tools/character-review/clips.*` for off-screen clip sheets. `WorldView` now supports several textured roles through `TexturedRole`. Updated `docs/character-pipeline.md`.
- **User-visible behavior:** Adult male field workers and gatherers use the straw-hat farmer. Hoe and gather work use the bent-over harvest clip. Their old hoe, sickle and basket are no longer drawn.
- **Verified:** Hunyuan shape is watertight with 361,588 faces. Body is 2,000 triangles. The off-screen sheet shows idle, walk, carry, sow and reap. Game built with 0 errors, `tests/SimTests` passed, and a 1120x630 in-game shot shows the farmer walking in the village.
- **Limitations:** Sowing and reaping were not captured inside the game, only on the clip sheet. The farmer has no hand tools. A small dark patch remains on one side of the hat brim. Performance was not re-measured. Women, children, elders and other jobs still use old models. The user has not reviewed the farmer.

## 2026-11-23 — Woodcutter v2 integrated into the game

- **Purpose:** The user asked to commit, then put the approved woodcutter in the game. Checkpoint commit is `2273ad3`; the user then asked to commit this entry's work as well.
- **Changed:** `tools/rig_ual_character.py` exports `game/assets/characters/hq_woodcutter.*`. `CharacterKit.Textured` loads its own mesh, texture and 22-joint rig. `WorldView` draws adult male woodcutters with it and shows the axe only while chopping. `MeshKit.Tree` has a taller trunk and higher canopy so the swing is visible. `Main` waits longer for `--watch` shots. Added `game/debug/WoodcutterPreview`.
- **User-visible behavior:** Adult male woodcutters use the new textured model for idle, walk, carry and chop. All trees look taller. Other villagers, including female woodcutters, keep the old models.
- **Verified:** Game built with 0 errors and `tests/SimTests` passed before the tree mesh edit; the tree edit is render-only and tests were not re-run after it. A real in-game screenshot shows the woodcutter chopping at a trunk. With 88 people in a 2560x1440 window: 30 FPS, 0.55% average CPU, 291 MB RAM.
- **Limitations:** GPU use and 300 people were not measured. No LOD yet. Carried goods still use the old load offset and were not reviewed on the new carry clip.
- **Incident:** The user saw the screen taken over during this work. No Godot process remained when checked. The likely cause is the agent's own 2560x1440 `--windowed` measurement, which covers a same-size monitor for 75 seconds. This does not confirm or disprove the earlier wallpaper always-on-top report; the user should test by launching `start-deskciv.cmd` directly.

## 2026-11-23 — Commit checkpoint before woodcutter integration

- **Purpose:** The user asked to commit the current work before integrating the woodcutter.
- **Changed:** `.gitignore` now excludes the downloaded Quaternius packs (`assets-source/quaternius-ual/`, 166 MB), generated Hunyuan outputs (`assets-candidates/hunyuan3d/`, about 50 MB) and `__pycache__`. Concept images, tools, docs and the logistics work are tracked.
- **User-visible behavior:** No change from this entry.
- **Verified:** `tests/SimTests` printed ALL PASSED and the game project built with 0 errors before the commit.
- **Limitations:** Ignored assets exist only on this machine. The packs can be downloaded again from the Quaternius itch.io page, and Hunyuan shapes can be regenerated from the tracked concepts with a fixed seed.

## 2026-11-23 — Documented the approved character pipeline

- **Purpose:** The user approved woodcutter-v2 and asked to record the process for future characters.
- **Changed:** Added `docs/character-pipeline.md` with steps, ChatGPT T-pose prompt, commands, acceptance checks and pitfalls. Moved the review scene and GIF script from the temporary scratchpad to `tools/character-review/`. Added a pointer in Claude's project memory.
- **User-visible behavior:** None in the app.
- **Verified:** Files were written and copied. The copied review project was not re-run from its new location, and the documented process has not yet been repeated on a second character.
- **Limitations:** Scripts still hardcode woodcutter paths, the axe and adult-male joint heights, so the next role needs them parameterized. Nothing was committed.

## 2026-11-23 — Regenerated woodcutter in T-pose and re-rigged (v2)

- **Purpose:** The user saw the v1 body as warped and asked to regenerate in T-pose. Measurement showed two causes: v1 auto-fitted joints were asymmetric (hips 17 cm forward, zigzag spine), and its lowered arms did not match the skeleton's horizontal T-pose.
- **Changed:** Saved the user's ChatGPT sheet to `assets-candidates/concepts/woodcutter-tpose/`. Added `tools/prepare_tpose_views.py` and `tools/build_tpose_character.py` (straight spine, mirrored limbs, measured on the dense mesh). `tools/generate_hunyuan_shape.py` and `tools/rig_ual_character.py` now take a version name. `build_hq_character.py` gained `MASK_LEFT_PROPS`. Strike contact is now the last frame before the hand reverses. Output is in `assets-candidates/hunyuan3d/woodcutter-v2/`, using `raw/side-as-right.glb`.
- **User-visible behavior:** None in the app. The running game still uses the old villagers.
- **Verified:** Hunyuan produced a watertight 344,930-face shape. Final body is 2,000 triangles, 1.80 m tall, axe 178 triangles. Blender export succeeded, and Godot rendered idle, walk, carry and all 30 chop frames. Body stays upright in the reviewed frames, both hands are on the handle, left wrist error is 0.003, and the blade lands flat against a test trunk.
- **Limitations:** The hand path at contact is still diagonal (0.766 horizontal, 0.643 vertical) because the source clip swings from overhead; only the final approach and blade are level. Fingers do not curl around the handle. The axe is held but not tuned for walk and carry. Texture coverage is 85.4%, with the rest filled from neighboring pixels. Not tested at game camera distance, not integrated, no LOD or VAT, no 300-person measurement. The Hunyuan regional license limit still applies. The always-on-top mitigation is still unconfirmed by the user.

## 2026-11-23 — Woodcutter rigged to CC0 skeleton with horizontal two-handed chop

- **Purpose:** Replace the rejected custom rig with existing tools and meet the user's horizontal-chop requirement.
- **Changed:** Extracted the user-downloaded Quaternius UAL1 and UAL2 packs into `assets-source/quaternius-ual/`. Added `tools/export_hq_landmarks.py` and `tools/rig_ual_character.py`. The Blender script fits the UAL skeleton to the 2,000-triangle woodcutter, binds with Automatic Weights, uses `Idle_Loop`, `Walk_Loop`, `Walk_Carry_Loop` and `TreeChopping_Loop`, attaches a 178-triangle axe to `hand_r`, and bakes the left arm onto the handle with Blender IK and Damped Track. Output: `assets-candidates/hunyuan3d/woodcutter-v1/rigged/woodcutter.glb` and `rig-report.json`; review images in `review/woodcutter-chop.gif` and `review/woodcutter-chop-sheet.png`.
- **User-visible behavior:** None in the app. The running game still uses the old villagers.
- **Verified:** Blender export succeeded and Godot loaded and rendered all 30 chop frames from the GLB. At contact the swing direction is 0.991 horizontal and 0.136 vertical, and the blade edge reaches a test trunk. Both hands are on the handle in the reviewed frames.
- **Limitations:** The left wrist stays 0.115 units from its solver target on every frame, and fingers do not curl because finger bones are excluded from deformation. Idle, walk and carry were only checked before the final axe orientation, and the axe is not yet tuned for those clips. Not tested at game camera distance, not integrated, no LOD or VAT, and no 300-person performance measurement. The always-on-top mitigation is still unconfirmed by the user.

## 2026-11-23 — Blender ready, waiting for CC0 animation packs

- **Purpose:** Replace the custom rig with existing tools.
- **Changed:** Extracted Blender 5.3.0 to ignored `.tools/blender` after matching the winget SHA-256 from an official mirror. Cloned Make-It-Animatable v2 to `.tools` for inspection only and did not install it. Created `assets-source/quaternius-ual/download/`.
- **User-visible behavior:** None in the app.
- **Decision:** Do not use Make-It-Animatable here because its pipeline references an Auto-Rig Pro fork and a gated Mixamo-derived dataset with no stated license. Planned path: Quaternius CC0 skeleton and clips, Blender automatic weights, then a horizontal axe swing authored in Blender.
- **Blocked:** Scripted download from itch.io did not work and a further attempt was denied by the permission system. Do not retry it. The user must download `Universal Animation Library[Standard].zip` and `Universal Animation Library 2[Standard].zip` into that folder.
- **Verified:** `blender.exe --version` reports 5.3.0. Rigging and animation have not been tested.

## 2026-11-23 — 2,000-triangle woodcutter test, custom rig stopped

- **Purpose:** Test the Hunyuan woodcutter at game budget beside the current model.
- **Changed:** Added `tools/build_hq_character.py` and review scene `game/debug/CharacterCompare`. Output is in `assets-candidates/hunyuan3d/woodcutter-v1/game` and `review`. The running app does not use it.
- **User-visible behavior:** None in the app.
- **Verified:** Body has 2,000 triangles, a 1024 texture and 12 test clips, and looks much closer to the concept than the current villager at close and game distance.
- **Limitations:** The custom rig and chop animation are not acceptable: arms do not grip the axe, and the swing is vertical while the user requires a horizontal felling swing. At the user's direction, stop hand-writing rigging and animation. Next candidate pipeline: Blender, Make-It-Animatable or AccuRIG, Quaternius Universal Animation Library 2, Godot retargeting, OpenVAT or an MIT VAT plugin, and meshoptimizer. None has been installed or tested. Model-weight licenses still need checking.

## 2026-11-23 — Hunyuan3D shape test for approved woodcutter

- **Purpose:** Test local image-to-3D from the approved ChatGPT concept.
- **Changed:** Saved concepts in `assets-candidates/concepts/woodcutter-approved/`. Added `tools/prepare_concept_views.py` and `tools/generate_hunyuan_shape.py`. Installed pinned Hunyuan3D-2 commit `f8db630`, an isolated Python environment and model cache under ignored `.tools/`. Outputs are in `assets-candidates/hunyuan3d/woodcutter-v1/`.
- **User-visible behavior:** None in the app.
- **Verified:** Hunyuan3D-2mv Turbo ran on the RTX 3060 in 11-20 seconds with 5.91 GB peak VRAM. `raw/side-as-right.glb` is the better candidate: watertight, 418,168 faces, correct rope and pouch sides. Shaded preview reviewed.
- **Limitations:** Shape only, with no texture, retopology, rig or game test. Hands and face are soft, the rope and pouch are fused to the clothing, and the 270-degree view shows a duplicated pouch shape. The Hunyuan license excludes use, distribution and display in the EU, UK and South Korea, which matters before any public release.

## 2026-11-23 — Start image-first character concepts with Codex

- **Purpose:** The user rejected the Python-generated Codex blocks and wants orthographic and isometric character images first, at a polished city-builder level.
- **Changed:** Added `docs/character-concept-prompt.md`. Started Codex CLI image generation for one original woodcutter in `assets-candidates/codex-concepts/woodcutter-v1/`.
- **User-visible behavior:** None in the app.
- **Verified:** No images were produced. The PATH Codex CLI 0.130 failed because `gpt-5.5` is rejected. The desktop app's Codex 0.171 ran with `gpt-6-luna` and `gpt-5.6-terra`, and both reported no built-in image tool in `codex exec`, although the feature flag is enabled. Codex wrote a failure `REPORT.md` and a local `AGENT_MEMORY.md` in the concept folder.
- **Limitation:** The next attempt must use an interactive Codex or ChatGPT session with image generation. No API key was used. Concepts are images only; no 3D conversion or integration should start until the user approves a design.

## 2026-11-23 — Mitigate wallpaper covering the primary monitor

- **Purpose:** The user reported DeskCiv as always on top and taking over one monitor.
- **Evidence:** While it happened, the window was still a child of Progman without a topmost style. Z-order tests with normal windows could not reproduce it. The suspected cause is GPU-driver direct scanout of an OpenGL window that exactly covers one monitor; this is unconfirmed because Win32 window-order checks cannot see it.
- **Changed:** `DesktopHost` makes the window one pixel shorter than the monitor. `TrayIcon` treats an external close as a graceful quit. Added `stop-deskciv.cmd`, `tools/stop-deskciv.ps1` and `tools/zorder-test.ps1`.
- **User-visible behavior:** DeskCiv should remain behind normal windows. If it does not, `stop-deskciv.cmd` saves and quits without using the tray.
- **Verified:** Window is 2560x1439 at 30 FPS. Z-order, functional and desktop probes passed. The stop command exited gracefully. Mouse test passed twice after one earlier run gave inconsistent wheel results that did not recur. The app was deliberately left closed so the user can test it.
- **Limitation:** Only the user's physical display can confirm the fix. If it fails, try the ANGLE renderer or another presentation path next.

## 2026-11-23 — Delivery dispatcher, worker roles, asset prompt

- **Purpose:** Commit the road/villager/mouse version, build the next logistics step, and prepare a fair external asset comparison.
- **Changed:** Commit `c3df393` is on branch `feature/village-logistics`. After it, uncommitted changes add `Role` (porter, builder, worker), a priority-based delivery dispatcher, role assignment, open-land building sites with road links, save format 3 with version 2 loading, test checks, and `docs/asset-generation-prompt.md`.
- **User-visible behavior:** Porters wait near storehouses and carry goods by urgency and distance. Builders handle construction and roads. Workers stay at their workplaces unless urgent deliveries need help. The village can open new streets when roadside land runs out. The existing world continues.
- **Verified:** Simulation tests pass for 12 worlds. The real version 2 save loaded at year 0.37 with 21 people. At 2560x1440 with 104 people over 60 seconds: 30 FPS, 0.56% CPU, 3.8% average and 3.97% peak GPU, 275 MB RAM. Functional, mouse and desktop probes passed.
- **Limitations:** Logistics changes are not committed. Performance is unmeasured at 300 people. Schools, inns, visible stock at buildings and longer production chains are not built. The asset prompt has not been run in Codex or Gemini.

## 2026-11-23 — Mouse pan and zoom on the wallpaper

- **Purpose:** The user asked to pan and zoom with the mouse.
- **Changed:** `TrayIcon` adds a pass-through low-level mouse hook; `Main` adds manual camera handling and extra metric columns; `DesktopHost`, `Win32` and `WorldView` support it. Added `tools/mouse-test.ps1`. Raw Input was tried first but delivered no events in the Godot process.
- **User-visible behavior:** With the pointer on the primary monitor's visible desktop, the wheel zooms toward the pointer and middle-button drag moves the map. Ctrl+wheel stays with Windows icon sizing. Mouse events are never blocked. The automatic camera resumes after 60 seconds without wheel or middle-drag input.
- **Verified:** `tools/mouse-test.ps1` passed with injected input: zoom in/out, pan, input ignored over another window, director resume, clean exit.
- **Limitations:** Not tested with a physical mouse, touchpad gestures or remote-desktop tools. An earlier test run showed unexplained zoom values once; the instrumented rerun was correct. Changes are not committed.

## 2026-11-23 — Tile roads, 300-person cap, original villager style

- **Purpose:** Start the logistics-style rebuild and test a clearer visual style. The user set the population target to about 200-300.
- **Changed:** `Simulation` and `Model` use a 4-unit tile grid, building footprints, doors, A* paths, planned and built roads, road labor, and save format 2. `MaxPeople` is 300. Added `tools/bake_villagers.py`, which generates original villagers, tools and 12 clips. Rebuilt building models and carried goods in `MeshKit`; updated `SimHost`, `WorldView`, `CharacterKit`, `Main`, `PoseSheet` and tests. The camera now moves closer and searches for an unobstructed angle.
- **User-visible behavior:** The village grows along dirt roads, buildings face roads, workers wear job-specific clothes and use visible tools and loads. A previous version 1 world is set aside as `world.sav.bad` and a new world starts.
- **Verified:** Simulation tests pass, including no overlapping buildings, doors on roads and the population cap. At 2560x1440 with 110 people over 60 seconds: 30 FPS, 0.48% CPU, 4.56% average and 9.53% peak GPU, 275 MB RAM. Functional test and desktop probe passed. The app was relaunched, started a new world and kept the old one as `world.sav.bad`.
- **Limitations:** Offer/demand logistics, specialist training, inns, visible building stock and longer production chains are not built. Work animations were reviewed in stills. The camera can still choose a poor woodcutter shot. These changes are not committed.

## 2026-11-23 — First local version committed

- **Purpose:** The user asked to save the current state as a version.
- **Changed:** Added `.gitignore`, initialized a local Git repository, and created the first commit with tag `v0.2-neolithic`. `.tools/`, generated build files and `measurements/` are not tracked. Nothing was pushed.
- **User-visible behavior:** None. The running app is unchanged.
- **Verified:** Git status and tracked-file list checked after commit. No new app tests were needed because code did not change.
- **Limitation:** A fresh clone must download Godot 4.7.2 .NET into `.tools/` before `start-deskciv.cmd` works.

## 2026-11-23 — One-monitor mode and animated visual test scene

- **Purpose:** The user asked for nicer low-poly models that show activity, and for the app to use one monitor because the seam hid the center. This replaces the earlier two-monitor decision.
- **Changed:** `DesktopHost` now covers and evaluates only the primary monitor. Added `tools/bake_characters.py`, `assets-source/quaternius-cube-world` (CC0), baked files in `game/assets/characters`, `CharacterKit`, and review scene `game/debug/PoseSheet`. Updated `WorldView`, `MeshKit`, `SimHost`, `Main`, `Simulation`, and test scripts.
- **User-visible behavior:** Four animated character models with tools and work clips for chopping, hoeing, mining, sowing, gathering, fishing, building and kneading. New thatched huts, wheat, grass, flowers, stones, chimney smoke, falling trees, and a closer camera that visits work areas with periodic overviews.
- **Verified:** Simulation tests pass. At 2560x1440 with about 100 people: 30 FPS, 0.48% CPU, 3.82% average and 4.05% peak GPU, 267 MB RAM over 60 seconds. Functional test passed. Desktop probe had 0 hung samples. Blank-desktop click worked in 3 of 4 final runs; in the other, Chrome stayed foreground and the cause is not established.
- **Limitations:** Work clips are generated from the rig and reviewed mostly as still poses, not in motion. A close-up of chopping beside a tree was not captured because the camera fell back to the overview. Up to 320 nearby people use full models; the rest use simple shapes. Medieval MegaKit and Universal Animation Library are not integrated. Portable export must include the custom `.dcmesh` and `rig.bin` files.

## 2026-11-23 — Brighter night and test speed slider

- **Purpose:** The user wanted visible nights and a way to speed up the world for testing.
- **Changed:** `Main` lighting, `TrayIcon` speed slider, `SimHost` adjustable speed with a 70 ms step budget per tick, `tools/functional-test.ps1`.
- **User-visible behavior:** Night follows the real clock but stays dimly lit. The tray volume popup has a Tốc độ slider from x1 to x500. Speed is not saved and returns to x1 on each start. The overlay shows speed above x1.
- **Verified:** Night screenshot reviewed. Functional test passed, with x100 measured at about x88 using 5-second metric samples while holding 30 FPS. Desktop probe: 0 hung samples in 16, blank click handled in 12 ms.
- **Limitations:** High speeds are capped by the step budget, so a large city may not reach the selected speed. x500 was not measured. The user's own DeskCiv instance (started 18:49) was left running and must be restarted to be sure it has this build.

## 2026-11-23 — Fix desktop hang and busy cursor

- **Purpose:** The user could not click desktop icons and saw a busy cursor while DeskCiv ran.
- **Evidence:** Windows flagged the DeskCiv window as hung from about 5 seconds after launch, and in some launches it held the foreground. Message pings still answered, so the earlier hit test missed the fault.
- **Changed:** `DesktopHost` detaches from Explorer's input queue, disables window input, hands foreground back whenever it receives it, and signals message-loop liveness each frame. `project.godot` sets `no_focus`. Added `tools/desktop-input-probe.ps1`.
- **User-visible behavior:** DeskCiv should no longer block the desktop or show the busy cursor.
- **Verified:** Five launches had 0 hung samples and 0 foreground samples, with a real click on blank desktop handled in 30 ms. A 2-minute run had 0 hung samples in 113 and no busy cursor. The functional test passed.
- **Limitations:** The fixes were applied together, so the exact contribution of each is unproven. Automated clicks targeted blank desktop only, not opening an icon; the user must confirm that.

## 2026-11-23 — Milestone 2 Neolithic vertical slice built

- **Purpose:** Replace dummy characters with a complete Neolithic settlement. Stop for user review before milestone 3.
- **Changed:** Added engine-free simulation in `game/src/Sim/` (`WorldMap`, `Model`, `Simulation`), `SimHost`, `MeshKit`, `WorldView`, rewrote `Main`, extended tray and settings. Removed prototype `Crowd.cs` and `Terrain.cs`. Added `tests/SimTests`, `tools/screenshots.ps1`, `start-deskciv.cmd`. Measurement scripts now use `--fresh --no-save`.
- **User-visible behavior:** A tribe of 20 founds a riverside village. People age, marry, have children, eat, keep huts warm and die. Goods are physically carried through berries, fish, wood, stone and grain to flour to bread. Four seasons, real-clock day/night, worn paths, graves, drought, harsh winter, sickness, bumper harvest, migrants, a slow event camera, a Vietnamese overlay with tray toggle, autosave every 5 minutes and on exit.
- **Verified:** `dotnet run -c Release` in `tests/SimTests` passes: determinism, save/load continuation, goods conservation, and 12 worlds for 50 simulated years in day, night and mixed patterns, none extinct and all completing the era. Desktop at 5120x1440, year 40, about 75 people: 30 FPS, 0.47% CPU, 2.71% average and 5.02% peak GPU, 252 MB RAM over 60 seconds. Functional test passed. Save and resume passed end to end; the test save was removed.
- **Limitations:** No real-time 42-year run. Performance not re-measured with a large late-era city. Windows-shutdown save path is implemented but not exercised. Audio remains placeholder and not heard; yielding to other apps' audio is not built. Models are procedural; CC0 assets are not used yet. Food surplus can grow large, so balance needs tuning. After the Neolithic the world stays in that era. Auto quality, Windows startup, portable export, NVIDIA overlay toast and history replay remain open.

## 2026-11-23 — 1-hour soak cancelled by user

- **Purpose:** The user observed no RAM growth and said the measurement is not needed.
- **Changed:** Stopped the soak with a normal window close. No code changed.
- **Verified:** The run stopped at about 222 seconds at 30 FPS, RAM 227 MB to 233 MB (peak 234 MB).
- **Limitation:** The 1-hour criterion is waived by the user and was not measured. Run a longer soak once real simulation exists. Milestone 1 is ready for user review; do not start milestone 2 without go-ahead.

## 2026-11-23 — Milestone 1 prototype built, 1-hour soak in progress

- **Purpose:** Prove the desktop-wallpaper host and performance budget before building the simulation.
- **Changed:** Added Godot 4.7.2 .NET in `.tools/` (SHA-512 verified), the project in `game/`, and `tools/measure.ps1` and `tools/functional-test.ps1`. Results go to `measurements/`.
- **User-visible behavior:** Renders a generated valley with 2,000 dummy characters behind desktop icons across both monitors. Tray left-click opens volume sliders and mute; right-click offers pause and quit. Generated wind and sparse chord are placeholders.
- **Verified:** 60-second run at 5120x1440: 30 FPS, 0.49% CPU, 3.07% average and 6.7% peak GPU 3D, 228 MB RAM. Functional test passed: icon hit test, tray popup, volume save and bus level, mute, stop at fullscreen and recovery, clean exit.
- **Limitations:** 1-hour soak not finished when this entry was written. Audio was tested through bus levels with a dummy driver, not by ear. Lighting is currently simple with no shadows, so budgets will need re-measuring as content grows. Auto quality, audio yielding to other apps, Windows startup, overlay and portable export are not built.
- **Note for agents:** In PowerShell, passing `$null` to a .NET string parameter sends `""`; use `[NullString]::Value`, especially with the wallpaper API.

## 2026-11-23 — Requirements round 4 answered, clarification complete

- **Purpose:** Record the final decisions. All design branches have an answer.
- **Changed:** This file only. No application code exists and Godot is not installed.
- **Audio:** Ambient sound varies by era, season, hour and camera position, plus sparse soft music, from CC0 or self-made sources. Left-clicking the tray icon opens Master, Ambient and Music sliders plus mute; right-click opens the menu. Levels persist. First run is 15%, with a 10-second fade-in at each start. Audio fades out for other apps' sound, fullscreen apps and lock screen, with a tray option to disable this.
- **Runtime:** Re-layout on monitor or resolution change. Auto quality lowers shadows, render resolution, then visible characters, without affecting simulation; presets are Low, Medium, High, Auto (default).
- **Data and packaging:** Keep the current world plus short chronicles and screenshots of past worlds in `%LOCALAPPDATA%\DeskCiv`. Ship as a portable DeskCiv folder needing no .NET or Godot install.
- **Prototype pass criteria (all required):** Runs behind icons on both monitors with icons still clickable; terrain and 2,000 dummy characters stable at 30 FPS at 5120x1440; average under 5% CPU, 10% GPU, 1.5 GB RAM; 1-hour run without abnormal RAM growth; tray volume works; rendering stops for fullscreen apps. On a miss, report real measurements and options; do not lower the criteria.
- **Verification:** None needed beyond recording the decisions. Feasibility of the targets is unproven.
- **Next:** Wait for the user's go-ahead, then build milestone 1 and stop for review.

## 2026-11-23 — Requirements round 3 answered

- **Purpose:** Record the user's round 3 decisions.
- **Changed:** This file only. No application code exists.
- **Accepted:** Godot 4 + C#, reconsider custom C#/DirectX only if the prototype misses targets. Standalone exe behind desktop icons, no mouse/keyboard input, tray menu for pause, overlay, settings, quit and Windows startup. Stop rendering when both monitors are covered or a fullscreen app runs; 15 FPS when one is covered. World freezes while PC is off or asleep, autosaves every 5 minutes and on shutdown. Physically hauled goods, age, families, housing and winter stockpiles. Director acts only indirectly, population never reaches 0, support rises if an era runs past 21 hours. Organic city growth that keeps historic traces. Conflict comes from outside the valley with no detailed battles. CC0 assets with a unified palette, procedural modular buildings for gaps. Milestones: technical prototype (stop for user review), Neolithic vertical slice, then all eras and replay.
- **Changed from recommendation:** The app must have sound, with volume adjustable from the system tray.
- **Verification:** None needed beyond recording the decisions. Godot is not installed yet.
- **Unresolved:** Audio details, display changes, quality scaling, world history, packaging and prototype pass criteria.

## 2026-11-23 — Requirements round 2 answered

- **Purpose:** Record the user's round 2 decisions.
- **Changed:** This file only. No application code exists.
- **Accepted:** 3D low-poly replaces 2D pixel art, and a performance prototype comes first. New project that learns from Latticefolk's architecture and may reuse its CC0 assets; copy a module only if clearly worthwhile, keeping MIT credit. One river valley growing from about 20 people to a city, up to about 2,000 individually simulated people, with aggregate population beyond that. Slow high-angle director camera with no shake or hard cuts. One season cycle = 30 real minutes; displayed calendar is non-linear from about 10,000 BCE to 2000. Day/night follows the real PC clock. Eight eras at about 21 real hours each: Paleolithic, Neolithic, Bronze, Iron/Classical, Medieval, Renaissance/Colonial sail, Industrial, 20th century. Fully offline, rule-based decisions.
- **Verification:** None needed beyond recording the decisions. The 10% GPU target at 5120x1440 is unproven until the prototype is measured.
- **Unresolved:** Engine, Windows integration, off-time behavior, simulation loop, director limits, city growth, conflict, assets, audio and first milestone.

## 2026-11-23 — Requirements round 1 answered

- **Purpose:** Record the user's round 1 decisions and the Latticefolk review.
- **Changed:** This file only. No application code exists.
- **Accepted:** Personal use first, with only self-made or clearly licensed assets. Hybrid simulation with a hidden director. About 7 real days per run, Stone Age to 2000. After a run, about 2 minutes of history replay, then a new world. Fictional civilization. Non-graphic hardship. One world across both monitors (5120x1440). Max 30 FPS, under 5% CPU and 10% GPU, render pause for fullscreen apps, persistent progress. Small, optional Vietnamese overlay.
- **Needs reconfirmation:** The user accepted 2D isometric pixel art before the Latticefolk link was reviewed. Latticefolk is 3D, so style is reopened in round 2.
- **Latticefolk facts (`ranzevor/Latticefolk`, read through GitHub only, not run):** MIT, TypeScript, Three.js, Node/Express/SQLite, CC0 asset manifest. Uses deterministic simulation, coarse/fine chunks, production chains, ecology and pluggable decision engines. `src/main.ts` is about 284 KB. It is a first-person/God View sandbox.
- **Unresolved:** Visual style, reuse of Latticefolk, engine, world scale, camera, calendar and era list.

## 2026-11-23 — Install grill-me, start requirements clarification

- **Purpose:** Prepare to clarify requirements for DeskCiv, a non-interactive desktop-background simulation of a civilization from the Stone Age to the 20th century (references: Knights and Merchants, Banished, Latticefolk).
- **Changed:** Created this file. Installed user-level skills `~/.claude/skills/grill-me` and `~/.claude/skills/grilling` from `mattpocock/skills` (MIT) at commit `abeeaf95660ba9e8b7c753eb38f441ca707a8823`. Only `SKILL.md` was installed; the OpenAI agent metadata was omitted.
- **User-visible behavior:** `/grill-me` and `grilling` are available in Claude Code. No application code exists yet.
- **Verified:** Read both skill files before install; they contain instructions only. `grilling` loaded in the session.
- **Machine facts:** RTX 3060, i5-12400F (6C/12T), 32 GB RAM, two 2560x1440 monitors at 59 Hz, Windows 11, .NET SDK 9.0.310, Node 22.22.1, Python via miniconda. Rust and Godot are not installed.
- **Unresolved:** All product requirements are still open.

