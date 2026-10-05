# Sound round corners: an air grid flooded from the listener, October 2026

Verdict: holds, with limits. A grid of air cells one unit across, flooded outwards from the listener, finds the way round a corner, through the demo's 1.4 doorway and over a wall in the open. It does so on a live world and on a baked one with the same answers, and nothing is baked for it. One flood answers every sound, and reading a sound out of it is a lookup of 36 ns.
There are three limits. Outdoors the flood has to be bounded: by a floor and a ceiling taken from the brushes' boxes, by each sound's max distance, and by stopping once the sounds are reached. Bounded, it settles 1,900 to 11,000 cells and takes 0.5 to 2.7 ms. Bounded by its radius alone it takes 83 to 686 ms.
The grid by itself does not give a direction worth playing. The first leg is up to 11 degrees off and jumps by up to 12 degrees from one frame to the next. A few rays against the world per sound mend that: within 1.4 degrees on the hand cases and at most 4.3 degrees a frame on the walks.
And the demo's start room has no roof. The shortest way out of it is over the wall, not through the door.

This is a spike. It produces answers, not the feature. Everything lives under
`spikes/sound-corners/`, nothing in the engine changed, and the project is not
in `Spectra.slnx`. Nothing was played and no window was opened: every world is
built in code and every answer is a number.

## Rig and versions

| | |
| --- | --- |
| Machine | 13th Gen Intel Core i9-13900K, 32 logical CPUs, 32 GB |
| Windows | Windows 11 Pro 10.0.26300 |
| .NET | SDK 10.0.401, runtime 10.0.12 |
| Build | Release, under the JIT with tiered compilation off, as `CsgBench` runs. One more run as a NativeAOT executable. |
| Engine | `master` at `55456ca` |

The machine was in use while the spike ran. The process is pinned to logical
CPU 4 at high priority. Every timing is a median, taken three times over, and
the whole program was then run three times. The tables give the middle run.
Between the three runs a timing moved by 2.4% in the middle and by 14% at the
ninetieth percentile. One run in three ran a stretch about 60% slow, while
other work had the machine.

Nothing was downloaded or installed.

A unit is the engine's unit. The demo's character is 1.8 of them tall and its
doorways are 1.4 wide and 2.2 high.

## What was built

`SoundCornersSpike/` is a C# console project of about 5,900 lines that
references `SpectraEngine.Core` and nothing else.

- The worlds are brushes placed the way `DemoPlayArea` places them and carved
  by `CsgWorld.Build`. The demo's course is the real one: `DemoPlayArea.Build`
  into a `Scene`, then `Scene.CaptureStaticWorldPlacements`, which needs no
  renderer.
- The baked world is the same per-cell trees run through `BspFlattener` into a
  `CompiledStaticWorld`. The nodes sit in native memory behind a
  `MemoryManager`, as the engine's mapped nodes do. No `.scmap` was written
  and no file was mapped.
- Parts are convex hulls copied off the nodes: world planes and a box.
- `AirField` decides which cells are air and keeps the answers. `Flood` is the
  flood, in four kinds. `PathReader` reads a sound out of a finished flood.
- The true paths come from `ExactPath2D`: a visibility graph over the corners
  of the open space, for cases whose path lies in one plane. Its five answers
  match the sums done by hand.

Everything the spike takes from the engine is public. Nothing had to be copied
out of it.

Some words the tables use:

| Word | Meaning |
| --- | --- |
| cell | a cube of the air grid, 0.5, 1 or 2 units across |
| link | whether two neighbouring air cells are joined |
| first leg | the path's first straight stretch, from the listener to its first corner |
| reach | a sound's max distance: no path longer than this can be heard |
| kept | the air answers are left from an earlier flood. "First time" asks the world for every cell. |
| refine | find the first leg with rays against the world, between the corner the listener sees and the next one |
| tighten | also swing that corner sideways to where the way over is shortest |

## Reproduce

From the repository root, in an ordinary shell.

```powershell
spikes/sound-corners/run.ps1                  # every section, three times, to out/run-<n>.txt
spikes/sound-corners/run.ps1 accuracy doors   # only these, once, to the console
spikes/sound-corners/run.ps1 -Aot -Runs 1     # the same as a NativeAOT executable
```

The sections are `cells`, `accuracy`, `cost`, `outdoors`, `direction`,
`staleness`, `sounds`, `doors`, `breaks` and `threads`. A full run takes about
eight minutes. `out/` is git-ignored. Lines that start with `INFO` name the
machine.

## 1. Is a cell air?

### What one question costs

Over 19,008 cells of the demo's course, 23% of them solid. A point is
`ContainsPoint`. A ray is `Raycast` with the hit thrown away. The box test
walks the cell's tree with the box and reports air, solid or both.

| World | Point | Box | Ray to the next cell | Ray across the region |
| --- | --- | --- | --- | --- |
| live | 18 ns | 31 ns | 71 ns | 182 ns |
| baked | 30 ns | 40 ns | 162 ns | 285 ns |

In a town of 2,000 brushes the same four are 20, 44, 71 and 145 ns live and
39, 59, 160 and 260 ns baked. The cost follows the brushes in a 32-unit chunk,
at most 33 there, not the size of the level.

Under the JIT a baked ray costs more than twice a live one. As NativeAOT it
costs 92 ns against 71. Why was not profiled.

### Four rules

| Rule | A cell is air when | Two cells are joined when |
| --- | --- | --- |
| centre point | its centre is not in solid | both are air |
| centre point and link rays | its centre is not in solid | both are air and the ray between their centres is clear |
| nine points | its centre and eight corners are not in solid | both are air |
| box | the box test finds no solid | both are air |

What a rule costs per cell, the first time and from the kept answer:

| Rule | Live, first time | Baked, first time | Kept | Points a cell | Rays a cell |
| --- | --- | --- | --- | --- | --- |
| centre point | 42 ns | 55 ns | 4 ns | 1.17 | 0 |
| centre point and link rays | 234 ns | 513 ns | 4 ns | 1.17 | 2.27 |
| nine points | 172 ns | 284 ns | 4 ns | 8.56 | 0 |
| box | 55 ns | 68 ns | 4 ns | one box | 0 |

### A cell half in a wall

Against 64 points spread through each cell of the demo's course, at cell 1:
14,353 cells are all air, 4,244 all solid and 411 are cut by a surface.

| Rule | Cut cells called air | Air cells called solid | Solid cells called air |
| --- | --- | --- | --- |
| centre point | 72.7% | 0 | 0 |
| nine points | 1.0% | 12 | 0 |
| box | 0% | 50 | 0 |

The box test errs on the solid side: it does not clip the box on its way down
the tree, so it can meet a solid leaf the box never reaches. The nine points'
twelve are cells whose corners touch solid that the 64 points, which stop
short of the corners, do not.

### When a doorway closes

Two sealed rooms and a wall one thick with a doorway 2.2 high. The doorway was
slid along the wall to 16 positions an eighth of a unit apart, since a door can
stand anywhere against the grid. The share of positions at which a flood gets
through:

| Cell | Rule | 0.7 wide | 1.0 wide | 1.4 wide | 2.0 wide | 2.8 wide |
| --- | --- | --- | --- | --- | --- | --- |
| 0.5 | centre point and link rays | 100% | 100% | 100% | 100% | 100% |
| 0.5 | nine points, box | 50% | 100% | 100% | 100% | 100% |
| 1 | centre point and link rays | 75% | 100% | 100% | 100% | 100% |
| 1 | nine points, box | 0% | 0% | 50% | 100% | 100% |
| 2 | centre point and link rays | 38% | 50% | 75% | 100% | 100% |
| 2 | nine points, box | 0% | 0% | 0% | 0% | 0% |

By the centre a gap is open for certain once it is one cell wide. By the
corners or the box it needs two. The demo's 1.4 doorway is open at every
position at cell 1 by its centre, at half of them by the box, and at three
quarters of them at cell 2. At cell 2 the last two rules find no air cell in a
room three high, so nothing gets anywhere.

### When a thin wall leaks

Two sealed rooms with a wall between them and no opening, at 8 positions a
quarter of a unit apart. The share at which a flood crosses the wall. Six
neighbours and 26 gave the same shares.

| Wall | Cell | Rule | 0.1 thick | 0.3 thick | 0.5 thick | 1.0 thick |
| --- | --- | --- | --- | --- | --- | --- |
| square to the grid | 1 | centre point | 100% | 75% | 50% | 0% |
| square to the grid | 1 | nine points | 100% | 50% | 0% | 0% |
| turned 20 degrees | 1 | centre point | 100% | 100% | 100% | 0% |
| turned 20 degrees | 2 | centre point | 100% | 100% | 100% | 100% |
| turned 45 degrees | 1 | centre point | 100% | 50% | 50% | 0% |
| any of the three | 0.5, 1, 2 | centre point and link rays | 0% | 0% | 0% | 0% |
| any of the three | 0.5, 1 | box | 0% | 0% | 0% | 0% |

The centre alone leaks through any wall thinner than a cell. The demo has
such a wall: `Play.DoorWall` is 0.4 thick. The link rays stop every leak, and
so does the box.

So the rule is the centre point with link rays. It never leaked, and it opens
a gap as soon as the gap is a cell wide. It costs 234 ns a cell the first time
on a live world and 513 ns on a baked one, and 4 ns from then on until the
world changes there.

### Doors

Parts are not in the tree. The spike lays them over each flood: a cell whose
centre a part holds is closed, and with "links" a step between two cells is
also closed when the part's hull crosses it. That is a slab clip against a few
planes, with no ray into the world.

A shut door in the start room's doorway, the room roofed so the doorway is the
only way out:

| Door | Cell 0.5, cells only | Cell 0.5, links | Cell 1, cells only | Cell 1, links |
| --- | --- | --- | --- | --- |
| 0.8 thick, mid wall (the demo's) | holds | holds | holds | holds |
| 0.4 thick, mid wall | leaks | holds | holds | holds |
| 0.4 thick, on one face of the wall | holds | holds | leaks | holds |
| 0.2 or 0.1 thick, mid wall | leaks | holds | holds | holds |
| 0.2 or 0.1 thick, on one face of the wall | leaks | holds | leaks | holds |

Where "cells only" holds for a door thinner than a cell, it is because a cell
centre happens to lie inside the door.

What the parts cost a flood of 57,000 cells at cell 1:

| Parts | Flood |
| --- | --- |
| none | 31.1 ms |
| the demo's four that collide | 32.5 ms |
| 200 the size of a door, as a flat list | 346 ms |

The demo's parts cost 1.4 ms. Two hundred cost ten times the flood, because
every ray was tried against every part. A real level needs the parts in a
broadphase.

The demo's door slides 1.4 at 3 units a second. What the flood makes of it,
with the listener outside and off to one side:

| Door slid by | Cell 0.5 | Cell 1 |
| --- | --- | --- |
| 0.0 to 0.2 | no path | no path |
| 0.3, 0.4 | no path | 9.83 |
| 0.5 | 9.53 | 9.83 |
| 0.6 to 0.9 | 9.37 | 9.49 |
| 1.0 | 9.16 | 9.49 |
| 1.1 | 9.03 | 9.49 |
| 1.2 to 1.4 | 9.03 | 9.07 |

The doorway opens in steps of a cell. At cell 1 the path appears a tenth of a
second after the door starts to move and shortens twice more. Each later step
is under 1 dB of loudness for this sound. The first is from nothing to the
whole path.

## 2. The flood

### The floods tried

| Flood | What it is |
| --- | --- |
| breadth first, 6 | plain breadth first over the six face neighbours, every step one cell |
| Dijkstra, 26 | all 26 neighbours with their real step lengths. A diagonal step is only open when every way round it along the axes is. |
| fast marching | the first-order kind, on the six-neighbour stencil |
| Theta*, 6 | lazy Theta*: a cell takes its parent's parent when it sees it, so a path is a few straight legs. Six neighbours. |
| Theta*, 6, ray to listener | the same, with a cell's sight of the listener checked by a ray against the world and not along the grid |
| pulled with rays | a breadth-first or Dijkstra flood whose chain of parents is pulled straight with rays when a sound is read |

Every flood starts from the cells round the listener that it sees by a ray,
so the listener's own cell need not be air.

### Against paths solved by hand

| Case | Straight | True path | True bend |
| --- | --- | --- | --- |
| one corner: an L-shaped corridor 3 wide | 19.81 | 25.18 | 76.3 deg |
| U corridor: round the end of a wall 2 thick | 5.00 | 28.17 | 166.8 deg |
| doorway, from the side: 1.4 wide in a wall 1 thick | 5.39 | 14.23 | 144.0 deg |
| doorway, nearly in line: the same doorway | 18.36 | 18.37 | 13.6 deg |
| over a wall in the open: 2.5 high, 1 thick, 60 long | 10.01 | 10.29 | 28.9 deg |

The worst of the five cases for each flood. Length error is the flood's length
over the true one. First leg is the angle between the flood's first leg and
the true one.

| Cell | Flood | Length error | First leg | First leg, refined | First leg, tightened |
| --- | --- | --- | --- | --- | --- |
| 0.5 | breadth first, 6 | 36.1% | 2.1 deg | 2.1 deg | 0.7 deg |
| 0.5 | Dijkstra, 26 | 10.8% | 2.4 deg | 2.4 deg | 0.7 deg |
| 0.5 | fast marching | 6.4% | 2.1 deg | 2.1 deg | 1.4 deg |
| 0.5 | Theta*, 6 | 4.7% | 2.1 deg | 2.1 deg | 0.7 deg |
| 0.5 | Theta*, 6, ray to listener | 4.7% | 2.1 deg | 1.8 deg | 0.7 deg |
| 1 | breadth first, 6 | 36.1% | 7.3 deg | 1.3 deg | 1.2 deg |
| 1 | Dijkstra, 26 | 21.8% | 7.3 deg | 1.3 deg | 1.3 deg |
| 1 | fast marching | 19.3% | 7.3 deg | 1.3 deg | 1.3 deg |
| 1 | Theta*, 6 | 15.8% | 7.3 deg | 1.3 deg | 1.3 deg |
| 1 | Theta*, 6, ray to listener | 10.2% | 4.2 deg | 1.3 deg | 1.4 deg |
| 1 | breadth first, 6, pulled with rays | 10.2% | 4.2 deg | 1.3 deg | 1.4 deg |
| 1 | Dijkstra, 26, pulled with rays | 10.2% | 4.2 deg | 1.3 deg | 1.4 deg |
| 2 | breadth first, 6 | 36.1% | 12.1 deg | 4.4 deg | 3.7 deg |
| 2 | Theta*, 6, ray to listener | 35.1% | 11.7 deg | 4.4 deg | 4.0 deg |

Theta* with a ray to the listener at cell 1, case by case:

| Case | Length | Error | First leg | Refined | Tightened |
| --- | --- | --- | --- | --- | --- |
| one corner | 26.21 | +4.1% | 4.2 deg | 0.7 deg | 1.3 deg |
| U corridor | 29.47 | +4.6% | 4.2 deg | 0.8 deg | 0.6 deg |
| doorway, from the side | 15.69 | +10.2% | 1.3 deg | 1.3 deg | 1.4 deg |
| doorway, nearly in line | 18.89 | +2.9% | 1.7 deg | 1.2 deg | 1.3 deg |
| over a wall in the open | 10.88 | +5.7% | 4.1 deg | 0.4 deg | 0.0 deg |

How to read it:

- Every flood is too long, never too short: a path has to pass through cell
  centres, and they stand half a cell off every wall. A doorway one cell wide
  costs the most. 10% of a path is 0.8 dB of loudness.
- Breadth first counts steps, and a step along a diagonal counts for up to
  1.73 of its length. Its lengths are useless as they are. Pulled straight
  with rays they are as good as Theta*'s on these five cases.
- Sight along the grid is half a cell too generous. At cell 0.5 it let the
  listener "see" a cell beyond the far top edge of the wall, which the wall
  hides. The first leg then pointed 2.1 degrees wrong, and refining had
  nothing to start from, since the ray to that corner is blocked. Checking a
  cell's sight of the listener with a ray puts that right, and in the open it
  is cheaper than walking the grid: 28 ms against 75 ms round the hut at
  radius 40.
- Where the ray-checked floods differ is the choice of route. See section 8.

On the baked world the floods give the same answers as on the live one. The
five hand cases and the demo's seven sounds, at three cell sizes with every
flood, are 324 answers. All 324 have the same length and the same first leg,
to the last digit.

### Time and memory, bounded only by the radius

One flood from a listener standing in the demo's course, which is open to the
sky, with the world as it is and no bound but the radius. Kept answers, in
ms. Each flood covers every path up to the radius, so breadth first, which
overstates a path by up to 1.73, goes that much further out.

| Cell | Radius | Breadth first, 6 | Dijkstra, 26 | Fast marching | Theta*, 6 | Theta*, 6, ray to listener | The last: cells settled | The last: first time | The last: window |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2 | 20 | 0.14 | 0.90 | 0.98 | 0.90 | 0.91 | 2,670 | 1.86 | 0.2 MB |
| 2 | 40 | 1.39 | 7.17 | 12.3 | 13.2 | 11.5 | 27,596 | 19.3 | 1.3 MB |
| 2 | 60 | 5.27 | 25.3 | 49.5 | 64.0 | 51.2 | 101,187 | 68.7 | 3.8 MB |
| 1 | 20 | 1.17 | 5.98 | 9.46 | 10.3 | 8.10 | 21,815 | 15.5 | 1.3 MB |
| 1 | 40 | 14.3 | 61.5 | 120 | 171 | 122 | 225,505 | 191 | 8.4 MB |
| 1 | 60 | not run | 251 | 528 | 906 | 642 | 822,031 | 783 | 26.5 MB |
| 0.5 | 20 | 11.9 | 48.5 | 92.3 | 130 | 74.8 | 175,578 | 136 | 8.4 MB |
| 0.5 | 40, 60 | not run | not run | not run | not run | not run | | | 60 and 196 MB |

Per cell settled, from kept answers: breadth first 28 to 35 ns, Dijkstra 216
to 278, fast marching 301 to 471, Theta* 327 to 1,100, Theta* with a ray to
the listener 342 to 780. The first time adds about 300 ns for each cell
settled: the 234 ns of section 1, for a few more cells than the flood settles.

The window is the box of cells round the listener that the flood's arrays
cover: 14 bytes a cell, for the path length, the parent, the first corner and
two bytes of flags. "Not run" is a window of more than 3.2 million cells. The
kept air answers are one byte a cell in blocks of 4,096 cells: 0.25 MB for a
flood of 43,000 cells.

In two sealed rooms the same floods settle 1,442 cells at any radius and take
0.06 ms breadth first and 0.33 to 0.42 ms for the others. Indoors the flood is
small. In the open the cells grow with the cube of the radius, and section 4
is about that.

## 3. Direction of arrival

The engine would play the sound from the listener's position plus the first
leg's direction times the path's length. So the direction is the first leg's.

Two walks at 5 units a second and 60 frames a second, with a flood every
frame:

- Past a doorway: 193 frames along a wall, 3 units from it, the sound in the
  next room off to one side. The true first leg turns by at most 1.59 degrees
  a frame.
- Along a wall in the open: 241 frames, 6 units from a wall 2.5 high with the
  sound behind it. The true first leg turns by at most 0.46 degrees a frame.

Theta* with a ray to the listener:

| Walk | Cell | First leg | Largest jump in a frame | Frames over 3 deg | Mean error | Largest error |
| --- | --- | --- | --- | --- | --- | --- |
| past a doorway | 0.5 | cell centre | 5.3 deg | 3 | 1.7 deg | 5.8 deg |
| past a doorway | 0.5 | refined | 3.7 deg | 2 | 0.8 deg | 3.3 deg |
| past a doorway | 0.5 | tightened | 3.1 deg | 1 | 0.6 deg | 3.1 deg |
| past a doorway | 1 | cell centre | 11.7 deg | 4 | 4.1 deg | 11.1 deg |
| past a doorway | 1 | refined | 7.9 deg | 1 | 2.2 deg | 8.9 deg |
| past a doorway | 1 | tightened | 4.3 deg | 2 | 1.4 deg | 4.9 deg |
| past a doorway | 2 | cell centre | 16.7 deg | 6 | 12.6 deg | 20.5 deg |
| past a doorway | 2 | tightened | 14.0 deg | 3 | 3.7 deg | 16.4 deg |
| along a wall | 0.5 | cell centre | 8.7 deg | 34 | 2.3 deg | 6.3 deg |
| along a wall | 0.5 | refined | 8.1 deg | 25 | 1.8 deg | 5.7 deg |
| along a wall | 0.5 | tightened | 1.3 deg | 0 | 0.2 deg | 0.5 deg |
| along a wall | 1 | cell centre | 11.5 deg | 16 | 5.0 deg | 9.1 deg |
| along a wall | 1 | refined | 8.2 deg | 16 | 2.0 deg | 5.6 deg |
| along a wall | 1 | tightened | 1.0 deg | 0 | 0.2 deg | 0.5 deg |
| along a wall | 2 | cell centre | 20.1 deg | 8 | 5.8 deg | 15.4 deg |
| along a wall | 2 | tightened | 1.2 deg | 0 | 0.2 deg | 0.5 deg |

What goes wrong, and what mends it:

- The first corner is a cell centre. When the path moves to the next cell the
  direction jumps by about a cell over the distance to the corner: 8 to 12
  degrees at cell 1.
- Refining finds where the listener's sight of the path really ends, by
  halving with twelve rays between the corner it sees and the next one. That
  takes the cell out of the answer across the path. It cannot move the corner
  along an edge, so along the wall the direction still steps a cell at a time.
- Tightening swings the corner about the line from the listener to a later
  point of the path, the sound itself when one corner can reach it, and keeps
  the angle at which the two legs, each drawn tight with rays, are shortest.
  Along the wall that is 1.0 degree a frame at most and 0.2 degrees of error,
  with no smoothing at all.
- Past the doorway two frames still jump, by 4.3 degrees. A limit on how fast
  the direction may turn removes them:

| Walk, cell 1, tightened | Largest jump | Mean error | Largest error |
| --- | --- | --- | --- |
| past a doorway, no smoothing | 4.3 deg | 1.4 deg | 4.9 deg |
| past a doorway, at most 180 deg a second | 3.0 deg | 1.4 deg | 4.9 deg |
| past a doorway, at most 90 deg a second | 1.5 deg | 1.4 deg | 4.9 deg |
| past a doorway, eased with 0.1 s | 1.5 deg | 4.3 deg | 8.4 deg |
| past a doorway, eased with 0.2 s | 1.4 deg | 8.3 deg | 16.1 deg |
| along a wall, no smoothing | 1.0 deg | 0.2 deg | 0.5 deg |
| along a wall, eased with 0.1 s | 0.5 deg | 2.0 deg | 2.8 deg |

Easing the way `SoundPathSmoother` eases loudness is the wrong tool here. A
listener walking past a doorway turns the true direction by 95 degrees a
second, and an eased direction lags it by 4 to 8 degrees. A limit on the turn
rate leaves a true turn alone and only catches the jumps.

The other floods on the same walks at cell 1, tightened: fast marching 4.6 and
1.5 degrees for the largest jump, Dijkstra pulled with rays 4.3 and 2.3.
Untightened, Dijkstra is the worst along the wall: 41 frames over 3 degrees,
because 26 neighbours tie on many paths of one length and the crossing place
on the wall's top wanders among them. Breadth first is worse again: its mean
error along the wall is 11 degrees even when refined.

One more thing was tried, the other way round: one flood from the sound, never
redone, with the listener reading the slope of path length where it stands.
At cell 1 it was 7.6 and 11.4 degrees off on average on the two walks, with
jumps of 16 and 7 degrees. It is not better than aiming a kept path, and it
needs a flood per sound. Not followed further.

## 4. Outdoors

Where nearly every cell is air, the flood is as big as its bounds let it be.
Five bounds were tried:

| Bound | What it does | Can it lose a path |
| --- | --- | --- |
| only blocked sounds | no flood at all when no sound in earshot has its straight line blocked | no |
| floor and ceiling | no cell above the highest brush near the listener or below the lowest, unless the listener or a sound is | no: a shortest path never leaves the box round what it bends round |
| reach | a cell is not gone on from when no sound could still be within its max distance by way of it | no: such a path could not be heard |
| stop | the flood ends once every blocked sound in the window has been reached | no |
| budget | the flood ends after so many cells | yes |

One flood at cell 1 with a radius of 60, Theta* with a ray to the listener,
kept answers. Each cell of the table is cells settled, then ms.

| Bound | Hut in a field, 15 away | The hut, 30 away | Wall in the open | Demo course, 6 sounds | Start room roofed and shut |
| --- | --- | --- | --- | --- | --- |
| radius only | 262,705, 85.0 | 228,846, 82.5 | 272,644, 86.5 | 823,795, 661 | 817,629, 686 |
| floor and ceiling | 20,443, 6.26 | 18,834, 5.38 | 16,204, 6.03 | 128,037, 91.8 | 126,713, 89.7 |
| stop | 44,935, 11.7 | 88,907, 24.0 | 5,559, 1.41 | 9,904, 3.67 | 817,629, 695 |
| reach | 8,293, 2.04 | 46,493, 11.6 | 9,359, 2.55 | 3,764, 1.30 | 3,456, 1.31 |
| floor and ceiling, stop and reach | 3,608, 0.90 | 10,695, 2.68 | 1,885, 0.48 | 2,747, 0.99 | 2,640, 1.01 |
| the same, first time | 2.17 ms | 6.31 ms | 1.10 ms | 1.97 ms | 2.21 ms |

The sound's reach is 30 for the hut at 15 and for the wall, 60 for the hut at
30, and each sound's own 15 to 20 in the demo. The window is 26.5 MB by the
radius alone and 1.7 to 2.9 MB between floor and ceiling.

The same at cell 0.5 with floor, ceiling, reach and stop: 20,968 cells for the
hut at 15, 62,168 at 30, 10,638 for the wall and 16,379 for the demo, in 5.5,
16.2, 2.8 and 6.0 ms. About six times the cells of cell 1.

What each bound did:

- Asking whether a sound's straight line is blocked costs 100 to 150 ns a
  sound, and finding floor and ceiling from the brushes' boxes 2 to 5 us.
  Both are nothing next to a flood.
- Floor and ceiling take the hut's flood from 262,705 cells to 20,443. In the
  open the flood becomes a disc a few cells thick. It is the bound that makes
  the open world affordable, and it changed no answer.
- Reach does the most where the sounds are near. It is also the only bound
  that helps a sound with no path at all. With the start room roofed and its
  door shut, four sounds have none: "stop" alone then floods everything,
  817,629 cells, and with reach it is 3,456.
- Stop does the most where the path is short: the wall, 5,559 cells from
  272,644.
- With all three, the answers were the same as with none in every case: no
  length moved and no first leg turned.
- A budget of 20,000 cells was never reached at cell 1. One of 5,000 cut the
  hut at 30 off with no answer. At cell 0.5 the budget of 20,000 lost the hut
  both times.

The listener walking a full circle round the hut, 15 from its middle, with a
flood every frame for 1,130 frames:

| Cell | First leg | Largest jump | Frames over 3 deg | Changes of route | Largest jump, eased with 0.1 s |
| --- | --- | --- | --- | --- | --- |
| 1 | cell centre | 12.4 deg | 19 | 4 | 2.1 deg |
| 1 | tightened | 9.0 deg | 10 | 4 | 1.3 deg |
| 0.5 | tightened | 8.2 deg | 7 | 8 | 0.9 deg |

The path goes straight through the door for 7% of the circle, round a corner
of the hut for 74% and over its roof for 19%. Where it changes from one to
another the direction jumps by up to 9 degrees, and that jump is real: two
routes of the same length arrive from two places. Only smoothing covers it.
At cell 0.5 the shares are 6%, 61% and 33%. Round the corner and over the roof
are within a few percent of each other there, and the grid's error decides.

## 5. Staleness and threads

### How often

The two walks again. The reference is a flood every frame. Between floods
three things were tried: hold the last answer, aim at the last flood's first
corner from where the listener now stands, and keep the last flood's corners
but find the first leg again with rays every frame, flooding at once when the
listener loses sight of the first corner.

Cell 1, largest direction error against a flood every frame:

| Floods | Past a doorway: hold | aim | rays | Along a wall: hold | aim | rays |
| --- | --- | --- | --- | --- | --- | --- |
| 10 a second | 7.7 deg | 4.6 deg | 1.0 deg | 2.7 deg | 2.1 deg | 0.7 deg |
| on leaving a cell, about 5 a second | 16.3 deg | 4.1 deg | 1.0 deg | 5.3 deg | 3.6 deg | 0.6 deg |
| 5 a second | 16.1 deg | 4.6 deg | 2.0 deg | 5.2 deg | 3.6 deg | 0.7 deg |
| 2 a second | 39.2 deg | 8.3 deg | 2.4 deg | 13.2 deg | 8.6 deg | 0.7 deg |
| 1 a second | 70.7 deg | 8.3 deg | 2.4 deg | 25.4 deg | 17.1 deg | 3.5 deg |

The largest error in path length at 2 floods a second was 2.19 units when
held, 0.42 when aimed and 0.23 with rays. At 10 units one unit is 0.8 dB.

So the flood does not have to keep up with the listener. Held, an answer is
wrong within a tenth of a second. With the first leg found again each frame,
two floods a second gave the same direction as sixty, to within 2.4 degrees.
My estimate, with nobody listening: 2 to 5 floods a second while the listener
moves, none while it stands still, and one at once when a door moves, the
world changes, the listener teleports or a sound loses sight of its corner.

Finding the first leg again costs 33 us a sound with tightening and 4.3 us
with refining alone. That is render-thread work, every frame, for each
blocked sound that can be heard.

### Threads

The flood read the world from eight worker threads while the main thread kept
deriving the next world from the one being read, as the engine's compile
does, by moving one brush to and fro. Four seconds, in each of the three runs:

| Run | Floods on the workers | Worlds compiled meanwhile | Wrong answers | Exceptions |
| --- | --- | --- | --- | --- |
| 1 | 43,056 | 8,434 | 0 | 0 |
| 2 | 37,500 | 6,198 | 0 | 0 |
| 3 | 40,968 | 7,611 | 0 | 0 |

Every answer was compared with the same flood on a world built with no caches
on one thread.

What the code says, read and not run unless marked:

- `CsgWorld`, `ChunkGrid`, `WorldChunk` and `BspTree` are safe to read from a
  worker. A chunk's lists and tree are only written while its world is being
  built. A patch makes new chunks for the cells it touches and a new overlay,
  and leaves the old world as it was. The world's lazy caches are swapped in
  with `Interlocked` and no query reads them. The run above agrees.
- `Brush` is not immutable as a whole: `Brush.Transform` has a public setter.
  The flood never reads it. It reads the tree, and `BrushPlacement.WorldBounds`
  for the floor and ceiling, which is the brush's fixed local box under the
  placement's own matrix.
- A baked world must not be disposed under a reader. That is true. The
  research note has it right. `FlatBspTree.ContainsPoint` takes the node span
  once and walks it. The span comes from `ContentBlob.Span`, which checks a
  flag and then hands out a window into the mapped pack with nothing holding
  the mapping for as long as the span lives. `Scene.ReleaseCompiledStaticWorld`
  disposes the blob on the render thread, and `PackHandle` says what follows:
  "Unmapping under a live span is an access violation, not an exception."
  The spike's baked world is in memory that is never freed, so this was not
  run.
- Parts are scene nodes and belong to the render thread. A worker needs a copy
  taken there: each part's world planes, its box and its node id, which is
  about 200 bytes a part, and a number that changes when any of them moves.

## 6. How much a bend costs

Three ways to put a number on how far round the path goes, against the true
bend, with Theta* and a ray to the listener at cell 1:

| Case | True bend | Corner by corner | From the two ends | True detour | Flood's detour |
| --- | --- | --- | --- | --- | --- |
| one corner | 76.3 deg | 81.8 deg | 76.3 deg | 5.37 | 6.39 |
| U corridor | 166.8 deg | 171.3 deg | 166.8 deg | 23.17 | 24.47 |
| doorway, from the side | 144.0 deg | 148.2 deg | 144.0 deg | 8.85 | 10.30 |
| doorway, nearly in line | 13.6 deg | 126.9 deg | 2.7 deg | 0.01 | 0.54 |
| over a wall in the open | 28.9 deg | 49.7 deg | 29.0 deg | 0.28 | 0.87 |

- Corner by corner is the sum of the turns along the flood's own path. It is
  not stable enough. A path squeezed through a doorway one cell wide zigzags
  between cell centres: 127 degrees for a path that truly bends by 14. Its
  worst error over the five cases is 46 degrees at cell 0.5, 113 at cell 1
  and 121 at cell 2.
- From the two ends is the angle between the straight line and the first leg,
  plus the same at the sound, each leg found with rays. For a path that wraps
  one way round, the two add up to the whole bend, and four of the five cases
  come out within 0.1 degrees. The fifth bends one way and back, and reads 2.7
  where the truth is 13.6: too little, on a path that is all but straight.
- The detour is path length less straight distance, the "ratio" idea. Physics
  likes it: the textbook rule for a noise barrier takes its loss from this
  very difference. But the flood's lengths run 0.5 to 1.5 units long, and near
  the edge of the shadow that is the whole detour: 0.54 against 0.01.

So the bend should come from the two end angles. They go to zero as the sound
comes into sight, which the loudness has to do as well, and they are measured
against the world and not the grid. As the start room's door slides open the
path length steps a cell at a time, and the bend from the two ends falls from
53 to 30 degrees two or three degrees a step, the same at cell 0.5 and cell 1
to within a degree.

## 7. Many sounds

A town of 2,000 brushes, 32 sounds within 30 units of the listener, 11 of them
with their straight line blocked. Cell 1, bounded as in section 4.

| Step | Each | For 32 |
| --- | --- | --- |
| is the straight line blocked, live | 153 ns | 4.9 us |
| the same, baked | 378 ns | 12.1 us |
| one flood for the 11 blocked sounds, Theta* with a ray to the listener, 15,913 cells, kept | | 6.06 ms |
| the same flood the first time, live | | 12.8 ms |
| the same flood the first time, baked | | 22.4 ms |
| the same flood as Dijkstra, 26, kept | | 4.76 ms |
| the same flood breadth first, 6, kept | | 0.91 ms |
| read a sound from the Theta* flood: length and first leg | 36 ns | 1.2 us |
| and the bend corner by corner | 91 ns | 2.9 us |
| and the first leg refined | 4.3 us | 0.14 ms |
| and the first corner tightened | 35 us | 1.13 ms |
| aim a kept path again from a listener that has moved | 33 us | 1.07 ms |
| read a sound from a Dijkstra or breadth-first flood, pulled with rays | 1.3 to 1.5 us | 41 to 49 us |
| a Theta* flood for each of the 11 blocked sounds | | 13.7 ms |

Reading a sound out of a Theta* flood is a lookup: the cell the sound is in,
its path length, and the first corner, which the flood carried along with
every cell. 36 ns. The other floods have to walk their chain of parents back
and pull it straight.

The flood is the cost, and it does not grow with the number of sounds. The
rays for the first leg do: 0.14 ms for 32 sounds refined and 1.1 ms tightened,
if all 32 were blocked. Here 11 were.

## 8. Against the wall path

The demo's start room: 8 by 8 inside, walls 2.5 high and one thick, a doorway
1.4 by 2.2 in its east wall, and `RoomTone` in the middle of it at 1.25, at
full volume to 6 and silent from 16. The listener stands outside, east of the
wall, with ears at 1.6. Lengths by hand are from the floor plan for the
doorway and from unfolding the wall for the way over it.

As authored, with no roof:

| Listener | Straight line | Through | By hand, doorway | By hand, over the wall | Flood, door shut | Flood, door open |
| --- | --- | --- | --- | --- | --- | --- |
| off to the side of the door | 8.55 | 1.07 of wall | 8.84 | 8.85 | 8.87, over the wall | 8.87, over the wall |
| in line with the door | 7.01 | the door | 7.01 | 7.38 | 7.48, over the wall | 7.01, straight |
| far along the wall | 12.81 | 2.88 of wall | 14.82 | 13.01 | 13.32, over the wall | 13.32, over the wall |

With a lid the spike puts on the room, so the doorway is the only way out:

| Listener | Straight line | By hand, doorway | Flood, door shut | Flood, door open |
| --- | --- | --- | --- | --- |
| off to the side of the door | 8.55 | 8.84 | no path | 9.07, doorway |
| in line with the door | 7.01 | 7.01 | no path | 7.01, straight |
| far along the wall | 12.81 | 14.82 | no path | 15.35, doorway |

The flood is Theta* with a ray to the listener at cell 1. With no roof the
sound is heard from over the wall, and that is right for a room with no roof:
by hand the two ways are 8.84 and 8.85 for the first listener. Whoever wants
to hear it from the doorway has to put a roof on.

The choice of route is where the floods part:

| Listener, no roof, door shut | By hand | Breadth first, pulled | Dijkstra, pulled | Theta*, ray to listener |
| --- | --- | --- | --- | --- |
| off to the side, cell 1 | 8.85 | 9.40 | 9.51 | 8.87 |
| far along the wall, cell 1 | 13.01 | 15.61 | 13.34 | 13.32 |
| far along the wall, cell 0.5 | 13.01 | 15.02 | 13.16 | 13.20 |

Breadth first finds a way over the wall, but not the short one: to it every
staircase of steps is as short as any other. Its path is 15% to 20% long, and
its first leg points somewhere else along the wall.

### The two paths in numbers

The wall path is the straight line with what the walls take off. Taking the
room's wall as the `generic` acoustic preset and the door as `wood`, and the
loudness of the air path from its length alone:

| Room | Listener | Wall path | Air path, door shut | Air path, door open | Its bend from the two ends |
| --- | --- | --- | --- | --- | --- |
| no roof | off to the side | -35.6 dB, high end -26.0 | -6.3 dB | -6.3 dB | 28 deg |
| no roof | in line with the door | -22.9 dB, high end -22.7 | -3.3 dB | -2.3 dB | 0 deg |
| no roof | far along the wall | -46.5 dB, high end -26.0 | -18.4 dB | -18.4 dB | 29 deg |
| roofed | off to the side | -35.6 dB, high end -26.0 | none | -6.8 dB | 30 deg |
| roofed | in line with the door | -22.9 dB, high end -22.7 | none | -2.3 dB | 0 deg |
| roofed | far along the wall | -46.5 dB, high end -26.0 | none | -31.9 dB | 64 deg |

Where the air path exists it is 15 to 29 dB louder than the wall path, before
anything is taken off for the bend. What the bend should take off is a
decision and was not measured.

How the engine should choose, as a proposal:

1. The straight line is clear: one path, the sound where it is. No flood is
   asked.
2. The straight line is blocked and the flood has no path within the sound's
   reach: one path, the wall path.
3. Both exist: both are worked out, the louder goes first, and the second is
   dropped when it is more than 20 dB under the first. `SoundPresenter` plays
   only the first path today, so until it plays two, the first is all that is
   heard, and that is nearly always the air path.
4. A path that appears, as when a door opens, starts at no loudness and comes
   up through the smoother. One that goes fades the same way. Neither path's
   loudness ever steps.

## 9. Where it breaks

| Case | What happens | Nuisance or fault |
| --- | --- | --- |
| a gap narrower than a cell | It is open at some positions against the grid and shut at others: a gap 0.7 wide at 75% of them at cell 1. When shut, the sound has the wall path only. | nuisance. The level maker can see no reason for it, so the editor should show the air grid. |
| a wall thinner than a cell | By the centre alone it leaks, at any angle. With link rays it never did. | fault without link rays, none with them |
| a door thinner than a cell | Leaks when only the cells its centre holds are closed. Holds when the steps its hull crosses are closed too. | fault without, none with |
| stairs and ramps | A passage 1.4 by 2.2 sloping at 20, 35 and 45 degrees is passed at every position at cell 0.5 and 1, and at none or half at cell 2. | none at cell 1 |
| the listener in a cell called solid | Of 3,000 head positions in the demo, 0.35 clear of the walls, 0.3% stand in such a cell at cell 1 and 1.2% at cell 2. The flood starts from the neighbours the listener sees by a ray, so it never mattered. Two of the 3,000 are inside the shut door's hull, and there nothing starts. | none |
| a sound in a cell that is shut | Six of the demo's seven sounds sit inside the part that makes them. The door's two are in cells the door closes: "no path" until the reader takes a neighbouring cell that sees the sound and ignores the sound's own part. | fault unless the sound's own part is known |
| cell centres on a surface | Levels are built on a snap grid and so are cell centres. At cell 1, 0.9% of the demo's cells change their answer when the centre moves by 0.01, at cell 2, 4.3%. With the air grid moved by an odd fraction of a cell, none at either size. | nuisance, and free to avoid |
| a very long path | A path longer than the flood's radius is not found. The U corridor's 28.17 is found at radius 30 and not at 28. | none: past the sound's max distance nothing is heard anyway |
| a sound beyond the radius | No answer. The wall path is all there is. | none |
| thousands of brushes | The flood does not see them. 500, 2,000 and 5,000 brushes at one density: 21 to 22 ms kept and 42 to 44 ms the first time, each for about 43,000 cells, with 2.3 rays a cell in all three. What a chunk holds matters, at most 34 brushes here, not what the level holds. | none |
| many parts | 200 parts in a flat list made the flood ten times slower. | fault until parts have a broadphase |
| two routes of one length | The direction jumps from one to the other, up to 9 degrees round the hut. | nuisance, covered by the turn-rate limit |
| a model in the way | Mesh nodes are not in the tree. Sound goes through them, as in the first version of walls. | known gap |

## NativeAOT

The engine ships as NativeAOT, so the whole program was also run once as a
NativeAOT executable. The publish needed nothing beyond the Visual Studio
Installer folder on `PATH`.

| Measurement | JIT, middle of three | NativeAOT, one run |
| --- | --- | --- |
| point, live | 18 ns | 28 ns |
| point, baked | 30 ns | 33 ns |
| ray to the next cell, live | 71 ns | 71 ns |
| ray to the next cell, baked | 162 ns | 92 ns |
| centre point and link rays, a cell, first time, live | 234 ns | 245 ns |
| the same, baked | 513 ns | 324 ns |
| is the straight line blocked, live | 153 ns | 147 ns |
| the same, baked | 378 ns | 216 ns |
| bounded flood, hut at 15, 3,608 cells | 0.90 ms | 0.88 ms |
| bounded flood, demo course, 2,747 cells | 0.99 ms | 1.04 ms |
| flood by radius alone, demo course | 661 ms | 680 ms |
| flood for 11 sounds in the town, kept | 6.06 ms | 6.09 ms |
| the same, first time, live | 12.8 ms | 13.2 ms |
| the same, first time, baked | 22.4 ms | 16.0 ms |
| read a sound | 36 ns | 36 ns |
| refine a first leg | 4.3 us | 4.2 us |
| tighten a first corner | 35 us | 34 us |

The floods cost the same. Points are slower as NativeAOT and everything on a
baked world is faster, so the baked world's first flood is 16 ms and not 22.

The two builds do not give the same bits. Of the 19,008 cell centres of
section 1, 22.9% are in solid under the JIT and 23.1% as NativeAOT. Those are
centres that lie on a surface of the demo's snap grid, which the two builds
round to different sides. A few lengths and angles in the accuracy tables
moved with them, by up to 0.45 units and 0.3 degrees. Whether the carve or
the point test is what differs was not looked into. Within one build, live
and baked agree to the last digit, and so do the three runs.

## Surprises and traps

1. The start room has no roof. Every ray-checked flood sent its sound over the
   wall, and by hand that is the shorter way by 1.8 units from far along the
   wall.
2. A ray can hit a film of solid that no point is in. The spike's first worlds
   were built at an offset of (0.31, 0.17, 0.43), which a float cannot hold. A
   doorway cut flush with a room came out 9.54e-7 from the room's wall, and
   `CsgWorld.Raycast` through the middle of the open doorway reported a hit
   on that face. Of 100,001 points along the same line, none was in solid. At
   an offset of sixteenths, or none, the ray is clear. The link rays shut that
   doorway. A level on a snap grid of binary fractions cannot do this. One
   that is moved or typed in decimals can, and it would catch the wall path's
   span trace as well.
3. A brush put inside a cut is cut away with it: the carve is one set
   expression with no order. The spike's first thin-wall test built its wall
   inside the cut room, so there was no wall and every rule "leaked".
4. Sight along the grid is half a cell too generous, and the first leg is what
   pays for it. See section 2.
5. Breadth first over six neighbours is eight times cheaper a cell than the
   rest and takes the long way round.
6. Summing a grid path's turns gives 127 degrees for a path that bends by 14.
7. The JIT and the NativeAOT build put 0.2% of the demo's cell centres on
   different sides of a surface. A test that pins a cell count would pass in
   one and fail in the other.
8. The demo's sounds sit inside their own parts.
9. A sound with no path is the dear case for "stop when reached". Only reach
   bounds it.
10. Reading the angle between two unit vectors with an arc cosine gives 0.03
    degrees for two that are equal. The live and the baked world looked 0.03
    degrees apart until that was changed to the cross product.

## What this means for the design

The cell size is 1 unit. The demo's doorway and a sloping passage of the same
size are open at every position, the first leg is within 1.4 degrees once it
is found with rays, and half the size costs about six times the cells for
gaps down to 0.5 and under a degree. At 2 units the doorway is shut at a
quarter of its positions.

A cell is air when its centre is not in solid, and two cells are joined when
a ray between their centres is clear. The air grid sits an odd fraction of a
cell off the snap grid. The answers are kept per block of cells until the
world changes there, which `Scene.WorldChangedSince` already answers for a
box. Parts are never kept: they are laid over each flood from a copy, closing
the cells whose centre they hold and the steps their hulls cross.

There is no radius to choose. Each sound's max distance is its reach, and the
flood goes on from a cell only while some blocked sound could still be in
reach by way of it. A budget of 20,000 cells sits on top: 5 to 8 ms from kept
answers at the measured 250 to 380 ns a cell, 12 to 16 ms the first time on a
live world and about 20 ms on a baked one as NativeAOT. A flood that runs into
the budget has no corner path for the sounds it did not reach, and they keep
the wall path.

The algorithm is lazy Theta* over six neighbours, with a cell's sight of the
listener checked by a ray. In the bounded floods it costs 250 to 380 ns a
cell, against about 240 for Dijkstra over 26 neighbours and 30 to 50 for
breadth first. It gives the right route where breadth first does not, it
wanders less along an edge than Dijkstra, and reading a sound out of it is a
lookup.
Breadth first is there if the budget ever has to shrink, at the price of
section 8's table.

The flood runs on a worker, two to five times a second while the listener
moves, and at once for a door, a world change or a lost corner. It reads a
`CsgWorld` it holds a reference to, or a baked world it holds a lease on, and
a copy of the parts. The render thread keeps each blocked sound's few corners
from the last flood and finds the first leg again every frame with rays:
refine always, tighten when there is time, since tightening is where the
1.1 ms for 32 sounds goes. Fewer halving steps would cut that. Not tried.

Outdoors it is bounded by floor and ceiling from the brushes' boxes, by reach
and by stopping when the sounds are reached, in that order of worth. With
those the open field costs what a room costs.

The path becomes the engine's second sound path like this:

- Position: the listener's position plus the first leg's direction times the
  path's length. The direction may turn by at most 180 degrees a second. That
  limit belongs with the path, since `SoundPathSmoother` leaves position
  alone.
- Loudness: `SoundFalloff.Gain` of the path's length, not of the straight
  distance, times what the bend takes off.
- Muffling: what the bend takes off the high end.
- The bend is the sum of the two end angles, held at 180. As a first guess to
  tune by ear, not a measurement: 6 dB and a further 12 dB off the high end
  for every 90 degrees, through `AcousticLoss`, so that bends and walls add up
  in one place. At no bend it takes nothing, so the sound does not step when
  it comes into sight.

A door changes it in three ways. While it moves, the flood is run again at
once and then at its rhythm. The doorway opens a cell at a time, and the
smoother carries the loudness across each step. And the door's own sounds
must not be blocked by the door.

What the real thing needs that the engine does not have:

- `SoundQuery` has a position and two distances and no name for the emitter.
  The corner path needs one: to keep a sound's corners between floods, and to
  know which part the sound sits under.
- A copy of the parts for a worker, with a number that changes when one
  moves, and a broadphase over them.
- A way to hold a baked world while a worker reads it, or to stop the worker
  before `ReleaseCompiledStaticWorld`.
- `SoundPresenter` playing two paths of one sound: two voices on the same
  samples at the same place in them.
- A ray that only says yes or no. `Raycast` works out a hit point and a
  normal that the flood throws away. Not measured how much that is.
- The air grid drawn in the editor, so a doorway that stays shut can be seen.

Still unknown:

- How any of it sounds. Nothing was played. The 180 degrees a second, the bend
  costs and the 20 dB between the two paths are guesses until someone listens.
- Whether two voices on one sound can be kept in step through OpenAL.
- A level with real rooms above one another. Every world here is one storey,
  or open.
- A tuned flood. This one is straightforward C#, with a binary heap and no
  care for the cache.

## Steam Audio: not run

The plan names Steam Audio as the thing to compare against. Its SDK would have
to be downloaded, and nothing was. From the research note alone:

A comparison would need the 4.8.1 SDK, `phonon.dll` bound by hand for
NativeAOT as Box3D is, the same worlds handed over as triangles from the chunk
meshes with the door as an instanced mesh, pathing probes baked for each
world, and then for the same listener positions its path lengths and
directions against the hand cases and its time against the tables here.

What I would expect it to show:

- On the hand cases, path lengths as good as these or better, since its probes
  are not tied to a grid of cells.
- No answer for a wall built a moment ago until the probes are baked again.
  The note quotes its guide: "Since pathing data is baked, the paths assume
  all geometry is static." It can find another way at run time when moving
  geometry blocks a baked path, which would cover a door. An edited level and
  a world with no extents it would not cover.
- No direction to give OpenAL. Its pathing result is a set of Ambisonic
  coefficients and three bands of EQ. A direction would have to be worked out
  of the first-order coefficients, which the note marks as its own inference.

None of this was run.

## Not measured

- Anything by ear.
- A mapped `.scmap`. The baked world here is in memory.
- Linux, and NativeAOT more than once.
- More than one storey, caves, or a path that has to go down and up.
- The under side of the demo's floor. Its slab has open air below it and a
  chasm cut through it. The floor bound is there for that, and how far a
  flood without it spreads down there was not counted.
- A listener that is itself carried, on a lift or in a vehicle.
- A broadphase for parts, a yes-or-no ray, a bucket queue in place of the
  heap, fewer halving steps in the tightening.
- Second-order fast marching, and what Theta* over 26 neighbours costs.
- Meshes as obstacles.

## Files

Everything is under `spikes/sound-corners/`.

| Path | What it is |
| --- | --- |
| `run.ps1` | build and run, three times over or section by section, under the JIT or as NativeAOT |
| `SoundCornersSpike/Grid/AirField.cs`, `CellRule.cs` | which cells are air, by four rules, and the kept answers |
| `SoundCornersSpike/Grid/Flood.cs`, `FloodWindow.cs` | the four floods, the bounds, steps and line of sight on the grid |
| `SoundCornersSpike/Grid/PathReader.cs` | reading a sound: pulling straight, refining, tightening, aiming again |
| `SoundCornersSpike/Grid/WorldHeights.cs` | floor and ceiling from the brushes' boxes |
| `SoundCornersSpike/Probes/` | the live and the baked world behind one set of questions, and parts as hulls |
| `SoundCornersSpike/Worlds/` | the hand cases with their true paths, the demo's course, the hut, the town |
| `SoundCornersSpike/Questions/` | one file per section of this report |
