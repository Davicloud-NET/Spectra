"""Writes the demo's placeholder sounds beside this file.

Run it with: python Assets/Sounds/make_sounds.py

Mono, 16 bit, 48 kHz, which is the rate the cook writes, so nothing is
resampled. Every run writes the same bytes.
"""

import math
import os
import struct

RATE = 48000
FULL_SCALE = 32767

# The start room's door slides for 27 ticks. The door sounds do their moving
# in that time and ring out after it.
DOOR_TRAVEL = 0.45


class Noise:
    """White noise from a generator of our own, so Python's does not decide the bytes."""

    def __init__(self, seed):
        self.state = seed

    def next(self):
        self.state = (self.state * 6364136223846793005 + 1442695040888963407) & 0xFFFFFFFFFFFFFFFF
        return (self.state >> 11) / float(1 << 52) - 1.0


def white(count, seed):
    noise = Noise(seed)
    return [noise.next() for _ in range(count)]


def low_pass(samples, cutoff):
    step = 1.0 - math.exp(-2.0 * math.pi * cutoff / RATE)
    out = []
    held = 0.0
    for sample in samples:
        held += step * (sample - held)
        out.append(held)
    return out


def high_pass(samples, cutoff):
    low = low_pass(samples, cutoff)
    return [sample - under for sample, under in zip(samples, low)]


def band(samples, low, high):
    return high_pass(low_pass(low_pass(samples, high), high), low)


def mix(*layers):
    return [sum(values) for values in zip(*layers)]


def gain(samples, factor):
    return [sample * factor for sample in samples]


def to_peak(samples, decibels):
    peak = max(abs(sample) for sample in samples)
    return gain(samples, 10.0 ** (decibels / 20.0) / peak)


def thump(count, start, high, low, sweep, decay):
    """A sine that drops from high to low and dies away: something heavy landing."""
    out = [0.0] * count
    first = int(start * RATE)
    phase = 0.0
    for i in range(first, count):
        t = (i - first) / RATE
        frequency = low + (high - low) * math.exp(-t / sweep)
        out[i] = math.sin(phase) * math.exp(-t / decay)
        phase += 2.0 * math.pi * frequency / RATE
    return out


def burst(count, start, noise, attack, decay):
    """A short shaped piece of noise: the hard part of a knock or a click."""
    out = [0.0] * count
    first = int(start * RATE)
    for i in range(first, count):
        t = (i - first) / RATE
        out[i] = noise[i] * min(t / attack, 1.0) * math.exp(-t / decay)
    return out


def played_once(samples, decibels, fade):
    """A sound that is not a loop starts and ends on zero, or it clicks."""
    out = to_peak(high_pass(samples, 20.0), decibels)
    count = int(fade * RATE)
    for i in range(count):
        out[-1 - i] *= i / count
    return out


def door(opening, seed):
    count = RATE
    rumble = to_peak(band(white(count, seed), 30.0, 150.0), 0.0)

    # Small speakers play nothing as low as the rumble. This band is for them.
    scrape = to_peak(band(white(count, seed + 1), 220.0, 540.0), 0.0)

    slide = [0.0] * count
    phase = 0.0
    for i in range(count):
        t = i / RATE
        along = min(t / DOOR_TRAVEL, 1.0)

        # Rises a little on the way open, falls on the way shut.
        frequency = 54.0 + 12.0 * (along if opening else 1.0 - along)
        motor = math.sin(phase) + 0.5 * math.sin(2.0 * phase) + 0.3 * math.sin(3.0 * phase)
        phase += 2.0 * math.pi * frequency / RATE

        if t < DOOR_TRAVEL:
            level = min(t / 0.06, 1.0)
        else:
            level = math.exp(-(t - DOOR_TRAVEL) / 0.04)

        roll = 1.0 + 0.12 * math.sin(2.0 * math.pi * 9.0 * t)
        slide[i] = level * roll * (0.8 * rumble[i] + 0.4 * scrape[i] + 0.25 * motor)

    if opening:
        stop = gain(thump(count, DOOR_TRAVEL, 80.0, 52.0, 0.03, 0.07), 0.3)
    else:
        knock = to_peak(low_pass(white(count, seed + 2), 700.0), 0.0)
        stop = mix(
            thump(count, DOOR_TRAVEL, 100.0, 44.0, 0.03, 0.11),
            gain(burst(count, DOOR_TRAVEL, knock, 0.001, 0.02), 0.6))

    return played_once(mix(slide, stop), -6.0, 0.03)


def lift_move():
    count = 2 * RATE

    # Whole cycles in two seconds, so the end runs into the start. The pairs
    # one cycle a second apart are the slow beat.
    partials = [(100, 1.0), (101, 0.3), (200, 0.5), (201, 0.15), (300, 0.25), (400, 0.1)]

    out = []
    for i in range(count):
        value = 0.0
        for frequency, level in partials:
            value += level * math.sin(2.0 * math.pi * ((frequency * i) % RATE) / RATE)
        out.append(value)

    return to_peak(out, -12.0)


def lift_stop():
    count = int(0.4 * RATE)
    clack = to_peak(band(white(count, 31), 600.0, 1600.0), 0.0)
    out = mix(
        thump(count, 0.002, 125.0, 66.0, 0.02, 0.05),
        gain(burst(count, 0.002, clack, 0.0005, 0.012), 0.9),
        gain(thump(count, 0.075, 170.0, 110.0, 0.01, 0.025), 0.3))

    return played_once(out, -6.0, 0.02)


def button_press():
    count = int(0.2 * RATE)
    tick = to_peak(band(white(count, 41), 900.0, 3500.0), 0.0)

    def click(start, pitch, level):
        return gain(
            mix(
                thump(count, start, pitch, pitch * 0.8, 0.004, 0.005),
                gain(burst(count, start, tick, 0.0003, 0.0025), 0.4)),
            level)

    # In, and softer on the way out.
    out = mix(click(0.004, 1500.0, 1.0), click(0.115, 1150.0, 0.45))
    return played_once(out, -6.0, 0.01)


def room_tone():
    count = 4 * RATE
    overlap = RATE // 2
    settle = RATE // 4

    air = band(white(settle + count + overlap, 51), 70.0, 600.0)[settle:]

    # Noise has no cycle to end on. The half second past the end is faded
    # out under the start, so the last sample runs into the first.
    out = air[:count]
    for i in range(overlap):
        turn = 0.5 * math.pi * i / overlap
        out[i] = air[i] * math.sin(turn) + air[count + i] * math.cos(turn)

    return to_peak(out, -30.0)


def chunk(name, body):
    padding = b"\x00" if len(body) % 2 else b""
    return name + struct.pack("<I", len(body)) + body + padding


def loop_chunk(frames):
    """One forward loop over the whole sound. Its end is the last frame, not one past it."""
    nanoseconds_a_frame = 1000000000 // RATE
    header = struct.pack("<9I", 0, 0, nanoseconds_a_frame, 60, 0, 0, 0, 1, 0)
    loop = struct.pack("<6I", 0, 0, 0, frames - 1, 0, 0)
    return chunk(b"smpl", header + loop)


def write(name, samples, looped=False):
    whole = [int(round(sample * FULL_SCALE)) for sample in samples]

    # The wave module cannot write a loop, so the chunks are written here.
    form = struct.pack("<HHIIHH", 1, 1, RATE, RATE * 2, 2, 16)
    body = chunk(b"fmt ", form) + chunk(b"data", struct.pack("<%dh" % len(whole), *whole))
    if looped:
        body += loop_chunk(len(whole))

    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), name)
    with open(path, "wb") as file:
        file.write(b"RIFF" + struct.pack("<I", 4 + len(body)) + b"WAVE" + body)


def main():
    write("door_open.wav", door(opening=True, seed=11))
    write("door_close.wav", door(opening=False, seed=21))
    write("lift_move.wav", lift_move(), looped=True)
    write("lift_stop.wav", lift_stop())
    write("button_press.wav", button_press())
    write("room_tone.wav", room_tone(), looped=True)


if __name__ == "__main__":
    main()
