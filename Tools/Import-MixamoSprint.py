#!/usr/bin/env python3
"""Retarget the Xbot Mixamo run to this checkout and write a local Unity clip."""

import argparse
import hashlib
import json
import math
import os
import re
import struct
import tempfile
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Assets/HeavyPockets/Resources"
DESTINATION = RESOURCES / "RunnerPrivate/Sprint.anim"
URL = "https://raw.githubusercontent.com/mrdoob/three.js/r181/examples/models/gltf/Xbot.glb"
DIGEST = "002f8d269de68e5dce3d25195caf390d1aa359bbfaae3fcf4c8dc78ec36c3ba5"
IDENTITY = (0, 0, 0, 1)
FACE_FORWARD = (0, 1, 0, 0)  # Unity locomotion runs toward +Z.


def multiply(a, b):
    x, y, z, w = a
    i, j, k, s = b
    return (
        w * i + x * s + y * k - z * j,
        w * j - x * k + y * s + z * i,
        w * k + x * j - y * i + z * s,
        w * s - x * i - y * j - z * k,
    )


def inverse(q):
    return (-q[0], -q[1], -q[2], q[3])


def normalize(q):
    length = math.sqrt(sum(x * x for x in q))
    return tuple(x / length for x in q)


def rotate(q, point):
    return multiply(multiply(q, (*point, 0)), inverse(q))[:3]


def mirror_z(q):
    # The source character faces +Z; the runner prefab faces -Z with the same up and left.
    return (-q[0], -q[1], q[2], q[3])


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def runner_rig():
    text = (RESOURCES / "Runner/Character.prefab").read_text()
    objects, transforms = {}, {}

    def vector(block, field):
        line = re.search(r"^  " + field + r": \{([^}]+)\}", block, re.MULTILINE)[1]
        return tuple(float(x.split(":")[1]) for x in line.split(","))

    for block in text.split("--- !u!"):
        header = re.match(r"(1|4) &(\d+)\n", block)
        if not header:
            continue
        kind, identifier = header.groups()
        if kind == "1":
            objects[identifier] = re.search(r"^  m_Name: (.+)$", block, re.MULTILINE)[1]
        else:
            parent = re.search(r"m_Father: \{fileID: (\d+)", block)[1]
            obj = re.search(r"m_GameObject: \{fileID: (\d+)", block)[1]
            transforms[identifier] = (
                obj,
                parent,
                normalize(vector(block, "m_LocalRotation")),
                vector(block, "m_LocalPosition"),
            )

    rig = {}

    def read(identifier):
        obj, parent, rotation, position = transforms[identifier]
        name = objects[obj]
        if name not in rig:
            if parent == "0":
                rig[name] = ("", IDENTITY, position, None)
            else:
                parent_name = read(parent)
                path, basis, origin, _ = rig[parent_name]
                rig[name] = (
                    (path + "/" + name).lstrip("/"),
                    normalize(multiply(basis, rotation)),
                    add(origin, rotate(basis, position)),
                    parent_name,
                )
        return name

    for identifier in transforms:
        read(identifier)
    return rig


def source_take(data):
    if hashlib.sha256(data).hexdigest() != DIGEST:
        raise ValueError("Unexpected Xbot.glb revision. Use the pinned three.js r181 file.")
    json_length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20 : 20 + json_length])
    binary = memoryview(data)[28 + json_length :]

    def samples(index):
        accessor = doc["accessors"][index]
        view = doc["bufferViews"][accessor["bufferView"]]
        components = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[accessor["type"]]
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        stride = view.get("byteStride", components * 4)
        return [
            struct.unpack_from("<" + "f" * components, binary, offset + i * stride)
            for i in range(accessor["count"])
        ]

    take = next(item for item in doc["animations"] if item["name"] == "run")
    times = [value[0] for value in samples(take["samplers"][0]["input"])]
    tracks = {}
    for channel in take["channels"]:
        node = doc["nodes"][channel["target"]["node"]]
        kind = channel["target"]["path"]
        if kind in ("rotation", "translation"):
            frames = samples(take["samplers"][channel["sampler"]]["output"])
            tracks[node["name"].split(":")[-1], kind] = frames
    return times, tracks, {n["name"].split(":")[-1]: n for n in doc["nodes"]}


def bone_mapping():
    mapping = {
        "Hips": "pelvis",
        "Spine": "spine_01",
        "Spine1": "spine_02",
        "Spine2": "spine_03",
        "Neck": "neck_01",
        "Head": "Head",
    }
    for side, suffix in (("Left", "_l"), ("Right", "_r")):
        for src, dst in (
            ("UpLeg", "thigh"),
            ("Leg", "calf"),
            ("Foot", "foot"),
            ("ToeBase", "ball"),
            ("Shoulder", "clavicle"),
            ("Arm", "upperarm"),
            ("ForeArm", "lowerarm"),
            ("Hand", "hand"),
        ):
            mapping[side + src] = dst + suffix
        for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
            for number in range(1, 4):
                mapping[f"{side}Hand{finger}{number}"] = f"{finger.lower()}_0{number}{suffix}"
    return mapping


def retarget(data):
    times, tracks, nodes = source_take(data)
    rig = runner_rig()
    # All source limb rest axes are world-aligned in the pinned Xbot T-pose.
    # Convert the local delta through the runner's parent rest frame.
    curves = {"Rotation": {}, "Position": {}}
    for src, dst in bone_mapping().items():
        path, basis, _, parent = rig[dst]
        parent_basis = rig[parent][1]
        src_bind = normalize(nodes[src].get("rotation", IDENTITY))
        if abs(src_bind[3]) < 0.99999:
            raise ValueError("Source rig no longer has the expected rest axes.")
        frames = []
        for source in tracks[src, "rotation"]:
            delta = mirror_z(multiply(normalize(source), inverse(src_bind)))
            if src == "Hips":
                delta = multiply(FACE_FORWARD, delta)
            value = normalize(multiply(multiply(inverse(parent_basis), delta), basis))
            if frames and sum(a * b for a, b in zip(frames[-1], value)) < 0:
                value = tuple(-x for x in value)
            frames.append(value)
        curves["Rotation"][path] = frames

    path, _, rest, parent = rig["pelvis"]
    _, basis, origin, _ = rig[parent]
    hips = nodes["Hips"]["translation"]
    scale = rest[1] / hips[1]
    frames = []
    for pos in tracks["Hips", "translation"]:
        # Keep the run in place; movement speed and heading belong to the controller.
        world = (-rest[0] - (pos[0] - hips[0]) * scale, pos[1] * scale, -rest[2])
        frames.append(rotate(inverse(basis), tuple(a - b for a, b in zip(world, origin))))
    curves["Position"][path] = frames
    # The source frame at 8/30 s has the same planted left forefoot as the jog at
    # phase zero. Align all channels so blending from jog preserves foot contacts.
    for channels in curves.values():
        for path, frames in channels.items():
            shifted = frames[8:-1] + frames[:8]
            channels[path] = shifted + [shifted[0]]
    return times, curves


def write_clip(data):
    times, curves = retarget(data)
    # Reuse the project's native Unity clip header and binding settings.
    template = (RESOURCES / "QuaterniusMotion/Sprint.anim").read_text()
    header = template.split("  m_RotationCurves:")[0]
    settings = template[template.index("  m_ScaleCurves:") : template.index("  m_EditorCurves:")]
    settings = re.sub(r"m_StopTime: [^\n]+", f"m_StopTime: {times[-1]:.8g}", settings)
    settings = settings.replace("m_SampleRate: 60", "m_SampleRate: 30")
    lines = [header.rstrip()]

    def formatted(value):
        if len(value) == 1:
            return f"{value[0]:.9g}"
        return "{" + ", ".join(f"{key}: {v:.9g}" for key, v in zip("xyzw", value)) + "}"

    def keys(indent, frames):
        lines.extend([indent + "  serializedVersion: 2", indent + "  m_Curve:"])
        for i, (t, value) in enumerate(zip(times, frames)):
            left, right = max(0, i - 1), min(len(times) - 1, i + 1)
            interval = times[right] - times[left]
            if i == 0 or i == len(times) - 1:
                left, right = len(times) - 2, 1
                interval = times[1] - times[0] + times[-1] - times[-2]
            slope = formatted([(b - a) / interval for a, b in zip(frames[left], frames[right])])
            lines.extend(
                [
                    indent + "  - serializedVersion: 3",
                    indent + f"    time: {t:.9g}",
                    indent + "    value: " + formatted(value),
                    indent + "    inSlope: " + slope,
                    indent + "    outSlope: " + slope,
                    indent + "    tangentMode: 0",
                    indent + "    weightedMode: 0",
                ]
            )
        lines.extend(
            [indent + "  m_PreInfinity: 2", indent + "  m_PostInfinity: 2", indent + "  m_RotationOrder: 4"]
        )

    for kind, tracks in curves.items():
        lines.append("  m_" + kind + "Curves:")
        for path, frames in tracks.items():
            lines.append("  - curve:")
            keys("    ", frames)
            lines.append("    path: " + path)
        if kind == "Rotation":
            lines.extend(["  m_CompressedRotationCurves: []", "  m_EulerCurves: []"])
    lines.extend([settings.rstrip(), "  m_EditorCurves:"])
    for kind, tracks in curves.items():
        for path, frames in tracks.items():
            for component, axis in enumerate("xyzw"[: len(frames[0])]):
                lines.extend(["  - serializedVersion: 2", "    curve:"])
                keys("    ", [(frame[component],) for frame in frames])
                lines.extend(
                    [
                        f"    attribute: m_Local{kind}.{axis}",
                        "    path: " + path,
                        "    classID: 4",
                        "    script: {fileID: 0}",
                        "    flags: 0",
                    ]
                )
    lines.extend(
        [
            "  m_EulerEditorCurves: []",
            "  m_HasGenericRootTransform: 0",
            "  m_HasMotionFloatCurves: 0",
            "  m_Events: []",
            "",
        ]
    )
    return "\n".join(lines)


def install(source=None):
    if DESTINATION.is_file():
        return
    if source:
        data = Path(source).read_bytes()
    else:
        with urlopen(URL, timeout=15) as response:
            data = response.read(3_000_000)
    content = write_clip(data)
    DESTINATION.parent.mkdir(exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=DESTINATION.parent, suffix=".tmp", delete=False, mode="w") as out:
        temporary = Path(out.name)
    try:
        temporary.write_text(content, encoding="utf-8")
        os.replace(temporary, DESTINATION)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Installed the Mixamo run in {DESTINATION}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", help="Use a local r181 Xbot.glb instead of downloading it")
    try:
        install(parser.parse_args().source)
    except (OSError, ValueError) as error:
        parser.exit(1, str(error) + "\n")
