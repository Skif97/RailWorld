# Пише сцену важеля стрілки shapes/reference/switchlever-scene.json: сам важіль (деталі Lever...), його текстури,
# дві анімації і тло для масштабу (бруси, рейки, гостряки: деталі Ref...). Сцена пишеться вся заново, ручні правки
# в редакторі цей скрипт перезаписує: щось змінити в моделі означає змінити число тут.
# Модель для гри з готової сцени робить tools/make_lever.py.
# Запускати з кореня репозиторію: python tools/make_lever_scene.py, потім python tools/make_lever.py
#
# Одиниці: вокселі, 16 на блок. Важіль стоїть у точці (8, 0, 8) на рівні підошви рейки, колія з боку +X і йде
# вздовж Z, бруси під важелем лежать уздовж X. Ручка кидається в площині XY, тобто впоперек колії.
# Де стоїть колія відносно важеля і скільки місця він може займати, скрипт читає з коду мода: якщо модель
# з ним розходиться, сцена не пишеться.

import json
import math
import re

SCENE = "RailWorld/assets/RailWorld/shapes/reference/switchlever-scene.json"
VOXELS = 16.0


# ── Розміри з коду мода ─────────────────────────────────────────────────────────────────────────────────────────

def code_constant(path, name):
    text = open(path, encoding="utf-8-sig").read()
    found = re.search(r"\b%s\s*=\s*([0-9.]+)f?\s*;" % name, text)
    if not found:
        raise SystemExit("у %s немає сталої %s" % (path, name))
    return float(found.group(1))


PICKER = "RailWorld/src/RailWay/SectionPicker.cs"
SLEEPER = code_constant(PICKER, "StandardSleeperLength") * VOXELS
GAUGE = code_constant(PICKER, "StandardWidth") * VOXELS
LEVER_GAP = code_constant(PICKER, "LeverGap") * VOXELS
REACH_ACROSS = code_constant(PICKER, "LeverReachAcross")
REACH_ALONG = code_constant(PICKER, "LeverReachAlong")
BOX_HEIGHT = code_constant(PICKER, "LeverHeight")
TIMBER_PAST_LEVER = code_constant("RailWorld/src/RailWay/NodeTimbers.cs", "LeverBase") * VOXELS

LEVER = 8.0                                   # де стоїть важіль, по X і по Z
TRACK_AXIS = LEVER + SLEEPER / 2 + LEVER_GAP  # вісь колії: важіль відступає від краю шпали на LeverGap
NEAR_RAIL = TRACK_AXIS - GAUGE / 2
FAR_RAIL = TRACK_AXIS + GAUGE / 2

# Рейка в перерізі, як у моделі рейки: пів ширини підошви, шийки й головки і висоти їхнього верху
RAIL_FOOT, RAIL_WEB, RAIL_HEAD = 1.0, 0.2, 0.6
FOOT_TOP, HEAD_BOTTOM, RAIL_TOP = 0.3, 1.8, 2.5
BLADE_WIDTH = 0.8
BLADE_OPEN = 1.92                             # на скільки в сцені відведений дальній гостряк


# ── Розміри самого важеля ──────────────────────────────────────────────────────────────────────────────────────

PIVOT = (LEVER, 2.4, LEVER)       # вісь важеля, вона ж вісь корпусу
LEAN = 60.0                       # нахил важеля від землі в крайньому положенні, градуси
BELOW = 2.0                       # довжина важеля нижче осі, до тяги
STROKE = 2 * BELOW * math.cos(math.radians(LEAN))   # хід нижнього кінця важеля, він же хід тяги
SLOT = (7.4, 8.6)                 # проріз у рамі й корпусі, де ходить важіль, по Z

MAST_Z = 12.0                     # щогла стоїть збоку від корпусу, над другим брусом
MAST_FOOT = 1.5                   # висота нижнього торця щогли: до землі вона не доходить
MAST_TOP = 17.5
CRANK = 0.91                      # плече кривошипа щогли
CRANK_DEG = 156.0                 # куди кривошип дивиться в першому положенні, від +X до +Z
POST_Z = 8.5                      # стовпчик на тязі, за який тримається коса тяга

# Покажчик: смужки заввишки SIGN_STEP, кожна далі від середини зсунута на SIGN_STEP від вістря
SIGN_TIP, SIGN_MIDDLE, SIGN_LENGTH, SIGN_HALF, SIGN_STEP = 4.5, 19.0, 5.0, 2.0, 0.5
SIGN_BANDS = ("White", "Black", "White")

TEXTURE_SIZE = 32                 # текстури гри 32 на 32 точки на блок, тобто дві точки на воксель
THROW_FRAMES = 30                 # кадрів на одне переведення
KEYS = 15                         # ключових кадрів на переведення


def cube(name, lo, hi, origin=None, base=(0, 0, 0), children=None, **rotation):
    """Деталь-кубик. lo, hi, origin у координатах сцени до повороту; base це from батька: діти рахуються від нього."""
    origin = origin or lo
    element = {
        "name": name,
        "from": [round(lo[n] - base[n], 3) for n in range(3)],
        "to": [round(hi[n] - base[n], 3) for n in range(3)],
        "rotationOrigin": [round(origin[n] - base[n], 3) for n in range(3)],
        "faces": {},
    }
    for key, value in rotation.items():
        element[key] = round(value, 2)
    if children:
        element["children"] = children
    return element


# ── Основа ─────────────────────────────────────────────────────────────────────────────────────────────────────

def base_parts():
    """Нерухоме: дві плити впоперек брусів, рама з прорізом, корпус із двох щік, вісь, стійка з двома хомутами."""
    parts = [
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

    # Щока корпусу в профіль це трапеція: два бруски і дві похилі стінки, повернуті кубики
    slope = math.degrees(math.atan2(2.8, 1.5))
    length = math.hypot(1.5, 2.8)
    for side, (z0, z1) in (("Near", (6.5, SLOT[0])), ("Far", (SLOT[1], 9.5))):
        middle = (z0 + z1) / 2
        parts += [
            cube("LeverBaseCheek" + side, (7.1, 0.6, z0), (8.9, 3.4, z1)),
            cube("LeverBaseCheek%sFoot" % side, (6.3, 0.6, z0), (9.7, 1.9, z1)),
            cube("LeverBaseCheek%sSlopeA" % side, (5.6, -0.15, z0), (5.6 + length, 0.6, z1), (5.6, 0.6, middle), rotationZ=slope),
            cube("LeverBaseCheek%sSlopeB" % side, (10.4 - length, -0.15, z0), (10.4, 0.6, z1), (10.4, 0.6, middle), rotationZ=-slope),
        ]
    return parts


# ── Важіль ─────────────────────────────────────────────────────────────────────────────────────────────────────

def lever_arm():
    """
    Ручка зі зламом і муфта з плечем та вантажем. Ручка намальована лежачи вздовж +X від осі і повернута на LEAN:
    так її кут міняється одним числом. Нижче осі вона йде між щоками крізь проріз у рамі до вилки на тязі.
    Муфта обертається довкола самої ручки (її вісь тут X), плече з вантажем відхилене від ручки на LEAN.
    """
    px, py = PIVOT[0], PIVOT[1]
    arm_from = (px - BELOW, py - 0.35, 7.65)
    knee = px + 6.9
    upper_from = (knee - 0.2, py - 0.35, 7.65)
    collar = px + 3.0
    collar_from = (collar - 0.8, py - 0.8, 7.2)
    weight_arm_from = (collar + 0.6, py - 0.3, 7.7)

    return cube("LeverArm", arm_from, (knee, py + 0.35, 8.35), PIVOT, rotationZ=LEAN, children=[
        cube("LeverArmUpper", upper_from, (knee + 5.0, py + 0.35, 8.35), (knee, py, 8), arm_from, rotationZ=12, children=[
            cube("LeverArmGrip", (knee + 5.0, py - 0.55, 7.45), (knee + 7.2, py + 0.55, 8.55), None, upper_from),
        ]),
        cube("LeverWeight", collar_from, (collar + 0.8, py + 0.8, 8.8), (collar, py, 8), arm_from, children=[
            cube("LeverWeightArm", weight_arm_from, (collar + 8.3, py + 0.3, 8.3), (collar, py, 8), collar_from, rotationZ=-LEAN, children=[
                cube("LeverWeightBlock", (collar + 5.5, py - 1.6, 7.2), (collar + 7.7, py + 1.0, 8.8), None, weight_arm_from),
            ]),
        ]),
    ])


# ── Привід щогли ───────────────────────────────────────────────────────────────────────────────────────────────

class Linkage:
    """
    Кривошип на щоглі і коса тяга від нього до стовпчика на тязі. Щогла має зробити рівно пів оберта, поки тяга
    пройде свій хід. Тяга йде не крізь вісь щогли, а збоку від неї, тож це виходить лише за певного початкового
    кута кривошипа і певного місця стовпчика: кут задано (CRANK_DEG), місце стовпчика шукаємо.
    """

    def __init__(self):
        self.fork = PIVOT[0] - STROKE / 2          # де нижній кінець ручки, поки та нахилена до колії
        self.pin = self.pin_at(0)                  # палець кривошипа в першому положенні

        # Стовпчик стоїть там, звідки до пальця на початку так само далеко, як після ходу до пальця навпроти
        far = self.pin_at(math.pi)

        def misfit(x):
            return math.hypot(x - self.pin[0], POST_Z - self.pin[1]) - math.hypot(x + STROKE - far[0], POST_Z - far[1])

        low, high = PIVOT[0] + CRANK, 40.0
        for _ in range(60):
            middle = (low + high) / 2
            if (misfit(middle) > 0) == (misfit(low) > 0):
                low = middle
            else:
                high = middle
        self.post = (low, POST_Z)
        self.length = math.hypot(self.post[0] - self.pin[0], self.pin[1] - self.post[1])
        self.turn = math.degrees(math.atan2(self.pin[1] - self.post[1], self.post[0] - self.pin[0]))

        # Щогла крутиться в той бік, де її кут однозначний на всьому ході: в інший механізм заклинює
        self.direction = next(direction for direction in (1, -1) if self.works(direction))

    @staticmethod
    def pin_at(turned):
        angle = math.radians(CRANK_DEG) - turned
        return (PIVOT[0] + CRANK * math.cos(angle), MAST_Z + CRANK * math.sin(angle))

    def shift(self, thrown):
        """На скільки тягу зсунув нижній кінець ручки: 0 в першому положенні, STROKE у другому."""
        lean = math.radians(LEAN + (180 - 2 * LEAN) * thrown)
        return (PIVOT[0] - BELOW * math.cos(lean)) - self.fork

    def solve(self, post, direction):
        """Кут щогли, радіани, коли стовпчик у точці post. None, якщо таких кутів кілька або жодного."""
        def gap(angle):
            pin = self.pin_at(direction * angle)
            return math.hypot(post[0] - pin[0], pin[1] - post[1]) - self.length

        marks = [gap(math.pi * n / 720) > 1e-9 for n in range(721)]
        if sum(1 for n in range(720) if marks[n] != marks[n + 1]) != 1:
            return None
        low, high = 0.0, math.pi
        for _ in range(50):
            middle = (low + high) / 2
            if (gap(middle) > 1e-9) == marks[0]:
                low = middle
            else:
                high = middle
        return (low + high) / 2

    def works(self, direction):
        return all(self.solve((self.post[0] + self.shift(n / 40), POST_Z), direction) is not None for n in range(1, 40))

    def at(self, thrown):
        """Зсув тяги, поворот щогли і довертання косої тяги відносно щогли, градуси, і наскільки тяга «тягнеться»."""
        shift = self.shift(thrown)
        post = (self.post[0] + shift, POST_Z)
        # У крайніх положеннях кут відомий: саме під них підібрано місце стовпчика
        angle = 0.0 if thrown <= 0 else math.pi if thrown >= 1 else self.solve(post, self.direction)
        pin = self.pin_at(self.direction * angle)
        mast = self.direction * math.degrees(angle)
        link = math.degrees(math.atan2(pin[1] - post[1], post[0] - pin[0])) - mast - self.turn
        stretch = math.hypot(post[0] - pin[0], pin[1] - post[1]) / self.length
        return shift, mast, link, stretch


def rod(linkage):
    """
    Тяга до гостряків: лежить нижче підошви рейки між брусами, від корпусу до поперечини гостряків. Вилка на ній
    обіймає нижній кінець ручки, стовпчик тримає косу тягу щогли, вушко на дальньому кінці йде до поперечини.
    """
    blade_inner = NEAR_RAIL + RAIL_HEAD + BLADE_WIDTH
    riser = (blade_inner + 0.04, blade_inner + 0.64)
    rod_from = (linkage.fork - 0.7, -0.8, 7.5)
    post = linkage.post
    return cube("LeverRod", rod_from, (riser[1], -0.3, 8.5), children=[
        cube("LeverRodFork", (linkage.fork - 0.5, -0.3, 7.5), (linkage.fork + 0.5, 1.1, 8.5), None, rod_from),
        cube("LeverRodPost", (post[0] - 0.3, -0.3, post[1] - 0.3), (post[0] + 0.3, MAST_FOOT - 0.25, post[1] + 0.3), None, rod_from),
        cube("LeverRodRiser", (riser[0], -0.3, 7.6), (riser[1], 0.5, 8.4), None, rod_from),
    ])


def mast(linkage):
    """
    Щогла з покажчиком: обертається довкола вертикалі разом з кривошипом і косою тягою. Висить у двох хомутах;
    кривошип сидить на її нижньому торці, коса тяга під ним, тож вона проходить під щоглою, а не крізь неї.
    Покажчик складений зі смужок, бо косих країв кубиками не зробити, і пофарбований трьома рівними смугами
    впоперек: третини сусідніх смужок складають куточок тієї самої форми, що й увесь покажчик. Вістря дивиться
    до колії, поки стрілка в першому положенні: тоді увімкнена гілка з далекого від важеля боку.
    """
    mast_from = (7.7, MAST_FOOT, MAST_Z - 0.3)

    def row(name, k, low, high):
        right = 2 * LEVER - (SIGN_TIP + SIGN_STEP * k + SIGN_STEP / 2)
        part = SIGN_LENGTH / len(SIGN_BANDS)
        return [cube(name + band + str(n), (right - SIGN_LENGTH + part * n, low, MAST_Z - 0.15),
                     (right - SIGN_LENGTH + part * (n + 1), high, MAST_Z + 0.15), None, mast_from)
                for n, band in enumerate(SIGN_BANDS)]

    rows = row("LeverSignRow0", 0, SIGN_MIDDLE - SIGN_STEP, SIGN_MIDDLE + SIGN_STEP)
    for k in range(1, int(SIGN_HALF / SIGN_STEP)):
        rows += row("LeverSignRowUp%d" % k, k, SIGN_MIDDLE + SIGN_STEP * k, SIGN_MIDDLE + SIGN_STEP * (k + 1))
        rows += row("LeverSignRowDown%d" % k, k, SIGN_MIDDLE - SIGN_STEP * (k + 1), SIGN_MIDDLE - SIGN_STEP * k)

    pin = linkage.pin
    return cube("LeverSign", mast_from, (8.3, MAST_TOP, MAST_Z + 0.3), (LEVER, MAST_FOOT, MAST_Z), children=rows + [
        cube("LeverSignCrank", (7.7, MAST_FOOT - 0.25, MAST_Z - 0.3), (LEVER + CRANK + 0.3, MAST_FOOT, MAST_Z + 0.3),
             (LEVER, MAST_FOOT, MAST_Z), mast_from, rotationY=-CRANK_DEG),
        # Коса тяга намальована від пальця кривошипа вздовж +X і довернута до стовпчика
        cube("LeverSignLink", (pin[0], MAST_FOOT - 0.5, pin[1] - 0.125), (pin[0] + linkage.length, MAST_FOOT - 0.25, pin[1] + 0.125),
             (pin[0], MAST_FOOT - 0.4, pin[1]), mast_from, rotationY=linkage.turn),
    ])


# ── Тло для масштабу ───────────────────────────────────────────────────────────────────────────────────────────

def scenery():
    """Два подовжені бруси, рамні рейки, гостряки і поперечина між ними. У гру не йдуть."""
    def rail(name, center):
        return [
            cube(name + "Foot", (center - RAIL_FOOT, 0, 0), (center + RAIL_FOOT, FOOT_TOP, 16)),
            cube(name + "Web", (center - RAIL_WEB, FOOT_TOP, 0), (center + RAIL_WEB, HEAD_BOTTOM, 16)),
            cube(name + "Head", (center - RAIL_HEAD, HEAD_BOTTOM, 0), (center + RAIL_HEAD, RAIL_TOP, 16)),
        ]

    timber_from, timber_to = LEVER - TIMBER_PAST_LEVER, TRACK_AXIS + SLEEPER / 2
    near_blade = (NEAR_RAIL + RAIL_HEAD, NEAR_RAIL + RAIL_HEAD + BLADE_WIDTH)
    # Дальній гостряк відведений від своєї рамної рейки
    far_blade = (FAR_RAIL - RAIL_HEAD - BLADE_OPEN - BLADE_WIDTH, FAR_RAIL - RAIL_HEAD - BLADE_OPEN)
    return [
        cube("RefTimber1", (timber_from, -2, 2), (timber_to, 0, 6)),
        cube("RefTimber2", (timber_from, -2, 10), (timber_to, 0, 14)),
        *rail("RefRailNear", NEAR_RAIL),
        *rail("RefRailFar", FAR_RAIL),
        cube("RefBladeNear", (near_blade[0], FOOT_TOP, 0), (near_blade[1], RAIL_TOP, 16)),
        cube("RefBladeFar", (far_blade[0], FOOT_TOP, 0), (far_blade[1], RAIL_TOP, 16)),
        cube("RefTieBar", (NEAR_RAIL + RAIL_FOOT, 0.5, 7.28), (far_blade[0] + 0.4, 1.0, 8.72)),
    ]


# ── Текстури ───────────────────────────────────────────────────────────────────────────────────────────────────

# Усе з текстур самої гри. Литі й катані деталі основи з плитного заліза, як скріплення рейок; усе, що рухається
# і треться, з темнішого потьмянілого; вантаж свинцевий; покажчик зі світлої жерсті з чорною смугою з випаленої
# глини; ручка дерев'яна. Шляхи без домену, як їх розуміє редактор
TEXTURES = {
    "wood": "block/wood/debarked/pine",
    "rail": "block/metal/tarnished/rusty-iron2",
    "leveriron": "block/metal/plate/iron",
    "leverrod": "block/metal/tarnished/iron",
    "leverweight": "block/metal/plate/lead",
    "leversign": "block/metal/plate/tin",
    "levergrip": "block/wood/debarked/oak",
    "leversignblack": "block/clay/hardened/black",
}
IRON_PARTS = ("LeverBasePlate", "LeverBaseFrame", "LeverBaseCheek", "LeverBaseStand", "LeverBaseCollar")


def material(name):
    if name.startswith("RefTimber"):
        return "wood"
    if name.startswith("Ref"):
        return "rail"
    if name.startswith("LeverSignRow"):
        return "leversignblack" if "Black" in name else "leversign"
    if name == "LeverWeightBlock":
        return "leverweight"
    if name == "LeverArmGrip":
        return "levergrip"
    if name.startswith(IRON_PARTS):
        return "leveriron"
    return "leverrod"


def paint(element):
    """
    Розкладає текстуру по гранях: дві точки на воксель, як у блоків гри. Кожна грань бере своє місце текстури,
    щоб сусідні деталі не були однакові; місце залежить лише від назви деталі й грані, тож від запуску до запуску
    не міняється.
    """
    key = material(element["name"])
    dx, dy, dz = (element["to"][n] - element["from"][n] for n in range(3))
    spans = {"north": (dx, dy), "east": (dz, dy), "south": (dx, dy), "west": (dz, dy), "up": (dx, dz), "down": (dx, dz)}
    order = ("north", "south", "east", "west", "up", "down")
    per_voxel = TEXTURE_SIZE / VOXELS
    for side, (u, v) in spans.items():
        u, v = min(u * per_voxel, TEXTURE_SIZE), min(v * per_voxel, TEXTURE_SIZE)
        seed = sum(ord(c) * (n + 7) for n, c in enumerate(element["name"])) + order.index(side) * 131
        u0 = (seed * 37 % 1000) / 1000.0 * (TEXTURE_SIZE - u)
        v0 = (seed * 53 % 1000) / 1000.0 * (TEXTURE_SIZE - v)
        element["faces"][side] = {"texture": "#" + key, "uv": [round(u0, 2), round(v0, 2), round(u0 + u, 2), round(v0 + v, 2)]}
    for child in element.get("children", []):
        paint(child)


# ── Анімація ───────────────────────────────────────────────────────────────────────────────────────────────────

def pose(linkage, frame, thrown):
    """
    Кадр: де всі рухомі деталі, коли стрілка між першим положенням (0) і другим (1). Повороти в кадрі додаються
    до тих, що деталь має в моделі. Ручка йде з LEAN на 180 - LEAN, муфта з вантажем робить пів оберта довкола неї,
    тягу веде нижній кінець ручки, а щоглу коса тяга сталої довжини.
    """
    def turn(x=0.0, y=0.0, z=0.0):
        return {"rotationX": round(x, 2), "rotationY": round(y, 2), "rotationZ": round(z, 2)}

    shift, mast_turn, link_turn, _ = linkage.at(thrown)
    return {"frame": frame, "elements": {
        "LeverArm": turn(z=(180 - 2 * LEAN) * thrown),
        "LeverWeight": turn(x=180.0 * thrown),
        "LeverSign": turn(y=mast_turn),
        "LeverSignLink": turn(y=link_turn),
        "LeverRod": {"offsetX": round(shift, 3), "offsetY": 0.0, "offsetZ": 0.0},
    }}


def animations(linkage):
    """
    throw це саме переведення з першого положення в друге. Гра програє його, щоб перевести важіль, тримає
    на останньому кадрі і відмотує назад, щоб повернути (SwitchLeverRenderer).
    demo це те саме туди й назад по колу з паузами, щоб дивитися в редакторі.
    """
    def keys(first, back):
        return [pose(linkage, first + THROW_FRAMES * n // KEYS, (1 - n / KEYS) if back else n / KEYS) for n in range(KEYS + 1)]

    return [
        {"name": "Throw", "code": "throw", "quantityframes": THROW_FRAMES + 1,
         "onActivityStopped": "Rewind", "onAnimationEnd": "Hold", "keyframes": keys(0, False)},
        {"name": "Demo", "code": "demo", "quantityframes": 100,
         "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat", "keyframes": [pose(linkage, 0, 0.0)] + keys(15, False) + keys(65, True)},
    ]


# ── Габарит ────────────────────────────────────────────────────────────────────────────────────────────────────

IDENTITY = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]


def rotation(axis, degrees):
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
    if axis == "X":
        return [[1, 0, 0], [0, c, -s], [0, s, c]]
    if axis == "Y":
        return [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    return [[c, -s, 0], [s, c, 0], [0, 0, 1]]


def moved(matrix, shift, point):
    return [sum(matrix[r][k] * point[k] for k in range(3)) + shift[r] for r in range(3)]


def chained(outer, outer_shift, inner, inner_shift):
    """Два перетворення підряд: спершу внутрішнє, потім зовнішнє."""
    return ([[sum(outer[r][k] * inner[k][c] for k in range(3)) for c in range(3)] for r in range(3)], moved(outer, outer_shift, inner_shift))


def corners(element, matrix, shift, frame, found):
    """Кути деталі та її дітей у сцені на цьому кадрі: так само, як їх ставить гра."""
    change = frame.get(element["name"], {})
    own = IDENTITY
    for axis in "XYZ":
        angle = element.get("rotation" + axis, 0.0) + change.get("rotation" + axis, 0.0)
        if angle:
            own, _ = chained(own, [0, 0, 0], rotation(axis, angle), [0, 0, 0])

    # Поворот довкола rotationOrigin і зсув кадру
    origin = element["rotationOrigin"]
    back = moved(own, [0, 0, 0], [-v for v in origin])
    offset = [origin[k] + back[k] + change.get("offset" + "XYZ"[k], 0.0) for k in range(3)]
    matrix, shift = chained(matrix, shift, own, offset)

    for x in (element["from"][0], element["to"][0]):
        for y in (element["from"][1], element["to"][1]):
            for z in (element["from"][2], element["to"][2]):
                found.append(moved(matrix, shift, [x, y, z]))

    # Діти рахуються від from батька
    inside = chained(matrix, shift, IDENTITY, element["from"])
    for child in element.get("children", []):
        corners(child, inside[0], inside[1], frame, found)


def envelope(parts, linkage):
    """
    Місце, яке важіль займає разом з ходом ручки, вантажу й покажчика: найдальші точки деталей за все переведення,
    блоків від точки важеля. Тяга до гостряків сюди не входить: вона лежить під власною колією.
    """
    reached = []
    for n in range(41):
        frame = pose(linkage, 0, n / 40)["elements"]
        for part in parts:
            corners(part, IDENTITY, [0, 0, 0], frame, reached)
    across = max(abs(q[0] - LEVER) for q in reached) / VOXELS
    along = max(abs(q[2] - LEVER) for q in reached) / VOXELS
    height = max(q[1] for q in reached) / VOXELS
    return across, along, height


def check(parts, linkage):
    """Правила прокладання беруть місце важеля з коду. Модель має в нього вміщатися, а коса тяга не розтягуватися."""
    problems = []
    across, along, height = envelope(parts, linkage)
    for measured, allowed, name in ((across, REACH_ACROSS, "LeverReachAcross"), (along, REACH_ALONG, "LeverReachAlong")):
        if measured > allowed + 1e-6:
            problems.append("модель сягає на %.3f блока, а SectionBox.%s лише %.2f" % (measured, name, allowed))
        elif allowed - measured > 0.05:
            print("увага: SectionBox.%s = %.2f, а модель сягає лише на %.3f" % (name, allowed, measured))
    if height > BOX_HEIGHT + 0.05:
        problems.append("модель заввишки %.3f блока, а рамка виділення SectionBox.LeverHeight лише %.2f" % (height, BOX_HEIGHT))

    stretch = max(abs(linkage.at(n / 200)[3] - 1) for n in range(201)) * linkage.length
    if stretch > 0.01:
        problems.append("коса тяга мала б міняти довжину на %.3f вокселя" % stretch)

    if problems:
        raise SystemExit("сцену не записано:\n  " + "\n  ".join(problems))
    print("габарит: %.3f блока впоперек колії, %.3f уздовж, висота %.3f" % (across, along, height))


def main():
    linkage = Linkage()
    arm, sign = lever_arm(), mast(linkage)
    still = base_parts()
    check(still + [arm, sign], linkage)

    elements = scenery() + still + [arm, rod(linkage), sign]
    for element in elements:
        paint(element)

    scene = {
        "editor": {"allAngles": False, "entityTextureMode": False},
        "textureWidth": TEXTURE_SIZE,
        "textureHeight": TEXTURE_SIZE,
        "textureSizes": {code: [TEXTURE_SIZE, TEXTURE_SIZE] for code in TEXTURES},
        "textures": TEXTURES,
        "elements": elements,
        "animations": animations(linkage),
    }
    json.dump(scene, open(SCENE, "w", encoding="utf-8", newline="\n"), indent="\t", ensure_ascii=False)
    print("записано %s: вісь колії на %.2f вокселя від важеля, хід тяги %.2f, коса тяга %.3f" % (
        SCENE, TRACK_AXIS - LEVER, STROKE, linkage.length))


main()
