"""
Модель пасажирського вагона широкої колії (shapes/reference і shapes/entity/wagon-passenger.json). Запуск з будь-якої
теки: python tools/make_wagon_shape.py. Логотип УЗ і смуги рахує uz_logo.py (покриття фігури прямокутниками, близько хвилини).
"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
SHAPES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "RailWorld", "assets", "RailWorld", "shapes")

U = 16.0  # одиниць форми на блок (метр)
CX, CZ = 8.0, 8.0  # центр вагона в координатах форми; y = 0 це верх головки рейки

def box(name, x0, x1, y0, y1, z0, z1, tex, rot=None, children=None, flat=False):
    """Прямокутник у метрах відносно центру вагона: x уздовж, z впоперек, y від рейки."""
    f = [CX + x0 * U, y0 * U, CZ + z0 * U]
    t = [CX + x1 * U, y1 * U, CZ + z1 * U]
    sx, sy, sz = t[0] - f[0], t[1] - f[1], t[2] - f[2]
    def uv(a, b):
        # flat: усі прямокутники беруть той самий маленький шматок текстури, тож колір у них однаковий і швів немає
        if flat:
            return [7.0, 7.0, 8.0, 8.0]
        return [0.0, 0.0, round(min(16.0, a), 3), round(min(16.0, b), 3)]
    faces = {
        "north": {"texture": "#" + tex, "uv": uv(sx, sy)},
        "south": {"texture": "#" + tex, "uv": uv(sx, sy)},
        "east": {"texture": "#" + tex, "uv": uv(sz, sy)},
        "west": {"texture": "#" + tex, "uv": uv(sz, sy)},
        "up": {"texture": "#" + tex, "uv": uv(sx, sz)},
        "down": {"texture": "#" + tex, "uv": uv(sx, sz)},
    }
    e = {"name": name, "from": [round(v, 3) for v in f], "to": [round(v, 3) for v in t],
         "rotationOrigin": [round((f[0] + t[0]) / 2, 3), round((f[1] + t[1]) / 2, 3), round((f[2] + t[2]) / 2, 3)],
         "faces": faces}
    if rot:
        for k, v in rot.items():
            e[k] = v
    if children:
        e["children"] = children
    return e

# Розміри: вкорочений пасажирський вагон колії 1520. Довжина й база як у вагона метро (81-717), щоб на кривих
# найменшого радіуса широкої колії кузов виходив за колію не більше ніж на пів блока; перерізи, висоти й ліврея
# пасажирського (Аммендорф A/Ku). Ширина 3.10: усередині стане три блоки. Метри
L = 18.6        # довжина кузова зовні
COUPLERS = 19.2  # по осях зчеплення автозчепів
W = 3.10        # ширина кузова
FLOOR = 1.125   # низ кузова над рейкою
EAVE = 3.6      # верх боковин
ROOF = 4.25     # верх даху (з дефлекторами 4.357)
BOGIE = 6.3     # від центру до шворня візка (база 12.6)
WHEELBASE = 2.4
WHEEL_R = 0.475
GAUGE = 1.52
COUPLER_Y = 1.06

hl, hw = L / 2, W / 2
els = []

# ── Кузов ──
els.append(box("Body", -hl, hl, FLOOR, EAVE, -hw, hw, "body"))
# Дах дугою: три сходинки, вужчі догори
els.append(box("Roof1", -hl, hl, EAVE, EAVE + 0.25, -hw + 0.06, hw - 0.06, "roof"))
els.append(box("Roof2", -hl + 0.05, hl - 0.05, EAVE + 0.25, EAVE + 0.47, -hw + 0.4, hw - 0.4, "roof"))
els.append(box("Roof3", -hl + 0.1, hl - 0.1, EAVE + 0.47, ROOF, -hw + 0.9, hw - 0.9, "roof"))
# Дефлектори вентиляції вздовж даху
for i in range(9):
    x = -hl + 2.0 + (L - 4.0) * i / 8
    els.append(box(f"Deflector{i + 1}", x - 0.2, x + 0.2, ROOF, ROOF + 0.107, -0.2, 0.2, "roof"))

# Ліврея нових вагонів УЗ: синій кузов, білі двері, літери УЗ біля одного торця. Дві смуги, як бруски логотипа,
# тягнуться вздовж вагона з косими зрізами на кінцях
import uz_logo
LOGO_H = 0.55          # висота логотипа
LOGO_Y = 1.4           # низ логотипа
LOGO_X0 = 1.6          # від краю кузова, як дивитися на бік, до логотипа
STRIPES_END = L - 2.6  # де закінчуються смуги, від того самого краю
LS = LOGO_H / 457      # метрів на піксель логотипа

def shape_boxes(side, rects, origin_u, tag):
    """Прямокутники, що покривають фігуру (uz_logo.cover), на боці вагона; піксель x стоїть на origin_u + x * LS.
    Глибина трохи різна в сусідніх, щоб вони не билися за неї."""
    sgn = -1 if side == "L" else 1
    def model(lx, ly):
        u = origin_u + lx * LS
        return (hl - u if side == "L" else -hl + u), LOGO_Y + (457 - ly) * LS
    parts = []
    for k, (p, q, n, t) in enumerate(rects):
        (px, py), (qx, qy) = model(*p), model(*q)
        (rx, ry) = model(p[0] + n[0] * t, p[1] + n[1] * t)
        length = math.hypot(qx - px, qy - py)
        thick = math.hypot(rx - px, ry - py)
        cx = (px + qx) / 2 + (rx - px) / 2
        cy = (py + qy) / 2 + (ry - py) / 2
        za, zb = sgn * hw, sgn * (hw + 0.012 + 0.0004 * (k % 3))
        e = box(f"{tag}{side}{k}", cx - length / 2, cx + length / 2, cy - thick / 2, cy + thick / 2, min(za, zb), max(za, zb), "stripe", flat=True)
        angle = math.degrees(math.atan2(qy - py, qx - px))
        if angle > 90: angle -= 180
        elif angle <= -90: angle += 180
        if abs(angle) > 1e-6:
            e["rotationZ"] = round(angle, 3)
        parts.append(e)
    return parts

LETTERS, _ = uz_logo.cover(uz_logo.letters, 0, uz_logo.LETTERS_END + 10, goal=0.975, extra=2)
def poly_cover(poly):
    xs = [pt[0] for pt in poly]
    return uz_logo.cover(uz_logo.convex(poly), min(xs) - 4, max(xs) + 4)[0]
STARTS = [poly_cover(uz_logo.TOP_START), poly_cover(uz_logo.BOTTOM_START)]
FINISHES = [poly_cover(uz_logo.TOP_FINISH), poly_cover(uz_logo.BOTTOM_FINISH)]
END_ORIGIN = STRIPES_END - 1280 * LS

for side in ("L", "R"):
    els.extend(shape_boxes(side, LETTERS, LOGO_X0, "Logo"))
    for k, (start, finish, (ly0, ly1)) in enumerate(zip(STARTS, FINISHES, (uz_logo.TOP_BAR, uz_logo.BOTTOM_BAR))):
        name = ("Top", "Bottom")[k]
        els.extend(shape_boxes(side, start, LOGO_X0, f"Stripe{name}Start"))
        els.extend(shape_boxes(side, finish, END_ORIGIN, f"Stripe{name}Finish"))
        # Пряма частина смуги між зрізами
        ua, ub = LOGO_X0 + (uz_logo.STRIPE_START - 2) * LS, END_ORIGIN + (uz_logo.STRIPE_FINISH + 2) * LS
        xa, xb = (hl - ub, hl - ua) if side == "L" else (-hl + ua, -hl + ub)
        sgn = -1 if side == "L" else 1
        za, zb = sgn * hw, sgn * (hw + 0.012)
        els.append(box(f"Stripe{name}{side}", xa, xb, LOGO_Y + (457 - ly1) * LS, LOGO_Y + (457 - ly0) * LS, min(za, zb), max(za, zb), "stripe", flat=True))

# Вікна: 9 рівно між торцями
win_w, win_y0, win_y1 = 1.05, 2.1, 3.05
n = 9
first, last = -hl + 2.15, hl - 2.15
xs = [first + (last - first) * i / (n - 1) for i in range(n)]
FRAME = 0.06  # біла рамка навколо вікна, як на вагонах УЗ
for i, x in enumerate(xs):
    for side, sgn in (("L", -1), ("R", 1)):
        def layer(k0, k1):
            a, b = sgn * (hw + k0), sgn * (hw + k1)
            return min(a, b), max(a, b)
        g0, g1 = layer(0.0, 0.02)
        els.append(box(f"Window{side}{i + 1}", x - win_w / 2, x + win_w / 2, win_y0, win_y1, g0, g1, "glass"))
        f0, f1 = layer(0.0, 0.025)
        x0, x1, y0, y1 = x - win_w / 2, x + win_w / 2, win_y0, win_y1
        els.append(box(f"WindowFrameTop{side}{i + 1}", x0 - FRAME, x1 + FRAME, y1, y1 + FRAME, f0, f1, "stripe"))
        els.append(box(f"WindowFrameBottom{side}{i + 1}", x0 - FRAME, x1 + FRAME, y0 - FRAME, y0, f0, f1, "stripe"))
        els.append(box(f"WindowFrameLeft{side}{i + 1}", x0 - FRAME, x0, y0, y1, f0, f1, "stripe"))
        els.append(box(f"WindowFrameRight{side}{i + 1}", x1, x1 + FRAME, y0, y1, f0, f1, "stripe"))

# Двері тамбурів: полотно в рамці, віконце, поручні обабіч; відкидні сходинки під ними
for end, x in (("A", -hl + 0.62), ("B", hl - 0.62)):
    for side, sgn in (("L", -1), ("R", 1)):
        face = sgn * hw  # зовнішня площина торцевої панелі
        def layer(k0, k1):
            a, b = face + sgn * k0, face + sgn * k1
            return (min(a, b), max(a, b))
        d0, d1 = FLOOR + 0.05, 3.25
        z0, z1 = layer(0.01, 0.02)
        els.append(box(f"Door{end}{side}", x - 0.4, x + 0.4, d0, d1, z0, z1, "door"))
        f0, f1 = layer(0.02, 0.03)
        els.append(box(f"DoorFrameTop{end}{side}", x - 0.45, x + 0.45, d1, d1 + 0.05, f0, f1, "doorframe"))
        els.append(box(f"DoorFrameLeft{end}{side}", x - 0.45, x - 0.4, d0, d1, f0, f1, "doorframe"))
        els.append(box(f"DoorFrameRight{end}{side}", x + 0.4, x + 0.45, d0, d1, f0, f1, "doorframe"))
        els.append(box(f"DoorGap{end}{side}", x - 0.4, x + 0.4, 1.95, 1.98, f0, f1, "doorframe"))
        els.append(box(f"DoorWindow{end}{side}", x - 0.25, x + 0.25, 2.3, 3.0, f0, f1, "doorglass"))
        h0, h1 = layer(0.03, 0.08)
        for hand, hx in (("L", x - 0.55), ("R", x + 0.55)):
            els.append(box(f"Handrail{end}{side}{hand}", hx - 0.025, hx + 0.025, 1.3, 2.9, h0, h1, "handrail"))
    els.append(box(f"Steps{end}", x - 0.4, x + 0.4, 0.5, FLOOR, -hw + 0.05, hw - 0.05, "frame"))

# Торці: гармошка переходу і автозчеп
for end, s in (("A", -1), ("B", 1)):
    x0, x1 = (hl, hl + 0.25) if s > 0 else (-hl - 0.25, -hl)
    els.append(box(f"Gangway{end}", x0, x1, FLOOR + 0.1, 3.3, -0.6, 0.6, "bellows"))
    reach = (COUPLERS - L) / 2
    cx0, cx1 = (hl, hl + reach) if s > 0 else (-hl - reach, -hl)
    els.append(box(f"Coupler{end}", cx0, cx1, COUPLER_Y - 0.14, COUPLER_Y + 0.14, -0.14, 0.14, "frame"))

# ── Рама і обладнання під підлогою: ящики між візками, як на кресленні ──
els.append(box("Sole", -hl + 0.25, hl - 0.25, FLOOR - 0.2, FLOOR, -hw + 0.15, hw - 0.15, "frame"))
boxes = [(-4.3, -3.1, "L"), (-2.8, -1.5, "R"), (-0.9, 0.9, "L"), (1.4, 2.7, "R"), (3.1, 4.3, "L")]
for i, (a, b, side) in enumerate(boxes):
    z0, z1 = (-hw + 0.3, -0.2) if side == "L" else (0.2, hw - 0.3)
    els.append(box(f"Underbox{i + 1}", a, b, 0.45, FLOOR - 0.2, z0, z1, "frame"))

def ring(name, x, yc, z0, z1, radius, thickness, segments, tex):
    """Кільце навколо осі колеса: шматки на верху кола, кожен повернутий навколо центру колеса."""
    parts = []
    half = math.pi * radius / segments * 1.04  # півдовжини шматка з напуском, щоб між шматками не було щілин
    for k in range(segments):
        e = box(f"{name}{k + 1}", x - half, x + half, yc + radius - thickness, yc + radius, z0, z1, tex)
        e["rotationOrigin"] = [round(CX + x * U, 3), round(yc * U, 3), e["rotationOrigin"][2]]
        angle = 360.0 * k / segments
        if angle:
            e["rotationZ"] = round(angle if angle <= 180 else angle - 360, 3)
        parts.append(e)
    return parts

def wheel(name, x, z, inward):
    """Колесо: обід з 16 шматків, як у вагонетки, реборда з внутрішнього боку, суцільний диск і маточина."""
    yc = WHEEL_R
    rim_w = 0.13
    parts = ring(name + "Rim", x, yc, z - rim_w / 2, z + rim_w / 2, WHEEL_R, 0.07, 16, "wheel")
    # Реборда тримає колесо на рейці: трохи більша за обід, з боку середини колії
    fz = z - inward * rim_w / 2
    parts += ring(name + "Flange", x, yc, min(fz, fz - inward * 0.03), max(fz, fz - inward * 0.03), WHEEL_R + 0.03, 0.05, 16, "wheel")
    # Суцільний диск під ободом, як у справжнього вагонного колеса: вісім смуг на весь діаметр, повернутих на
    # 22.5° одна відносно одної. Смуга завширшки 2·r·sin(11.25°) покриває все коло радіуса r і виступає за нього
    # лише на 2%, а виступ ховається під ободом
    disc = WHEEL_R - 0.06
    half = disc * math.sin(math.pi / 16)
    for k in range(8):
        e = box(f"{name}Disc{k + 1}", x - disc, x + disc, yc - half, yc + half, z - 0.03, z + 0.03, "wheel")
        if k:
            e["rotationZ"] = 22.5 * k
        parts.append(e)
    parts.append(box(name + "Hub", x - 0.09, x + 0.09, yc - 0.09, yc + 0.09, z - 0.08, z + 0.08, "frame"))
    return parts

# ── Візки ──
def bogie(name, xc):
    parts = []
    # Бокові рами за колесами, надресорна балка поперек
    for side, z in (("L", -GAUGE / 2 - 0.12), ("R", GAUGE / 2 + 0.12)):
        parts.append(box(f"{name}Frame{side}", xc - 1.55, xc + 1.55, 0.35, 0.75, z - 0.1, z + 0.1, "frame"))
        # Пружини між рамою і кузовом
        parts.append(box(f"{name}Spring{side}", xc - 0.25, xc + 0.25, 0.75, FLOOR - 0.25, z - 0.12, z + 0.12, "spring"))
    parts.append(box(f"{name}Bolster", xc - 0.3, xc + 0.3, 0.55, 0.85, -GAUGE / 2 - 0.2, GAUGE / 2 + 0.2, "frame"))
    # Колісні пари: колесо восьмикутником з двох квадратів, повернутих на 45°
    for axle, dx in (("1", -WHEELBASE / 2), ("2", WHEELBASE / 2)):
        x = xc + dx
        parts.append(box(f"{name}Axle{axle}", x - 0.08, x + 0.08, WHEEL_R - 0.08, WHEEL_R + 0.08, -GAUGE / 2 - 0.1, GAUGE / 2 + 0.1, "frame"))
        for side, z in (("L", -GAUGE / 2), ("R", GAUGE / 2)):
            parts += wheel(f"{name}Wheel{axle}{side}", x, z, -1 if side == "L" else 1)
    return parts

els += bogie("BogieA", -BOGIE)
els += bogie("BogieB", BOGIE)

shape = {
    "editor": {"allAngles": False, "entityTextureMode": False},
    "textureWidth": 16, "textureHeight": 16,
    "textures": {
        "body": "block/liquid/dye/blue",
        "roof": "block/stone/plaster/plain",
        "stripe": "block/cloth/linen/white",
        "glass": "block/clay/hardened/gray",
        "door": "block/cloth/linen/white",
        "doorframe": "block/clay/hardened/black",
        "doorglass": "block/clay/hardened/gray",
        "handrail": "block/clay/hardened/black",
        "bellows": "block/clay/hardened/black",
        "frame": "block/metal/plate/lead",
        "spring": "block/metal/plate/steel",
        "wheel": "block/metal/plate/steel",
    },
    "elements": els,
}

out = os.path.join(SHAPES, "reference", "wagon-passenger.json")
with open(out, "w", encoding="utf-8") as fh:
    json.dump(shape, fh, ensure_ascii=False, indent="\t")
print(len(els), "elements")

# ── Модель для гри (shapes/entity): візки групами, що повертаються анімацією ──
import copy

def relative(e, origin):
    e = copy.deepcopy(e)
    for key in ("from", "to", "rotationOrigin"):
        e[key] = [round(e[key][i] - origin[i], 3) for i in range(3)]
    return e

entity_els = []
groups = {}
for e in els:
    prefix = e["name"][:6]
    if prefix in ("BogieA", "BogieB"):
        groups.setdefault(prefix, []).append(e)
    else:
        entity_els.append(copy.deepcopy(e))

MAX_TURN = 30  # у EntityWagon.MaxBogieTurn те саме
MAX_PITCH = 15  # у EntityWagon.MaxBogiePitch те саме
animations = []
for prefix, parts in groups.items():
    parent = copy.deepcopy(next(p for p in parts if p["name"] == prefix + "Bolster"))
    xc = (parent["from"][0] + parent["to"][0]) / 2
    parent["name"] = prefix
    # Візок повертається навколо шворня і нахиляється вздовж колії навколо точки на головці рейки під ним
    parent["rotationOrigin"] = [round(xc, 3), 0.0, round(CZ, 3)]
    parent["children"] = [relative(p, parent["from"]) for p in parts if p["name"] != prefix + "Bolster"]
    entity_els.append(parent)
    # Нахил візка вздовж колії: поворот навколо поперечної осі (Z моделі)
    animations.append({
        "name": prefix + "Pitch", "code": prefix.lower() + "pitch", "quantityframes": 61,
        "onActivityStopped": "Stop", "onAnimationEnd": "Hold",
        "keyframes": [
            {"frame": 0, "elements": {prefix: {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": -MAX_PITCH}}},
            {"frame": 60, "elements": {prefix: {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": MAX_PITCH}}},
        ],
    })
    animations.append({
        "name": prefix, "code": prefix.lower(), "quantityframes": 61,
        "onActivityStopped": "Stop", "onAnimationEnd": "Hold",
        "keyframes": [
            {"frame": 0, "elements": {prefix: {"rotationX": 0.0, "rotationY": -MAX_TURN, "rotationZ": 0.0}}},
            {"frame": 60, "elements": {prefix: {"rotationX": 0.0, "rotationY": MAX_TURN, "rotationZ": 0.0}}},
        ],
    })

entity_shape = copy.deepcopy(shape)
entity_shape["elements"] = entity_els
entity_shape["animations"] = animations
entity_shape["textures"] = {k: "game:" + v for k, v in shape["textures"].items()}
out = os.path.join(SHAPES, "entity", "wagon-passenger.json")
with open(out, "w", encoding="utf-8") as fh:
    json.dump(entity_shape, fh, ensure_ascii=False, indent="\t")
print(len(entity_els), "top-level elements in entity shape")
