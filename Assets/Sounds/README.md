# Sounds

The demo's start room plays these. They are placeholders made by a script. Each one is there to be replaced by a recorded sound of the same name.

| File | What it is | Length |
|---|---|---|
| `door_open.wav` | a heavy door sliding open, with a soft stop | 1.0 s |
| `door_close.wav` | the same door sliding shut, with a thud | 1.0 s |
| `lift_move.wav` | an electric hum with a slow beat, looped | 2.0 s |
| `lift_stop.wav` | a clunk | 0.4 s |
| `button_press.wav` | a click in and a softer click out | 0.2 s |
| `room_tone.wav` | quiet air, looped | 4.0 s |

All are mono, 16 bit, 48 kHz. The two looped ones carry a loop region over the whole file.

## Making them again

`make_sounds.py` wrote them. It needs Python 3 and nothing else:

```bash
python Assets/Sounds/make_sounds.py
```

It writes the same bytes on every run, so a change to a sound shows up as a change to its file.

The door in the start room slides for 0.45 seconds. Both door sounds do their moving in that time, and the rest of the second is the stop ringing out. If the door's speed changes, change `DOOR_TRAVEL` in the script.
