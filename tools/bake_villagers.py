"""Generates DeskCiv's original villager models, tools and work animations.

Usage: python -I tools/bake_villagers.py
Output: game/assets/characters
"""
import math
import struct
from pathlib import Path

import numpy as np

OUTPUT = Path(__file__).resolve().parent.parent / "game" / "assets" / "characters"
FPS = 24

NAMES = ["hips", "spine", "chest", "neck", "head", "armL", "foreL", "handL", "armR", "foreR", "handR",
         "legL", "shinL", "footL", "legR", "shinR", "footR"]
J = {n: i for i, n in enumerate(NAMES)}
PARENT = [-1, 0, 1, 2, 3, 2, 5, 6, 2, 8, 9, 0, 11, 12, 0, 14, 15]
PIVOT = np.array([
    (0, .95, 0), (0, 1.08, 0), (0, 1.26, 0), (0, 1.47, 0), (0, 1.54, 0),
    (.215, 1.42, 0), (.245, 1.14, 0), (.26, .89, .01),
    (-.215, 1.42, 0), (-.245, 1.14, 0), (-.26, .89, .01),
    (.1, .93, 0), (.105, .52, 0), (.105, .1, 0),
    (-.1, .93, 0), (-.105, .52, 0), (-.105, .1, 0),
], float)

SKIN = (.86, .66, .5)
DARK = (.1, .08, .07)
LEATHER = (.33, .21, .12)
WOOD = (.5, .33, .18)
STEEL = (.72, .74, .76)
STONE = (.53, .52, .49)
STRAW = (.86, .72, .36)


class Mesh:
    def __init__(self):
        self.p, self.n, self.c, self.j = [], [], [], []

    def tri(self, joint, inside, a, b, c, color):
        a, b, c = np.array(a, float), np.array(b, float), np.array(c, float)
        normal = np.cross(c - a, b - a)
        size = np.linalg.norm(normal)
        if size < 1e-9:
            return
        normal /= size
        if np.dot(normal, (a + b + c) / 3 - inside) < 0:
            b, c = c, b
            normal = -normal
        for v in (a, b, c):
            self.p.append(v); self.n.append(normal); self.c.append(color); self.j.append(joint)

    def quad(self, joint, inside, a, b, c, d, color):
        self.tri(joint, inside, a, b, c, color)
        self.tri(joint, inside, a, c, d, color)

    def tube(self, joint, a, b, ra, rb, color, sides=7, cap=True):
        a, b = np.array(a, float), np.array(b, float)
        ra = (ra, ra) if np.isscalar(ra) else ra
        rb = (rb, rb) if np.isscalar(rb) else rb
        axis = b - a
        axis /= np.linalg.norm(axis)
        u = np.cross(axis, (0, 0, 1)) if abs(axis[2]) < .9 else np.cross(axis, (1, 0, 0))
        u /= np.linalg.norm(u)
        v = np.cross(axis, u)
        if abs(axis[1]) > .7:
            u, v = np.array((1., 0, 0)), np.array((0, 0, 1.))
        inside = (a + b) / 2
        ring = lambda center, r, k: center + u * math.cos(k * math.tau / sides) * r[0] + v * math.sin(k * math.tau / sides) * r[1]
        for k in range(sides):
            self.quad(joint, inside, ring(a, ra, k), ring(a, ra, k + 1), ring(b, rb, k + 1), ring(b, rb, k), color)
            if cap:
                self.tri(joint, inside, a, ring(a, ra, k), ring(a, ra, k + 1), color)
                self.tri(joint, inside, b, ring(b, rb, k), ring(b, rb, k + 1), color)

    def ball(self, joint, center, radius, color, lon=9, lat=6, vmin=-1.0, vmax=1.0):
        c = np.array(center, float)
        r = np.array(radius, float)
        low, high = math.asin(vmin), math.asin(vmax)
        def point(i, k):
            phi = low + (high - low) * k / lat
            theta = i * math.tau / lon
            return c + r * np.array((math.cos(phi) * math.cos(theta), math.sin(phi), math.cos(phi) * math.sin(theta)))
        for i in range(lon):
            for k in range(lat):
                self.quad(joint, c, point(i, k), point(i + 1, k), point(i + 1, k + 1), point(i, k + 1), color)

    def box(self, joint, center, size, color, tilt=0.0):
        c = np.array(center, float)
        h = np.array(size, float) / 2
        co, si = math.cos(tilt), math.sin(tilt)
        def corner(x, y, z):
            q = np.array((x * h[0], y * h[1], z * h[2]))
            return c + np.array((q[0], q[1] * co - q[2] * si, q[1] * si + q[2] * co))
        for s in (-1, 1):
            self.quad(joint, c, corner(s, -1, -1), corner(s, 1, -1), corner(s, 1, 1), corner(s, -1, 1), color)
            self.quad(joint, c, corner(-1, s, -1), corner(1, s, -1), corner(1, s, 1), corner(-1, s, 1), color)
            self.quad(joint, c, corner(-1, -1, s), corner(1, -1, s), corner(1, 1, s), corner(-1, 1, s), color)

    def write(self, name):
        count = len(self.p)
        joints = np.zeros((count, 4)); joints[:, 0] = self.j
        weights = np.zeros((count, 4)); weights[:, 0] = 1
        with (OUTPUT / f"{name}.dcmesh").open("wb") as f:
            f.write(struct.pack("<ii", count, count))
            for array in (self.p, self.n, self.c, joints, weights):
                f.write(np.asarray(array, dtype="<f4").tobytes())
            f.write(np.arange(count, dtype="<i4").tobytes())
        return count // 3


OUTFITS = {
    "laborer": dict(shirt=(.55, .42, .28), legs=(.34, .3, .25), hair=(.27, .17, .1), hat="cap", hat_color=(.42, .3, .2), beard=True),
    "woman": dict(shirt=(.62, .27, .25), legs=(.5, .22, .22), hair=(.42, .24, .12), dress=True, apron=(.86, .82, .7), scarf=(.85, .78, .6)),
    "woodcutter": dict(shirt=(.26, .42, .25), legs=(.33, .25, .17), hair=(.2, .13, .08), hat="hood", hat_color=(.2, .34, .2), beard=True, belt=LEATHER),
    "farmer": dict(shirt=(.82, .74, .54), legs=(.42, .33, .2), hair=(.5, .34, .16), hat="straw", hat_color=STRAW),
    "fisher": dict(shirt=(.25, .39, .55), legs=(.3, .32, .36), hair=(.3, .28, .25), hat="cap", hat_color=(.16, .27, .42), beard=True),
    "miner": dict(shirt=(.4, .38, .36), legs=(.24, .23, .22), hair=(.12, .1, .09), hat="band", hat_color=(.62, .22, .16), beard=True, belt=LEATHER),
    "builder": dict(shirt=(.74, .47, .18), legs=(.32, .27, .22), hair=(.32, .2, .1), hat="cap", hat_color=(.62, .38, .13), apron=(.42, .29, .17)),
    "baker": dict(shirt=(.9, .88, .8), legs=(.56, .52, .46), hair=(.42, .3, .18), hat="baker", hat_color=(.95, .94, .9), apron=(.97, .96, .92)),
    "child": dict(shirt=(.72, .58, .3), legs=(.45, .36, .24), hair=(.55, .36, .16)),
    "elder": dict(shirt=(.46, .42, .5), legs=(.34, .32, .36), hair=(.82, .82, .8), beard=True, beard_color=(.86, .86, .84), dress=True),
}


def person(o):
    m = Mesh()
    shirt, legs, hair = o["shirt"], o["legs"], o["hair"]
    for side, upper, shin, foot in ((1, "legL", "shinL", "footL"), (-1, "legR", "shinR", "footR")):
        x = .1 * side
        m.tube(J[upper], (x, .95, 0), (x * 1.05, .52, 0), (.098, .105), (.072, .078), legs)
        m.tube(J[shin], (x * 1.05, .54, 0), (x * 1.05, .13, -.005), (.072, .08), (.052, .058), legs)
        m.tube(J[foot], (x * 1.05, .2, -.005), (x * 1.05, .055, -.005), .066, .074, LEATHER)
        m.box(J[foot], (x * 1.05, .045, .075), (.125, .09, .27), LEATHER)
    m.tube(J["hips"], (0, .9, 0), (0, 1.12, 0), (.175, .125), (.17, .118), shirt, 9)
    m.tube(J["spine"], (0, 1.08, 0), (0, 1.3, 0), (.17, .118), (.205, .13), shirt, 9)
    m.tube(J["chest"], (0, 1.26, 0), (0, 1.47, 0), (.205, .132), (.15, .1), shirt, 9)
    bottom = .24 if o.get("dress") else .66
    m.tube(J["hips"], (0, .98, 0), (0, bottom, 0), (.18, .13), (.29 if o.get("dress") else .225, .22 if o.get("dress") else .17), shirt, 10)
    m.tube(J["hips"], (0, .93, 0), (0, 1.0, 0), (.185, .135), (.185, .135), o.get("belt", DARK), 9)
    m.box(J["hips"], (0, .95, .135), (.075, .06, .025), (.66, .58, .36))
    if "apron" in o:
        m.box(J["hips"], (0, .73 if not o.get("dress") else .6, .185), (.28, .46 if not o.get("dress") else .68, .025), o["apron"], -.1)
        m.box(J["chest"], (0, 1.3, .135), (.2, .24, .02), o["apron"])
    if "scarf" in o:
        m.tube(J["chest"], (0, 1.42, 0), (0, 1.49, 0), (.15, .11), (.1, .09), o["scarf"])
    m.tube(J["neck"], (0, 1.45, 0), (0, 1.58, .01), .055, .05, SKIN)
    head = (0, 1.67, .015)
    m.ball(J["head"], head, (.118, .14, .128), SKIN)
    m.box(J["head"], (0, 1.655, .145), (.035, .055, .05), (.8, .58, .44))
    for s in (1, -1):
        m.box(J["head"], (s * .046, 1.695, .128), (.03, .026, .018), DARK)
        m.box(J["head"], (s * .046, 1.725, .13), (.05, .012, .014), hair)
        m.ball(J["head"], (s * .118, 1.665, .01), (.018, .036, .028), SKIN, 5, 3)
    m.ball(J["head"], (0, 1.69, -.005), (.128, .14, .14), hair, 10, 4, vmin=.05)
    m.box(J["head"], (0, 1.65, -.115), (.21, .17, .05), hair)
    if o.get("dress") and "elder" not in str(o.get("shirt")):
        m.tube(J["head"], (0, 1.62, -.13), (0, 1.32, -.16), .06, .035, hair)
    if o.get("beard"):
        color = o.get("beard_color", hair)
        m.box(J["head"], (0, 1.585, .105), (.15, .1, .07), color)
        m.box(J["head"], (0, 1.625, .135), (.09, .018, .02), color)
    hat = o.get("hat")
    if hat == "straw":
        m.tube(J["head"], (0, 1.765, 0), (0, 1.785, 0), .3, .28, o["hat_color"], 12)
        m.tube(J["head"], (0, 1.775, 0), (0, 1.9, 0), .14, .08, o["hat_color"], 10)
        m.tube(J["head"], (0, 1.785, 0), (0, 1.815, 0), .143, .135, (.55, .25, .18), 10)
    elif hat == "cap":
        m.ball(J["head"], (0, 1.72, 0), (.135, .12, .145), o["hat_color"], 10, 4, vmin=.05)
        m.box(J["head"], (0, 1.735, .15), (.17, .018, .09), o["hat_color"])
    elif hat == "hood":
        m.ball(J["head"], (0, 1.69, -.012), (.15, .165, .155), o["hat_color"], 10, 5, vmin=-.25)
        m.tube(J["chest"], (0, 1.38, -.02), (0, 1.5, -.02), (.19, .14), (.13, .12), o["hat_color"])
    elif hat == "band":
        m.tube(J["head"], (0, 1.71, .01), (0, 1.75, .01), (.133, .145), (.13, .142), o["hat_color"], 10)
    elif hat == "baker":
        m.tube(J["head"], (0, 1.74, 0), (0, 1.8, 0), .13, .13, o["hat_color"], 10)
        m.ball(J["head"], (0, 1.87, 0), (.165, .095, .165), o["hat_color"], 10, 4)
    for side, arm, fore, hand in ((1, "armL", "foreL", "handL"), (-1, "armR", "foreR", "handR")):
        m.ball(J[arm], (.215 * side, 1.42, 0), (.08, .08, .082), shirt, 7, 4)
        m.tube(J[arm], (.215 * side, 1.43, 0), (.245 * side, 1.14, 0), .072, .056, shirt)
        m.tube(J[fore], (.245 * side, 1.15, 0), (.26 * side, .92, .01), .057, .043, shirt if o.get("dress") else SKIN)
        m.ball(J[hand], (.262 * side, .865, .012), (.047, .062, .05), SKIN, 6, 4)
    return m


def handle(m, joint, x, length, radius=.024, color=WOOD):
    m.tube(joint, (x, .87, -.16), (x, .87, length), radius, radius * .9, color, 6)


def tools():
    r, l, xr, xl = J["handR"], J["handL"], -.262, .262
    out = {}
    axe = Mesh(); handle(axe, r, xr, .86, .027)
    axe.box(r, (xr, .87, .78), (.05, .13, .13), DARK)
    axe.box(r, (xr, .96, .8), (.03, .25, .18), STEEL)
    axe.box(r, (xr, 1.08, .8), (.018, .05, .25), (.86, .88, .9))
    out["axe"] = axe
    hoe = Mesh(); handle(hoe, r, xr, 1.2)
    hoe.box(r, (xr, .8, 1.19), (.2, .16, .03), STEEL)
    hoe.box(r, (xr, .86, 1.17), (.05, .05, .07), DARK)
    out["hoe"] = hoe
    pick = Mesh(); handle(pick, r, xr, .98, .028)
    pick.tube(r, (xr, .87, .95), (xr, 1.14, 1.0), .035, .008, STEEL, 5)
    pick.tube(r, (xr, .87, .95), (xr, .6, 1.0), .035, .008, STEEL, 5)
    pick.box(r, (xr, .87, .95), (.07, .08, .08), DARK)
    out["pick"] = pick
    hammer = Mesh(); handle(hammer, r, xr, .54, .024)
    hammer.box(r, (xr, .87, .54), (.1, .24, .12), STONE)
    hammer.box(r, (xr, .87, .54), (.112, .05, .132), LEATHER)
    out["hammer"] = hammer
    rod = Mesh(); rod.tube(r, (xr, .87, -.12), (xr, .87, 2.1), .017, .006, WOOD, 5)
    rod.tube(r, (xr, .87, 2.1), (xr, -.62, 2.14), .004, .004, (.9, .9, .86), 3)
    rod.ball(r, (xr, .05, 2.12), (.03, .04, .03), (.86, .22, .16), 6, 3)
    out["rod"] = rod
    sickle = Mesh(); sickle.tube(r, (xr, .87, -.06), (xr, .87, .2), .024, .02, WOOD, 6)
    for i in range(7):
        a0, a1 = i / 7 * 3.4 - .4, (i + 1) / 7 * 3.4 - .4
        p0 = (xr, .99 + math.sin(a0) * .17, .33 + math.cos(a0) * .15)
        p1 = (xr, .99 + math.sin(a1) * .17, .33 + math.cos(a1) * .15)
        sickle.tube(r, p0, p1, .017, .013, STEEL, 4)
    out["sickle"] = sickle
    basket = Mesh()
    basket.tube(l, (xl, .62, .12), (xl, .8, .12), .15, .19, STRAW, 9)
    basket.tube(l, (xl, .79, .12), (xl, .82, .12), .195, .195, (.62, .46, .22), 9)
    basket.ball(l, (xl, .81, .12), (.16, .05, .16), (.66, .2, .3), 8, 3)
    basket.tube(l, (xl - .17, .81, .12), (xl, .92, .12), .012, .012, LEATHER, 4)
    basket.tube(l, (xl + .17, .81, .12), (xl, .92, .12), .012, .012, LEATHER, 4)
    out["basket"] = basket
    pestle = Mesh(); handle(pestle, r, xr, .84, .032)
    pestle.ball(r, (xr, .87, .86), (.07, .07, .11), WOOD, 7, 4)
    out["pestle"] = pestle
    return out


def rx(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rz(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def ry(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def smooth(v):
    v = max(0., min(1., v)); return v * v * (3 - 2 * v)


def lerp(a, b, t):
    return a + (b - a) * t


def swing(u, peak=.6, hit=.72):
    if u < peak:
        return smooth(u / peak)
    if u < hit:
        return 1 - ((u - peak) / (hit - peak)) ** 2
    return 0.


def stance(p, a=13):
    p["legL"] = (-a, 3); p["legR"] = (a, -3); p["shinL"] = (6, 0); p["shinR"] = (10, 0); p["footL"] = (7, 0); p["footR"] = (-14, 0)
    return p


def strike(arm, elbow, lean, twist=0.):
    p = stance({})
    p["spine"] = (lean * .45, 0); p["chest"] = (lean * .55, 0)
    p["armR"] = (-arm, 8); p["foreR"] = (-elbow, 0)
    p["armL"] = (-arm * .92, -24); p["foreL"] = (-elbow - 14, 0)
    p["_turn"] = twist
    p["head"] = (-lean * .5, 0)
    return p


def idle(u):
    w = math.sin(u * math.tau)
    return {"chest": (w * 1.2, 0), "armL": (w * 1.5, -5), "armR": (-w * 1.5, 5), "foreL": (-10, 0), "foreR": (-10, 0), "head": (-w, 0), "_turn_head": math.sin(u * math.tau * .5) * 10}


def legs_walk(p, u, stride=31):
    w = math.sin(u * math.tau)
    p["legL"] = (-stride * w, 0); p["legR"] = (stride * w, 0)
    p["shinL"] = (max(0, -math.cos(u * math.tau + .35)) * 54 + 4, 0)
    p["shinR"] = (max(0, math.cos(u * math.tau + .35)) * 54 + 4, 0)
    p["footL"] = (w * 12, 0); p["footR"] = (-w * 12, 0)
    p["_dy"] = -abs(math.cos(u * math.tau)) * .022
    return p


def walk(u):
    w = math.sin(u * math.tau)
    return legs_walk({"armL": (24 * w, -5), "armR": (-24 * w, 5), "foreL": (-22, 0), "foreR": (-22, 0), "spine": (4, 0)}, u)


def carry(u):
    return legs_walk({"armL": (-54, -8), "armR": (-54, 8), "foreL": (-48, 0), "foreR": (-48, 0), "spine": (7, 0), "chest": (-3, 0)}, u, 24)


def chop(u):
    s = swing(u)
    return strike(lerp(42, 168, s), lerp(14, 42, s), lerp(30, -10, s), lerp(14, -24, s))


def hoe(u):
    s = swing(u, .55, .7)
    return strike(lerp(30, 122, s), lerp(18, 48, s), lerp(40, 6, s), 8)


def mine(u):
    s = swing(u, .58, .68)
    return strike(lerp(48, 176, s), lerp(10, 38, s), lerp(36, -8, s), lerp(8, -14, s))


def build(u):
    s = swing((u * 2) % 1, .6, .74)
    p = stance({"spine": (10, 0), "chest": (lerp(18, 4, s), 0)})
    p.update(armR=(-lerp(52, 148, s), 10), foreR=(-lerp(18, 52, s), 0), armL=(-58, -16), foreL=(-52, 0), head=(-8, 0))
    return p


def sow(u):
    w = math.sin(u * math.tau)
    p = legs_walk({"spine": (9, 0)}, u * 2 % 1, 7)
    p.update(armL=(-34, -14), foreL=(-72, 0), armR=(-lerp(18, 96, (w + 1) / 2), lerp(30, -12, (w + 1) / 2)), foreR=(-20, 0))
    p["_turn"] = w * 12
    return p


def gather(u):
    w = math.sin(u * math.tau)
    p = {"legL": (-66, 4), "legR": (-30, -4), "shinL": (82, 0), "shinR": (62, 0), "footL": (-16, 0), "footR": (-30, 0),
         "spine": (24, 0), "chest": (26, 0), "head": (-22, 0), "armL": (-36, -14), "foreL": (-66, 0),
         "armR": (-lerp(44, 92, (w + 1) / 2), 8), "foreR": (-lerp(6, 34, (w + 1) / 2), 0), "_dy": -.235}
    return p


def reap(u):
    s = swing(u, .55, .74)
    p = {"legL": (-34, 5), "legR": (16, -5), "shinL": (34, 0), "shinR": (18, 0), "footL": (2, 0), "footR": (-14, 0),
         "spine": (20, 0), "chest": (24, 0), "head": (-20, 0), "armL": (-62, -8), "foreL": (-34, 0),
         "armR": (-lerp(62, 74, s), lerp(-46, 38, s)), "foreR": (-14, 0), "_dy": -.075, "_turn": lerp(-24, 20, s)}
    return p


def fish(u):
    jerk = max(0., math.sin(u * math.tau * 2 - 1.1)) ** 8
    w = math.sin(u * math.tau)
    p = stance({"spine": (3 + w, 0), "head": (6, 0)}, 9)
    p.update(armR=(-(58 + jerk * 24), 10), foreR=(-(34 + jerk * 10), 0), armL=(-44, -18), foreL=(-62, 0))
    return p


def knead(u):
    s = (math.sin(u * math.tau * 2) + 1) / 2
    p = stance({"spine": (lerp(24, 10, s), 0), "chest": (lerp(18, 6, s), 0), "head": (-14, 0)}, 10)
    p.update(armR=(-lerp(46, 108, s), 12), foreR=(-lerp(34, 58, s), 0), armL=(-lerp(44, 104, s), -24), foreL=(-lerp(44, 66, s), 0))
    return p


CLIPS = [("idle", idle, 2.4), ("walk", walk, .92), ("carry", carry, 1.05), ("chop", chop, 1.2), ("hoe", hoe, 1.35), ("mine", mine, 1.1),
         ("sow", sow, 1.5), ("gather", gather, 1.7), ("fish", fish, 3.6), ("build", build, 1.3), ("knead", knead, 1.4), ("reap", reap, 1.15)]


def frame(pose):
    bind = [np.eye(4) for _ in NAMES]
    world = [np.eye(4) for _ in NAMES]
    for i, parent in enumerate(PARENT):
        bind[i][:3, 3] = PIVOT[i]
        local = np.eye(4)
        x, z = pose.get(NAMES[i], (0, 0))
        rotation = rz(z) @ rx(x)
        if NAMES[i] == "chest" and "_turn" in pose:
            rotation = ry(pose["_turn"]) @ rotation
        if NAMES[i] == "head" and "_turn_head" in pose:
            rotation = ry(pose["_turn_head"]) @ rotation
        local[:3, :3] = rotation
        if parent < 0:
            local[:3, 3] = PIVOT[i] + (0, pose.get("_dy", 0), 0)
            world[i] = local
        else:
            local[:3, 3] = PIVOT[i] - PIVOT[parent]
            world[i] = world[parent] @ local
    return np.array([world[i] @ np.linalg.inv(bind[i]) for i in range(len(NAMES))])[:, :3, :]


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for old in OUTPUT.glob("*.dcmesh"):
        old.unlink()
    counts = {name: person(outfit).write(name) for name, outfit in OUTFITS.items()}
    tool_counts = {name: mesh.write("tool_" + name) for name, mesh in tools().items()}
    frames, table = [], []
    for name, clip, seconds in CLIPS:
        n = max(2, round(seconds * FPS))
        table.append((name, len(frames), n))
        frames.extend(frame(clip(i / n)) for i in range(n))
    data = np.array(frames, dtype="<f4")
    with (OUTPUT / "rig.bin").open("wb") as f:
        f.write(struct.pack("<iiii", len(NAMES), len(frames), len(table), FPS))
        for _, start, n in table:
            f.write(struct.pack("<ii", start, n))
        f.write(data.tobytes())
    (OUTPUT / "clips.txt").write_text("\n".join(f"{n} {s} {c}" for n, s, c in table) + "\n", encoding="utf-8")
    (OUTPUT / "SOURCE.txt").write_text("Original DeskCiv villagers, tools and animations generated by tools/bake_villagers.py.\n", encoding="utf-8")
    print("triangles", counts)
    print("tools", tool_counts)
    print(f"joints={len(NAMES)} frames={len(frames)} clips={len(table)} kb={sum(p.stat().st_size for p in OUTPUT.iterdir()) // 1024}")


if __name__ == "__main__":
    main()
