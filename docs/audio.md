# Sound

How sound in a level works, why it is built this way, and the design for the part that is not built: sound round corners.

Status on 2026-10-06: everything here is built and tested except section 9. Nobody has listened to any of it yet. Section 10 lists what needs ears.

The user docs are on the site: `reference/sound-entities.md`, `guides/captions.md`, `reference/material-files.md` and `reference/console.md`. File formats are in `docs/formats-and-pipeline.md`. Two spikes carry the measurements this design stands on: `docs/spikes/2026-10-openal-filters.md` and `docs/spikes/2026-10-sound-round-corners.md`.

## 1. The shape

```
simulation (ticks)          render thread (each frame)                      device
EntityWorld.Sounds  --->  SoundPresenter
  what is playing           query per sound
                            ISoundPropagation   distance, walls
                            SoundPathSmoother   ease, small steps
                            LevelDoppler        pitch from the path
                            32 sources to the loudest  ----------->  OpenAL: pan, low-pass
                            CaptionFeed  --->  FrameSnapshot  --->  log, editor, a game's own view
```

Four rules hold it together.

- **The simulation knows what is playing, not how it sounds.** A playing sound is an entry in `EntityWorld.Sounds`. Its end and its markers are counted in ticks from the sound's own length and pitch. Nothing asks the audio device whether a sound has finished. A level on a server with no device fires `OnEnded` on the same tick.
- **One step decides how a sound reaches the listener.** `ISoundPropagation.Resolve` takes the listener and every audible sound and gives each up to two paths. A path is an apparent position, a loudness and a muffling amount. It has no scene in its signature.
- **The engine works out loudness itself.** OpenAL's distance model is off and it only places the sound. Otherwise a path that is longer than the straight line would fight OpenAL's own idea of distance.
- **Captions are a list, not a widget.** The engine publishes which captions show now. Whatever draws them only reads the list.

## 2. What is playing

`SoundEmitters` on `EntityWorld` is the registry. An entity starts a sound there and gets an id. Each emitter knows its node, file, gain, pitch, the two distances, whether it loops, and what is simulated for it. `ISoundCatalog` answers how long a file is and where its markers are. It must be set before `EntityWorld.Activate`, or a sound that starts with the level finds nothing.

`point_sound` is the one class that plays sounds today. `Play` on a playing sound starts it over. `OnMarker` carries the marker's name, so a `logic_case` can tell markers apart. That is the whole answer to choreography for now: a spoken line can open a door on one word. The demo's lift starts on the word "up" this way.

Doors, lifts and buttons have no sound settings of their own. Wiring the demo's start room by hand took seven sound entities and about a dozen wires, and the lift's hum can start over a lift that stands still. Whether the movers get settings for a moving sound and an arriving sound is an open decision.

## 3. Files

- **`.saudio`**, version 2. Cooked from a `.wav` and resampled to the project's rate. A table of tagged sections follows the header, so an unknown section is skipped. One section exists: markers, a frame and a name each.
- **Markers** come from the wav's cue points and labels, or from a label file beside it, `<name>.markers.txt`. They live in the sound file because they are gameplay timing and must not change with the player's language.
- **The engine reads cooked sounds only.** The editor cooks a wav the first time a level uses it or someone listens to it, into a cache outside the project. The demo's sounds are cooked by its build with `scook sounds`.
- **Captions** are text, packed as written: `Captions/<language>.txt` and `<sound>.<language>.vtt`. Section 8.
- **Materials** say what they are made of with `acoustic = wood` in the `.spectramat`. Section 5.
- **Baked maps** keep the material of each collision hull face in an optional `.scmap` section, so a baked level answers the same as a live one.

## 4. From playing to heard

`SoundPresenter` runs once a frame on the render thread, after the camera is final.

1. Each playing sound becomes a `SoundQuery`: where its node is now, its two distances, what is simulated, and the node itself.
2. The propagation gives each a path. A sound too quiet to hear gets none.
3. `SoundPathSmoother` eases loudness and position, and moves the muffling in small steps (section 6). It snaps on a sound's first frame and on a teleport.
4. `LevelDoppler` turns the change of the path's length into a pitch factor (section 7).
5. The 32 sources go to the loudest sounds. One that gets no source keeps counting, and when it becomes one of the loudest it starts at the place the simulation says it has reached. A sound keeps its source unless a challenger is 1.5 times louder, so two equal sounds do not swap every frame.
6. A stereo file is never placed. It plays in both ears and only its loudness follows.

When the world ends, every level voice stops and everything kept per sound is dropped, the wall answers included. Stop, a level load during play and a save all pass through that one place.

Loudness over distance is `SoundFalloff`: full inside the minimum distance, then the inverse of distance, tapered so it reaches nothing at the maximum distance.

The editor's play button uses a voice of its own on a source outside the pool, so a preview never takes a source from a level.

## 5. Walls

A sound with something solid between it and the listener is quieter and duller, by what the solid is made of and how thick it is. `WallPropagation` does the distance part exactly as `DirectPropagation` does and then multiplies in what the walls leave. With nothing in the way the answer is the same to the last bit.

**What is in the way.** `Scene.TraceSolidSpans` returns the solids a segment passes through, nearest first, each with where it starts and ends and the material of the face entered. It reads the carved world, so a doorway cut through a wall is open. Parts that collide count where they stand now, so a door that has slid open is no longer on the line. Triggers and models do not block. It costs one to two and a half microseconds a trace on the demo's level.

**What it costs.** `AcousticPresets` holds, for each kind of material, what one surface takes and what each further metre takes, as an overall loss and a loss at 5 kHz, in decibels. Losses add along the line and are turned into the two factors once, with a floor of 30 dB overall and 26 dB at the top, so a sound behind a metre of concrete can still be heard.

| Preset | Surface | Surface, high | Per metre | Per metre, high |
|---|---|---|---|---|
| generic | 8.5 | 10.5 | 21 | 26 |
| fabric | 1 | 4 | 2.5 | 10 |
| plaster | 6.5 | 6 | 16 | 15 |
| wood | 7 | 7.5 | 17 | 19 |
| glass | 7.5 | 5 | 19 | 12.5 |
| metal | 7.5 | 12 | 19 | 30 |
| brick | 9.5 | 13.5 | 24 | 33.5 |
| concrete | 10 | 13.5 | 25 | 33.5 |
| rock | 10.5 | 13.5 | 25.5 | 33.5 |

These start from the mass law for one reference wall of each kind, cut to a quarter so a sound behind a wall stays audible. They are tuning data, not measurements. Brick, concrete and rock will probably sound alike.

**Five lines, not one.** One line from the sound to the listener flips from blocked to clear in a single frame as the listener passes a door frame. Each sound is heard along five lines: one to the listener and four to a ring of 0.25 round the listener's head. Their gains are averaged, so a view that is half open is half as blocked. Decibels are not averaged: a half open view beside a thick wall would then sound nearly shut.

**A sound's own body.** A sound under a door is not behind its own door. The trace ignores the sound's node and every part above it in the tree, and each line starts where it leaves the box of the nearest solid part, grown by 0.25. So a door's sound stays clear as the door slides into its wall.

**The listener in a wall.** A camera that clips into a wall for a frame must not mute the world. A solid that holds one end of a line costs by depth, and its surface loss comes in over 0.25 units.

**A budget.** At most 60 traces a frame, which is twelve sounds in full. A sound keeps its answer until its turn comes. An answer is due when the world near its lines was recompiled, when the listener or the sound moved 0.1, or after a quarter of a second, since a door that moves raises no signal. A new sound is answered on its first frame, so nothing is heard clear through a wall for a moment and then muffled. The whole step takes about 0.1 ms a frame with 32 sounds and a walking listener.

**With no audio device** nothing is traced. Captions there go by distance alone, and can differ from a machine that has sound.

## 6. The low-pass

The muffling number, `GainHf`, is the level left at 5 kHz on OpenAL Soft's high shelf. The very top falls to about its square. From about 0.1 down the middle of the sound goes as well.

OpenAL has no typed filter API in the binding in use, so the backend fetches four entry points through `alGetProcAddress` and calls them as function pointers, which NativeAOT allows. One scratch filter serves every source, because OpenAL copies a filter's values when it is attached. A library with no filters is warned about once and sounds are then quieter behind walls but not duller.

OpenAL switches to a new value in one step, and a large step knocks the low end for a few samples: 22 percent of a 200 Hz tone's amplitude for a move from 1 to 0.85. So the smoother moves `GainHf` by at most 0.02 an update and 1.2 a second. The worst step is then 4 percent. The price is time: a sound goes quiet in about 0.3 seconds when a door shuts and takes about 0.75 seconds to go dull.

The tests that render through the real OpenAL into memory are opt-in: `SPECTRA_AUDIO_LOOPBACK=1`.

## 7. Doppler

The pitch of a sound rises while it and the listener close in and falls while they part. The factor is `c / (c + rate)`, where `rate` is how fast the path's length grows and `c` is 343 units a second. Working from the path's length and not from velocities treats a moving listener and a moving sound alike, and stays right once a path bends round a corner.

The listener moves every frame and entities move every tick. A plain rate over frame time flutters by 40 cents at 144 frames a second. `DopplerClock` fits each path's rate to both clocks at once, which leaves under a hundredth of a cent at 60, 144 and 240 frames a second and a slow drift of a few cents at uneven rates.

The factor is kept between 0.5 and 2. A teleport is not motion: the first-person controller tells the presenter when the view snaps. Doppler changes only the pitch handed to the device. The simulation is not told, so a sound's end and its markers stay on the same ticks. Subtitles follow what is heard.

## 8. Switches and captions

**Switches.** Each sound has four, all on by default: `placed`, `fades`, `walls`, `doppler`. The console has the same four for the whole engine (`sound_simulate`), for listening to what each does. What is heard needs both.

**Two kinds of caption.** A sound caption is one line in `Captions/<language>.txt`: the sound's path, an equals sign, the words. A voice subtitle is a WebVTT file beside the voice file. The kind comes from where the text lives, never from a code in the text. This is where the design turns away from Source, whose captions are compiled into a hashed binary, keyed by sound script names and typed by codes inside the translated text.

**The feed.** `CaptionFeed` holds what shows now. The engine keeps the rules about hearing and reading:

- A caption comes up when its sound arrives at half a percent of full volume or more and is let go under a quarter of a percent. The gap keeps a sound at the edge of hearing from showing over and over.
- A voice line shows when it is said, never before.
- A caption stays up long enough to read: at least a second, and half a second plus a fifteenth of a second for each letter.
- A sound that repeats refreshes its caption. A looped sound shows its caption when it comes into hearing and lets it go once read.

Looks belong to the view. Until the game UI exists the views are the log and the editor's Output panel. A game sets `Engine.LogCaptions` to false and reads the same list.

The setting starts at voice only (`captions off|voice|all`). A missing language falls back to the project's, and the cook counts what each language lacks.

## 9. Round corners: the design

Not built. The spike says a grid of air cells flooded from the listener holds, with limits. This section is what to build from it.

**The idea.** Nothing is baked, because levels are edited live, doors move and the world has no extents. Around the listener, space is cut into cells one unit across. A flood outwards from the listener's cell gives each cell the length of the shortest way to the listener through air. A sound that has no clear line then has a path length and a direction that path arrives from, and is played from that direction at that distance: from the doorway, not through the wall.

**What the spike settled.**

- **A cell is air** when its centre is in air and a ray between it and its neighbour's centre is clear. The centre alone leaks through any wall thinner than a cell, and the demo has one 0.4 thick.
- **Cell size 1.** The demo's 1.4 doorway is open at every position at 0.5 and 1, and at three in four at 2.
- **The flood** is lazy Theta* over six neighbours with a ray back to the listener. Its worst length error on the hand cases is 10 percent, under a decibel. Breadth first is eight times cheaper and takes the wrong route.
- **Bounds.** By radius alone a flood outdoors takes 83 to 686 ms. Bounded by a floor and ceiling from the brushes' boxes, by each sound's maximum distance, and by stopping once the sounds are reached, it takes 0.5 to 2.7 ms. A budget of 20,000 cells was never hit.
- **Direction** cannot be read off the grid: it is up to 11 degrees off and jumps 12 degrees a frame. A few rays per sound against the real world mend it to within 1.4 degrees, and at most 4.3 degrees a frame on a walk past a doorway. A turn limit of 180 degrees a second caps the rest.
- **When.** Two to five floods a second on a worker thread, plus one at once when a door moves, the world changes or a corner is lost. Each frame, the render thread finds each sound's first leg again with rays. The world's trees are safe to read from a worker. Parts need a snapshot taken on the render thread.
- **What a bend costs** comes from the angle between the straight line and the first leg, and the same at the sound's end. Summing the grid path's turns is unusable.
- **One flood serves every sound.** Reading a sound out of it is a lookup.

**How it joins the engine.** `SoundPaths` already has room for two paths per sound. With a clear line a sound has one direct path. Blocked, with no air path in reach, it has the wall path. With both it has both, and the quieter is dropped when it is more than 20 dB under. In the demo's start room the air path came out 15 to 29 dB louder than the wall path.

**What the engine needs first.**

- A presenter that plays two paths of one sound in step. Today it plays the first.
- Position smoothing for a path's apparent position.
- A worker-safe snapshot of parts, with a change counter.
- A way to hold a baked world while a worker reads it, since its trees window a mapped file.

**What it will not do.** A gap narrower than a cell is open at some positions and shut at others. A room with no roof lets sound out over its walls, which is right, and is why the demo's start room would need a roof before a sound is heard from its doorway. Two routes of the same length make the direction jump when the shorter one changes.

**Not compared.** The plan named Steam Audio as the thing to measure against. That needs its SDK downloaded and was not run. From reading alone: its paths round corners are baked for geometry that does not move, and they come out in a form OpenAL cannot take unless Steam Audio also mixes. It stays a candidate for reverb.

## 10. What needs ears

No test can answer these.

- Falloff and the default distances of 2 and 30.
- Clicks when a sound starts, stops, is cut by `Stop`, or loses its source.
- The acoustic table, starting with a 5 cm wooden door against a 20 cm concrete wall, then glass against metal.
- The 0.75 seconds a sound takes to go dull when a door shuts, and whether a bassy sound buzzes faintly while it does.
- A step across a door frame, and a door's own sound heard from beside the doorway.
- Doppler on a steady tone while walking, and a teleport near a looped sound.
- The placeholder sounds, the synthetic voice and the two loop seams.
- The lift waiting for the word "up".
- Whether the caption thresholds and reading times feel right.

## 11. Not built

Corners, reverb, HRTF, mixer groups and lowering other sounds under speech, music, compressed formats, streaming from disk, pause, a drawn caption view, caption styling, a different recording per language, a timeline for scenes, lip sync, and the Luau API for sound.

Known gaps in what is built:

- The engine goes by the volume it plays a file at and does not know how loud the recording is. A quiet recording keeps a source and a caption that a loud one deserves more. A loudness figure in `.saudio` would fix both.
- A level keeps a sound it has loaded until the editor restarts.
- A sound's first play in a level cooks on the render thread in the editor.
- Meshes do not block sound.
- A pause or a time scale will need the pace passed to the Doppler clock.

## 12. Where the code is

| Part | Place |
|---|---|
| What is playing | `SpectraEngine.Core/Entities/SoundEmitters.cs`, `SpectraEngine.Entities/PointSound.cs` |
| Presenter, voices, preview | `SpectraEngine.Core/Audio/SoundPresenter.cs`, `LevelVoices.cs`, `SoundPreview.cs` |
| Propagation, walls, Doppler | `SpectraEngine.Core/Audio/Propagation/` |
| Acoustic table | `SpectraEngine.Core/Audio/Acoustics/` |
| The span query | `SpectraEngine.Core/Scene/Scene.SolidSpans.cs` |
| Device and filter | `SpectraEngine.Core/Audio/OpenAlBackend.cs`, `OpenAlLowPass.cs` |
| Captions | `SpectraEngine.Core/Audio/Captions/`, `SpectraEngine.Editor/Shell/CaptionOutput.cs` |
| Cook | `Spectra.Kitchen/Audio/`, `Spectra.Kitchen/Rules/` |
| Editor | `SpectraEngine.Editor/Sounds/`, `SpectraEngine.Editor/Shell/SoundPreview*.cs` |
| Demo sounds | `Assets/Sounds/`, `Assets/Captions/`, `SpectraEngine.Core/Scene/DemoPlayArea.cs` |
