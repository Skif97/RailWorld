# ЧЕРНЕТКА, ПЕРЕПИСАТИ НАЧИСТО (записано 2026-10-13 з тимчасової папки, щоб не загубилося).
# Пише сцену важеля стрілки shapes/reference/switchlever-scene.json: усі деталі Lever..., текстури
# і дві анімації (throw для гри, demo для перегляду в редакторі). Тло сцени (Ref...) лише зсуває.
# УВАГА: перезаписує деталі Lever... у сцені, тож ручні правки в редакторі зникнуть.
# Скрипт латався багато разів заміною шматків і читається погано; числа розмірів у ньому мають збігатися
# з SectionBox.LeverGap у коді, але ніщо цього не перевіряє. Див. KNOWN_ISSUES.md, розділ «Важіль стрілки».
# Запускати з кореня репозиторію: python tools/make_lever_scene.py, потім python tools/make_lever.py

import json, math

# Збирає весь важіль стрілки в сцені заново: усі деталі Lever* і тестову анімацію.
# Важіль стоїть у точці (8, 0, 8), колія з боку +X, бруси вздовж X на Z = 4 і 12. Важіль кидається в площині XY
p = "RailWorld/assets/RailWorld/shapes/reference/switchlever-scene.json"
d = json.load(open(p, encoding="utf-8"))

def cube(name, lo, hi, origin=None, base=(0, 0, 0), children=None, **rotation):
    # lo, hi, origin у координатах сцени без повороту; base це from батька: діти рахуються від нього
    def face(u, v):
        return {"texture": "#lever", "uv": [0.0, 0.0, round(min(u, 32.0), 2), round(min(v, 32.0), 2)]}
    origin = origin or lo
    dx, dy, dz = (hi[n] - lo[n] for n in range(3))
    e = {"name": name,
         "from": [round(lo[n] - base[n], 3) for n in range(3)],
         "to": [round(hi[n] - base[n], 3) for n in range(3)],
         "rotationOrigin": [round(origin[n] - base[n], 3) for n in range(3)],
         "faces": {"north": face(dx, dy), "east": face(dz, dy), "south": face(dx, dy), "west": face(dz, dy),
                   "up": face(dx, dz), "down": face(dx, dz)}}
    for key, value in rotation.items():
        e[key] = round(value, 2)
    if children:
        e["children"] = children
    return e

PIVOT = (8.0, 2.4, 8.0)           # вісь важеля, вона ж вісь корпусу
LEAN = 60.0                       # нахил важеля від землі в крайньому положенні
BELOW = 2.0                       # довжина важеля нижче осі, до тяги
STROKE = 2 * BELOW * math.cos(math.radians(LEAN))   # хід нижнього кінця важеля, він же хід тяги
MAST_Z = 12.0
TRACK = 8.0                       # на скільки вокселів колія далі від важеля, ніж край шпали плюс 4
MAST_FOOT = 1.5                   # висота нижнього торця щогли
RADIUS = 0.91                     # плече кривошипа щогли, підібране разом з CRANK_DEG (див. нижче)
SLOT = (7.4, 8.6)                 # проріз у рамі й корпусі, де ходить важіль

# --- Основа ---------------------------------------------------------------------------------------------------
slope = math.degrees(math.atan2(2.8, 1.5))
length = math.hypot(1.5, 2.8)
base = [
    cube("LeverBasePlateA", (2.5, -0.25, 4), (5.5, 0.25, 12)),
    cube("LeverBasePlateB", (10.5, -0.25, 4), (13.5, 0.25, 12)),
    cube("LeverBaseFrameNear", (4, 0.25, 5), (12, 0.6, SLOT[0])),
    cube("LeverBaseFrameFar", (4, 0.25, SLOT[1]), (12, 0.6, 11)),
    cube("LeverBaseAxle", (7.6, 2.0, 6.0), (8.4, 2.8, 10.0)),
    cube("LeverBaseStand", (7.4, 0.6, 9.7), (8.6, 9.5, 10.3)),
    cube("LeverBaseStandArmLow", (7.5, 2.3, 9.7), (8.5, 3.0, MAST_Z - 0.4)),
    cube("LeverBaseCollarLow", (7.3, 2.1, MAST_Z - 0.7), (8.7, 3.2, MAST_Z + 0.7)),
    cube("LeverBaseStandArm", (7.5, 8.6, 9.7), (8.5, 9.3, MAST_Z - 0.4)),
    cube("LeverBaseCollar", (7.3, 8.4, MAST_Z - 0.7), (8.7, 9.5, MAST_Z + 0.7)),
]
# Корпус це дві щоки клином, між ними ходить важіль
for side, (z0, z1) in (("Near", (6.5, SLOT[0])), ("Far", (SLOT[1], 9.5))):
    middle = (z0 + z1) / 2
    base += [
        cube("LeverBaseCheek" + side, (7.1, 0.6, z0), (8.9, 3.4, z1)),
        cube("LeverBaseCheek%sFoot" % side, (6.3, 0.6, z0), (9.7, 1.9, z1)),
        cube("LeverBaseCheek%sSlopeA" % side, (5.6, -0.15, z0), (5.6 + length, 0.6, z1), (5.6, 0.6, middle), rotationZ=slope),
        cube("LeverBaseCheek%sSlopeB" % side, (10.4 - length, -0.15, z0), (10.4, 0.6, z1), (10.4, 0.6, middle), rotationZ=-slope),
    ]

# --- Важіль ---------------------------------------------------------------------------------------------------
# Намальований лежачи вздовж +X від осі і повернутий на LEAN: кут міняється одним числом. Нижче осі він іде
# між щоками крізь проріз у рамі до вилки на тязі. Муфта з плечем і вантажем обертається довкола самого важеля
px, py = PIVOT[0], PIVOT[1]
arm_from = (px - BELOW, py - 0.35, 7.65)
knee = px + 6.9
upper_from = (knee - 0.2, py - 0.35, 7.65)
collar = px + 3.0
collar_from = (collar - 0.8, py - 0.8, 7.2)
weight_arm_from = (collar + 0.6, py - 0.3, 7.7)
arm = cube("LeverArm", arm_from, (knee, py + 0.35, 8.35), PIVOT, rotationZ=LEAN, children=[
    cube("LeverArmUpper", upper_from, (knee + 5.0, py + 0.35, 8.35), (knee, py, 8), arm_from, rotationZ=12, children=[
        cube("LeverArmGrip", (knee + 5.0, py - 0.55, 7.45), (knee + 7.2, py + 0.55, 8.55), None, upper_from),
    ]),
    cube("LeverWeight", collar_from, (collar + 0.8, py + 0.8, 8.8), (collar, py, 8), arm_from, children=[
        cube("LeverWeightArm", weight_arm_from, (collar + 8.3, py + 0.3, 8.3), (collar, py, 8), collar_from, rotationZ=-LEAN, children=[
            cube("LeverWeightBlock", (collar + 5.5, py - 1.6, 7.2), (collar + 7.7, py + 1.0, 8.8), None, weight_arm_from),
        ]),
    ]),
])

# --- Тяга до гостряків ----------------------------------------------------------------------------------------
# Лежить нижче підошви рейки між брусами, від корпусу до поперечини гостряків. Вилка на ній обіймає нижній кінець
# важеля, стовпчик тримає косу тягу щогли, вушко на дальньому кінці йде до поперечини
fork = px - STROKE / 2            # де нижній кінець важеля, поки той нахилений до колії
# Щогла має зробити рівно пів оберта, поки тяга пройде свій хід. Тяга йде не крізь вісь щогли, а збоку від неї,
# тож це виходить лише за певного початкового кута кривошипа і певного місця стовпчика. Кут задаємо (CRANK_DEG,
# від +X до +Z), а місце стовпчика шукаємо: відстань від нього до пальця на початку має дорівнювати відстані
# від зсунутого стовпчика до пальця, що став навпроти
CRANK_DEG = 156.0
def pin_at(turned):
    angle = math.radians(CRANK_DEG) - turned
    return (px + RADIUS * math.cos(angle), MAST_Z + RADIUS * math.sin(angle))
PIN = pin_at(0)
POST_Z = 8.5
def misfit(x):
    far = pin_at(math.pi)
    return math.hypot(x - PIN[0], POST_Z - PIN[1]) - math.hypot(x + STROKE - far[0], POST_Z - far[1])
low, high = px + RADIUS, 40.0
for _ in range(60):
    middle = (low + high) / 2
    if (misfit(middle) > 0) == (misfit(low) > 0): low = middle
    else: high = middle
ATTACH = (low, POST_Z)            # стовпчик під косу тягу
LINK = math.hypot(ATTACH[0] - PIN[0], PIN[1] - ATTACH[1])
LINK_TURN = math.degrees(math.atan2(PIN[1] - ATTACH[1], ATTACH[0] - PIN[0]))
rod_from = (fork - 0.7, -0.8, 7.5)
rod = cube("LeverRod", rod_from, (21.8 + TRACK, -0.3, 8.5), children=[
    cube("LeverRodFork", (fork - 0.5, -0.3, 7.5), (fork + 0.5, 1.1, 8.5), None, rod_from),
    cube("LeverRodPost", (ATTACH[0] - 0.3, -0.3, ATTACH[1] - 0.3), (ATTACH[0] + 0.3, MAST_FOOT - 0.25, ATTACH[1] + 0.3), None, rod_from),
    cube("LeverRodRiser", (21.2 + TRACK, -0.3, 7.6), (21.8 + TRACK, 0.5, 8.4), None, rod_from),
])

# --- Щогла з покажчиком ---------------------------------------------------------------------------------------
# Обертається довкола вертикалі разом з покажчиком і кривошипом. Покажчик складений зі смужок заввишки STEP:
# косих країв кубиками не зробити. Коса тяга сидить на пальці кривошипа і намальована від нього вздовж +X
# Щогла до землі не доходить: висить у двох хомутах. Кривошип сидить на її нижньому торці, а коса тяга
# під кривошипом, тож вона проходить під щоглою, а не крізь неї
mast_from = (7.7, MAST_FOOT, MAST_Z - 0.3)
TIP, MIDDLE, LENGTH, HALF, STEP = 4.5, 19.0, 5.0, 2.0, 0.5
# Вістря дивиться до колії, поки стрілка в першому положенні: тоді увімкнена гілка з далекого від важеля боку
# Покажчик пофарбований трьома рівними смугами впоперек: біла, чорна, біла. Смужки зсунуті одна відносно одної,
# тож кожна третина смужки разом із сусідніми складає куточок тієї самої форми, що й увесь покажчик
BANDS = ("White", "Black", "White")
def row(name, k, low, high):
    right = 16.0 - (TIP + STEP * k + STEP / 2)
    part = LENGTH / len(BANDS)
    return [cube(name + band + str(n), (right - LENGTH + part * n, low, MAST_Z - 0.15), (right - LENGTH + part * (n + 1), high, MAST_Z + 0.15), None, mast_from)
            for n, band in enumerate(BANDS)]
rows = row("LeverSignRow0", 0, MIDDLE - STEP, MIDDLE + STEP)
for k in range(1, int(HALF / STEP)):
    rows += row("LeverSignRowUp%d" % k, k, MIDDLE + STEP * k, MIDDLE + STEP * (k + 1))
    rows += row("LeverSignRowDown%d" % k, k, MIDDLE - STEP * (k + 1), MIDDLE - STEP * k)
sign = cube("LeverSign", mast_from, (8.3, 17.5, MAST_Z + 0.3), (8, MAST_FOOT, MAST_Z), children=rows + [
    cube("LeverSignCrank", (7.7, MAST_FOOT - 0.25, MAST_Z - 0.3), (8.0 + RADIUS + 0.3, MAST_FOOT, MAST_Z + 0.3), (8.0, MAST_FOOT, MAST_Z), mast_from, rotationY=-CRANK_DEG),
    cube("LeverSignLink", (PIN[0], MAST_FOOT - 0.5, PIN[1] - 0.125), (PIN[0] + LINK, MAST_FOOT - 0.25, PIN[1] + 0.125),
         (PIN[0], MAST_FOOT - 0.4, PIN[1]), mast_from, rotationY=LINK_TURN),
])

REF_X = {"RefTimber1": (2.4, 40.0), "RefTimber2": (2.4, 40.0), "RefRailNearFoot": (18.76, 20.76), "RefRailNearWeb": (19.56, 19.96),
         "RefRailNearHead": (19.16, 20.36), "RefRailFarFoot": (31.24, 33.24), "RefRailFarWeb": (32.04, 32.44),
         "RefRailFarHead": (31.64, 32.84), "RefBladeNear": (20.36, 21.16), "RefBladeFar": (28.92, 29.72), "RefTieBar": (20.76, 29.32)}
for e in d["elements"]:
    if e["name"] not in REF_X: continue
    low, high = REF_X[e["name"]]
    e["from"][0] = low if e["name"].startswith("RefTimber") else low + TRACK
    e["to"][0] = high + TRACK
d["elements"] = [e for e in d["elements"] if not e["name"].startswith("Lever")] + base + [arm, rod, sign]

# --- Текстури -------------------------------------------------------------------------------------------------
# Усе з текстур самої гри. Литі й катані деталі основи з плитного заліза, як скріплення рейок; усе, що рухається
# і треться, з темнішого потьмянілого; вантаж свинцевий; покажчик з білої жерсті, щоб його було видно здалеку;
# ручка дерев'яна
TEXTURES = {
    "leveriron": "block/metal/plate/iron",
    "leverrod": "block/metal/tarnished/iron",
    "leverweight": "block/metal/plate/lead",
    "leversign": "block/metal/plate/tin",
    "leversignblack": "block/clay/hardened/black",
    "levergrip": "block/wood/debarked/oak",
}
def material(name):
    if name.startswith("LeverSignRow"): return "leversignblack" if "Black" in name else "leversign"
    if name == "LeverWeightBlock": return "leverweight"
    if name == "LeverArmGrip": return "levergrip"
    if name.startswith(("LeverBasePlate", "LeverBaseFrame", "LeverBaseCheek", "LeverBaseStand", "LeverBaseCollar", "LeverBaseMastPad")): return "leveriron"
    return "leverrod"

SIZE = 32                          # текстури гри 32 на 32 точки на блок, тобто дві точки на воксель
PER_VOXEL = SIZE / 16.0
def paint(e):
    key = material(e["name"])
    dx, dy, dz = (e["to"][n] - e["from"][n] for n in range(3))
    spans = {"north": (dx, dy), "south": (dx, dy), "east": (dz, dy), "west": (dz, dy), "up": (dx, dz), "down": (dx, dz)}
    for index, (side, (u, v)) in enumerate(spans.items()):
        u, v = min(u * PER_VOXEL, SIZE), min(v * PER_VOXEL, SIZE)
        # Кожна грань бере своє місце текстури, щоб сусідні деталі не були однакові. Місце залежить лише від
        # назви деталі й грані, тож від запуску до запуску не міняється
        seed = sum(ord(c) * (n + 7) for n, c in enumerate(e["name"])) + index * 131
        u0 = (seed * 37 % 1000) / 1000.0 * (SIZE - u)
        v0 = (seed * 53 % 1000) / 1000.0 * (SIZE - v)
        e["faces"][side] = {"texture": "#" + key, "uv": [round(u0, 2), round(v0, 2), round(u0 + u, 2), round(v0 + v, 2)]}
    for child in e.get("children", []):
        paint(child)
for e in d["elements"]:
    if e["name"].startswith("Lever"): paint(e)
d["textures"].pop("lever", None)
d["textureSizes"].pop("lever", None)
for key, path in TEXTURES.items():
    d["textures"][key] = path
    d["textureSizes"][key] = [SIZE, SIZE]

# --- Тестова анімація -----------------------------------------------------------------------------------------
def turn(x=0.0, y=0.0, z=0.0):
    return {"rotationX": round(x, 2), "rotationY": round(y, 2), "rotationZ": round(z, 2)}

def solve(post, direction):
    # На скільки повернута щогла, коли стовпчик у точці post. None, якщо таких кутів кілька або жодного:
    # тоді механізм у цей бік не працює
    def gap(a):
        pin = pin_at(direction * a)
        return math.hypot(post[0] - pin[0], pin[1] - post[1]) - LINK
    marks = [gap(math.pi * n / 720) > 1e-9 for n in range(721)]
    if sum(1 for n in range(720) if marks[n] != marks[n + 1]) != 1: return None
    low, high = 0.0, math.pi
    for _ in range(50):
        middle = (low + high) / 2
        if (gap(middle) > 1e-9) == marks[0]: low = middle
        else: high = middle
    return (low + high) / 2

# У який бік крутиться щогла: у той, де кут однозначний на всьому ході
def works(direction):
    for n in range(1, 40):
        lean = math.radians(LEAN + (180 - 2 * LEAN) * n / 40)
        if solve((ATTACH[0] + (px - BELOW * math.cos(lean)) - fork, ATTACH[1]), direction) is None: return False
    return True
DIRECTION = next(direction for direction in (1, -1) if works(direction))

def pose(frame, thrown):
    # Тягу веде нижній кінець важеля. Щогла повертається на стільки, щоб палець кривошипа лишався від стовпчика
    # на тязі рівно на довжину косої тяги
    lean = math.radians(LEAN + (180 - 2 * LEAN) * thrown)
    shift = (px - BELOW * math.cos(lean)) - fork
    post = (ATTACH[0] + shift, ATTACH[1])
    # У крайніх положеннях кут відомий: саме під них підібрано місце стовпчика
    angle = 0.0 if thrown <= 0 else math.pi if thrown >= 1 else solve(post, DIRECTION)
    pin = pin_at(DIRECTION * angle)
    mast = DIRECTION * math.degrees(angle)
    link = math.degrees(math.atan2(pin[1] - post[1], post[0] - pin[0])) - mast - LINK_TURN
    stretch = math.hypot(post[0] - pin[0], pin[1] - post[1]) / LINK
    return {"frame": frame, "elements": {
        "LeverArm": turn(z=(180 - 2 * LEAN) * thrown),
        "LeverWeight": turn(x=180.0 * thrown),
        "LeverSign": turn(y=mast),
        "LeverSignLink": turn(y=link),
        "LeverRod": {"offsetX": round(shift, 3), "offsetY": 0.0, "offsetZ": 0.0},
    }}, stretch

STEPS = 15
SPAN = 30                          # кадрів на одне переведення
def keys(first, back):
    made, worst = [], 0.0
    for n in range(STEPS + 1):
        key, stretch = pose(first + SPAN * n // STEPS, (1 - n / STEPS) if back else n / STEPS)
        made.append(key)
        worst = max(worst, abs(stretch - 1))
    return made, worst

# throw це саме переведення з першого положення в друге: його бере гра і сама ставить на потрібний кадр.
# demo це те саме туди й назад по колу, щоб дивитися в редакторі
there, worst = keys(0, False)
forth, _ = keys(15, False)
back, _ = keys(65, True)
d["animations"] = [
    {"name": "Throw", "code": "throw", "quantityframes": SPAN + 1,
     "onActivityStopped": "EaseOut", "onAnimationEnd": "Hold", "keyframes": there},
    {"name": "Demo", "code": "demo", "quantityframes": 100,
     "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat", "keyframes": [pose(0, 0.0)[0]] + forth + back},
]
json.dump(d, open(p, "w", encoding="utf-8", newline="\n"), indent="\t", ensure_ascii=False)
print("ok stroke", STROKE, "link", round(LINK, 3), "link misfit", round(worst * LINK, 3))
