# Будує форми нівеліра: тринога розкладена і складена, прилад окремо, і вони ж разом. Розміри у вокселях (16 на
# блок). Запуск: python tools/make_leveler_shape.py
#
# Тринога за референсами геодезичних штативів: кожна нога з двох паралельних планок з розпірками, знизу
# висувний брусок із хомутом і металевим наконечником, угорі шарнір: вушко голови і болт крізь планки.
# Прилади (нівелір, теодоліт) рахуються у своїх розмірах і зменшуються своїм масштабом довкола свого низу.
import json, math, os

SHAPES = os.path.join(os.path.dirname(__file__), "..", "RailWorld", "assets", "RailWorld", "shapes")

# Текстури триноги беруться з матеріалів крафту, тут лише типові: wood з дощок, nails з цвяхів (гвинти, болти,
# наконечники), plate з металевої пластини (площадка під прилад, хомути, вушка). Прилад поки має сталі текстури
TEXTURES = {
    "wood": "game:block/wood/planks/oak1",
    "nails": "game:block/metal/plate/iron",
    "plate": "game:block/metal/plate/iron",
    "strap": "game:block/leather/plain",
    "fresh": "game:block/metal/plate/brass",
    "silver": "game:block/metal/plate/silver",
    "black": "game:block/metal/plate/blackbronze",
    # Сірі вставки: свіжа сталь рівна, а потьмяніла чорна бронза з різкими плямами, і на дрібних деталях строката
    "dark": "game:block/metal/plate/steel",
    "glass": "game:block/glass/smoky",
}

C = 8.0  # середина блока в плані

# Текстури з волокнами: у дерева гри волокна йдуть уздовж U (горизонтально). Щоб на кожній грані вони йшли вздовж
# довшого боку деталі, на гранях, вищих за ширину, текстура повертається на 90°
GRAIN = {"wood"}

def span(a0, a1, flip=False):
    # Відрізок текстури за положенням грані, а не від кута текстури: сусідні грані тоді продовжують одна одну, а не
    # повторюють той самий шматок. Текстура займає 16 вокселів; відрізок, що вилазить за край, зсувається назад
    if flip:
        a0, a1 = 16 - a1, 16 - a0
    length = min(a1 - a0, 16)
    u0 = a0 % 16
    if u0 + length > 16:
        u0 = 16 - length
    return round(u0, 3), round(u0 + length, 3)

def faces(frm, to, tex):
    (x0, y0, z0), (x1, y1, z1) = frm, to
    # Для кожної грані: її горизонталь і вертикаль. Верх текстури там, де грань вища
    sides = {"north": ((x0, x1), (y0, y1), True), "south": ((x0, x1), (y0, y1), True),
             "east": ((z0, z1), (y0, y1), True), "west": ((z0, z1), (y0, y1), True),
             "up": ((x0, x1), (z0, z1), False), "down": ((x0, x1), (z0, z1), False)}
    result = {}
    for f, (h, v, flip) in sides.items():
        face = {"texture": "#" + tex}
        if tex in GRAIN and v[1] - v[0] > h[1] - h[0]:
            # Гра при повороті лише переставляє кути відрізка текстури між вершинами грані, тож U тут іде вздовж
            # вертикалі грані, а V вздовж горизонталі (ModelCubeUtilExt.AddFace, uvRotation)
            u, w = span(*v), span(*h)
            face["rotation"] = 90
        else:
            u, w = span(*h), span(*v, flip)
        face["uv"] = [u[0], w[0], u[1], w[1]]
        result[f] = face
    return result

def box(name, frm, to, tex, origin=None, rx=0.0, ry=0.0, rz=0.0):
    e = {"name": name, "from": [round(v, 3) for v in frm], "to": [round(v, 3) for v in to], "faces": faces(frm, to, tex)}
    if origin is not None:
        e["rotationOrigin"] = [round(v, 3) for v in origin]
    if rx: e["rotationX"] = round(rx, 3)
    if ry: e["rotationY"] = round(ry, 3)
    if rz: e["rotationZ"] = round(rz, 3)
    return e

def adder(out):
    # Функція, що додає брусок у список елементів форми
    def add(name, frm, to, tex, origin=None, rx=0.0, ry=0.0, rz=0.0):
        out.append(box(name, frm, to, tex, origin=origin, rx=rx, ry=ry, rz=rz))
    return add

# Накладені грані різних елементів не повинні лежати в одній площині, інакше вони мерехтять. Де елементи
# заходять один в один, їхні торці зсуваються на EPS
EPS = 0.005
SIN60 = math.sin(math.radians(60))
COS45 = math.cos(math.radians(45))

def ring(add, name, axis, cx, cy, s0, s1, R, T, tex, solid=False, fill_tex=None, joined=(False, False), sides=8, arc=None):
    """Кругла деталь: окремі пластини (sides штук, типово вісім), що сходяться ребрами на гранях многокутника. R від
    осі до зовнішньої грані пластини, T товщина стінки. axis "y": вісь стоїть, cx і cy це x і z осі; axis "z": вісь
    лежить уздовж Z, cx і cy це x і y; axis "x": уздовж X, cx і cy це y і z. solid: усередину ставиться заповнення,
    щоб деталь була суцільною, а не трубою; fill_tex його текстура, якщо не така, як у пластин. joined: до якого торця
    впритул стає продовження деталі (така сама ring); там торці не зсуваються, бо їх закриває продовження, а зсув
    лишив би щілину. arc: лише пластини, чий кут (від першої, градусів) у цих межах, тобто дуга, а не коло"""
    assert sides % 8 == 0, "заповнення ховається кутами в косих пластинах, тож грані мають бути і під 45°"
    step = 360 / sides
    side = 2 * R * math.tan(math.radians(step / 2))
    for k in range(sides):
        if arc is not None and not arc[0] <= step * k <= arc[1]:
            continue
        # Сусідні пластини заходять одна в одну біля внутрішнього ребра, тож торці через одну трохи коротші
        e = EPS if k % 2 else 0.0
        a0 = s0 if joined[0] else s0 + e
        a1 = s1 if joined[1] else s1 - e
        if axis == "y":
            add("%s%d" % (name, k + 1), [cx + R - T, a0, cy - side / 2], [cx + R, a1, cy + side / 2], tex,
                origin=[cx, s0, cy], ry=step * k)
        elif axis == "z":
            add("%s%d" % (name, k + 1), [cx - side / 2, cy + R - T, a0], [cx + side / 2, cy + R, a1], tex,
                origin=[cx, cy, s0], rz=step * k)
        else:
            add("%s%d" % (name, k + 1), [a0, cx + R - T, cy - side / 2], [a1, cx + R, cy + side / 2], tex,
                origin=[s0, cx, cy], rx=step * k)
    if solid:
        plug(add, name + "Fill", axis, cx, cy, s0 + 2 * EPS, s1 - 2 * EPS, R, T, fill_tex or tex)

def plug(add, name, axis, cx, cy, s0, s1, R, T, tex):
    """Квадратне заповнення всередині восьми пластин (ring): воно закриває отвір, а кутами ховається в косих
    пластинах. Влазить, лише коли стінка не тонша за R * (1 - cos 45°)"""
    lo, hi = R - T, R * COS45
    assert lo < hi, (name, "стінка затонка для заповнення")
    a = (lo + hi) / 2
    if axis == "y":
        add(name, [cx - a, s0, cy - a], [cx + a, s1, cy + a], tex)
    elif axis == "z":
        add(name, [cx - a, cy - a, s0], [cx + a, cy + a, s1], tex)
    else:
        add(name, [s0, cx - a, cy - a], [s1, cx + a, cy + a], tex)

def truncated_triangle(add, name, y0, y1, r, t, tex):
    """Трикутна плита зі зрізаними кутами: довгі грані на відстані r від осі в напрямках 0, 120, 240 градусів,
    зрізи на відстані t між ними. Складається з шести брусків, кожен від своєї грані всередину; бруски заходять
    один в один, тож верх і низ кожного трохи зсунуті"""
    w = (t - 0.5 * r) / SIN60            # пів довгої грані
    s = r * SIN60 - 0.5 * w              # пів зрізу
    a = SIN60 * 2 * w - 2 * r + 0.05     # докуди довгий брусок заходить за вісь, щоб не вилізти за сусідню грань
    pieces = [(0.0, a, r, w), (120.0, a, r, w), (240.0, a, r, w), (60.0, 0.0, t, s), (180.0, 0.0, t, s), (300.0, 0.0, t, s)]
    check_truncated_triangle(r, t, pieces)
    for k, (ry, near, far, half) in enumerate(pieces):
        d = k * EPS
        add("%s%d" % (name, k + 1), [C + near, y0 + d, C - half], [C + far, y1 - d, C + half], tex, origin=[C, y0, C], ry=ry)

def check_truncated_triangle(r, t, pieces):
    # Кути брусків не виходять за плиту, і бруски разом вкривають усю плиту
    normals = [(math.radians(a), r) for a in (0, 120, 240)] + [(math.radians(a), t) for a in (60, 180, 300)]
    def inside(x, z, tol=1e-6):
        return all(x * math.cos(n) + z * math.sin(n) <= d + tol for n, d in normals)
    def local(x, z, ry):
        n = math.radians(ry)
        return x * math.cos(n) + z * math.sin(n), -x * math.sin(n) + z * math.cos(n)
    for ry, near, far, half in pieces:
        n = math.radians(ry)
        for u in (near, far):
            for v in (-half, half):
                assert inside(u * math.cos(n) - v * math.sin(n), u * math.sin(n) + v * math.cos(n)), ("кут бруска поза плитою", ry)
    steps = 200
    for i in range(steps + 1):
        for j in range(steps + 1):
            x, z = -t + 2 * t * i / steps, -t + 2 * t * j / steps
            if not inside(x, z, -1e-3):
                continue
            assert any(near <= u <= far and -half <= v <= half
                       for ry, near, far, half in pieces for u, v in [local(x, z, ry)]), ("дірка в плиті", x, z)


# ── Тринога
HEAD = 19.0            # верх ніг, низ голови: окуляр приладу тоді трохи нижче очей гравця
TILT = 13.0            # нахил розкладеної ноги назовні, градусів
RADIUS = 2.2           # на якій відстані від осі шарнір ноги
SLAT_W, SLAT_T = 0.7, 0.45    # планка: ширина (назовні) і товщина
GAP = 1.2              # просвіт між двома планками ноги
UPPER_BOTTOM = 7.0     # де кінчаються планки
LOWER_TOP = 10.0       # докуди висувний брусок заходить між планки
TIP_DEPTH = 0.57       # на скільки вістря розкладеної ноги в землі
RETRACT = 4.0          # на скільки висувний брусок засунутий у складеної триноги
HEAD_SIDE = 2.6        # від осі до довгої грані плити голови
HEAD_CUT = 3.3         # від осі до зрізаного кута
HEAD_T = 0.6           # товщина плити

# Низ наконечника в системі ноги: такий, щоб після нахилу ноги довкола шарніра вістря було на TIP_DEPTH у землі
_t = math.radians(TILT)
FOOT = HEAD - (HEAD + RADIUS * math.sin(_t) + TIP_DEPTH) / math.cos(_t)

def leg(out, i, ry, tilt, retract):
    origin = [C, HEAD, C]
    x0 = C + RADIUS - SLAT_W / 2
    x1 = C + RADIUS + SLAT_W / 2
    half = GAP / 2
    def part(name, frm, to, tex):
        out.append(box("leg%d-%s" % (i, name), frm, to, tex, origin=origin, rz=tilt, ry=ry))
    # Дві планки
    part("slatA", [x0, UPPER_BOTTOM, C - half - SLAT_T], [x1, HEAD - 0.2, C - half], "wood")
    part("slatB", [x0, UPPER_BOTTOM, C + half], [x1, HEAD - 0.2, C + half + SLAT_T], "wood")
    # Дві розпірки між ними
    for k, y in enumerate((UPPER_BOTTOM + 3.5, HEAD - 5.0)):
        part("spacer%d" % (k + 1), [x0 + 0.05, y, C - half], [x1 - 0.05, y + 0.9, C + half], "wood")
    # Хомут на кінці планок; нижче за кінці планок, щоб низ планок не лежав у площині низу хомута
    part("clamp", [x0 - 0.12, UPPER_BOTTOM - 0.1, C - half - SLAT_T - 0.12], [x1 + 0.12, UPPER_BOTTOM + 0.7, C + half + SLAT_T + 0.12], "plate")
    part("clampScrew", [x1 + 0.12, UPPER_BOTTOM + 0.15, C - 0.2], [x1 + 0.5, UPPER_BOTTOM + 0.55, C + 0.2], "nails")
    # Висувна нижня частина з наконечником: оковка на бруску і вістря, що звужується донизу. У складеної триноги
    # вона засунута між планки
    foot = FOOT + retract
    xc = C + RADIUS
    part("lower", [x0 + 0.1, foot + 2.1, C - 0.3], [x1 - 0.1, LOWER_TOP + retract, C + 0.3], "wood")
    part("shoe", [x0 + 0.05, foot + 1.2, C - 0.35], [x1 - 0.05, foot + 2.3, C + 0.35], "nails")
    for k, (bottom, top, w) in enumerate(((foot + 0.7, foot + 1.2, 0.22), (foot + 0.3, foot + 0.7, 0.13), (foot, foot + 0.3, 0.06))):
        part("spike%d" % (k + 1), [xc - w, bottom, C - w], [xc + w, top, C + w], "nails")
    # Шарнір: болт крізь обидві планки під головою
    part("bolt", [x0 + 0.15, HEAD - 1.0, C - half - SLAT_T - 0.2], [x1 - 0.15, HEAD - 0.6, C + half + SLAT_T + 0.2], "nails")

def tripod(tilt, retract):
    out = []
    for i, ry in enumerate((0, 120, 240)):
        leg(out, i + 1, ry, tilt, retract)
    # Голова триноги: трикутна плита зі зрізаними кутами, довгі грані дивляться на ноги. Поверх неї кругла
    # металева площадка, на яку стає прилад
    truncated_triangle(adder(out), "headPlate", HEAD, HEAD + HEAD_T, HEAD_SIDE, HEAD_CUT, "wood")
    for i, ry in enumerate((0, 120, 240)):
        # Вушко лише торкається плити знизу й не доходить до її грані, щоб їхні грані не лежали в одній площині
        out.append(box("lug%d" % (i + 1), [C + RADIUS - 0.5, HEAD - 1.2, C - 0.55], [C + HEAD_SIDE - 0.1, HEAD, C + 0.55], "plate",
                       origin=[C, HEAD, C], ry=ry))
    ring(adder(out), "headTop", "y", C, C, HEAD + HEAD_T, HEAD + HEAD_T + 0.3, 2.2, 0.7, "plate", solid=True)
    return out

def touching_tilt():
    """Нахил складених ніг досередини, за якого сусідні ноги якраз торкаються. Нога в своїй системі стоїть уздовж X від
    осі; площина посередині між нею і сусідньою лежить під 60° до неї, і внутрішній кут кожної деталі ноги на кожному
    її кінці по висоті не має заходити за цю площину. Шукається найбільший нахил, за якого ще не заходить"""
    parts = []
    leg(parts, 0, 0, 0.0, RETRACT)
    def clearance(tilt):
        t = math.radians(tilt)
        worst = float("inf")
        for e in parts:
            w = max(abs(e["from"][2] - C), abs(e["to"][2] - C))
            for y in (e["from"][1], e["to"][1]):
                # Внутрішнє ребро деталі після нахилу ноги довкола шарніра (як rz у box)
                r = (e["from"][0] - C) * math.cos(t) - (y - HEAD) * math.sin(t)
                worst = min(worst, r * math.tan(math.radians(60)) - w)
        return worst
    lo, hi = -20.0, 0.0      # на lo ноги вже заходять одна в одну, на hi ще ні
    assert clearance(lo) < 0 < clearance(hi)
    for _ in range(50):
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if clearance(mid) < 0 else (lo, mid)
    return hi

FOLDED_TILT = touching_tilt()

STRAP_Y = 11.0         # висота стрічки на складених ногах
STRAP_H, STRAP_T = 0.8, 0.15

def strap(out, tilt):
    # Стрічка, що стягує складені ноги: шість відрізків довкола ніг. Три лежать на зовнішніх гранях ніг, три між
    # ногами по краях планок; кінці відрізків там, де перетинаються сусідні лінії
    t = math.radians(tilt)
    y = STRAP_Y + STRAP_H / 2
    # Зовнішня грань ноги на висоті стрічки після нахилу ноги довкола шарніра, і пів ширини ноги по планках
    outer = (RADIUS + SLAT_W / 2) * math.cos(t) - (y - HEAD) * math.sin(t)
    edge = GAP / 2 + SLAT_T
    between = outer * math.cos(math.radians(60)) + edge * math.sin(math.radians(60))
    lines = [(0, outer), (60, between), (120, outer), (180, between), (240, outer), (300, between)]
    def cross(a, b):
        (na, da), (nb, db) = a, b
        ca, sa = math.cos(math.radians(na)), math.sin(math.radians(na))
        cb, sb = math.cos(math.radians(nb)), math.sin(math.radians(nb))
        det = ca * sb - sa * cb
        return ((da * sb - db * sa) / det, (ca * db - cb * da) / det)
    for k, (n, d) in enumerate(lines):
        # Кінці відрізка вздовж його лінії
        p0, p1 = cross(lines[k - 1], lines[k]), cross(lines[k], lines[(k + 1) % 6])
        a = math.radians(n)
        z0, z1 = sorted(-p[0] * math.sin(a) + p[1] * math.cos(a) for p in (p0, p1))
        # Сусідні відрізки заходять один в один на кутах, тож через один трохи нижчі
        e = EPS * (k % 2)
        out.append(box("strap%d" % (k + 1), [C + d, STRAP_Y + e, C + z0], [C + d + STRAP_T, STRAP_Y + STRAP_H - e, C + z1], "strap",
                       origin=[C, STRAP_Y, C], ry=n))

# ── Прилади у своїх розмірах; низ приладу в (C, 0, C) його системи, труба вздовж Z, об'єктив з боку малих Z.
# Кожен прилад зменшується своїм масштабом, щоб окуляр на тринозі був трохи нижче очей гравця
BASE = HEAD + HEAD_T + 0.3     # де стоїть низ приладу на тринозі
inst = []

def ibox(name, frm, to, tex, origin=None, rx=0.0, ry=0.0, rz=0.0):
    inst.append((name, frm, to, tex, origin, rx, ry, rz))

# ── Нівелір
LEVELER_SCALE = 0.539
# Прилад зі свіжої латуні, бо його крафтять. Стрижні гвинтів, окуляр і ковпачки рівня зі сталі: прилад
# зібраний з різних деталей
# Підставка з трьома підйомними гвинтами
ring(ibox, "tribrach", "y", C, C, 0.0, 0.8, 2.6, 0.8, "fresh", solid=True)
for i, ry in enumerate((30, 150, 270)):
    ibox("screwStem%d" % (i + 1), [C + 2.0, 0.8, C - 0.2], [C + 2.4, 2.0, C + 0.2], "dark", origin=[C, 0, C], ry=ry)
    ibox("screwKnob%d" % (i + 1), [C + 1.75, 1.0, C - 0.45], [C + 2.65, 1.5, C + 0.45], "fresh", origin=[C, 0, C], ry=ry)
# Верхня плита підставки кругла і ширша за коло гвинтів, щоб кожен гвинт стояв під нею
ring(ibox, "plate", "y", C, C, 2.0, 2.4, 2.9, 0.9, "fresh", solid=True)
# Вертикальна вісь (торці закриті плитою й лімбом) і лімб
ring(ibox, "axis", "y", C, C, 2.4, 4.2, 0.9, 0.3, "fresh")
# Лімб: латунний диск зі срібним ободом, на якому шкала, бо на сріблі поділки читаються краще
ring(ibox, "circle", "y", C, C, 4.2, 4.6, 2.3, 0.7, "silver", solid=True, fill_tex="fresh")
# Балка вздовж приладу
ibox("beam", [C - 0.6, 4.6, 3.6], [C + 0.6, 5.4, 12.4], "fresh")
# Y-опори: стояк і вилка з двох зубців обабіч труби
TUBE_Y = 7.6           # вісь труби над низом приладу
TUBE_R = 1.0
for name, z in (("front", 4.4), ("back", 10.8)):
    # Деталі опори стикуються, а не заходять одна в одну: стояк вужчий за основу вилки, зубці стоять на ній
    ibox("post-" + name, [C - 0.4, 5.4, z + 0.1], [C + 0.4, 6.0, z + 0.7], "fresh")
    ibox("forkL-" + name, [C - TUBE_R - 0.4, 6.4, z], [C - TUBE_R, TUBE_Y + 0.3, z + 0.8], "fresh")
    ibox("forkR-" + name, [C + TUBE_R, 6.4, z], [C + TUBE_R + 0.4, TUBE_Y + 0.3, z + 0.8], "fresh")
    ibox("forkBase-" + name, [C - TUBE_R - 0.4, 6.0, z], [C + TUBE_R + 0.4, 6.4, z + 0.8], "fresh")
# Зорова труба, однакова в нівеліра і теодоліта: порожня всередині й відкрита ззаду. Спереду ширший об'єктив, у ньому
# лінза; ззаду з труби виходить вужчий окуляр з наочником і своєю лінзою. Тіло з темної чорної бронзи, щоб не
# відблискувало, на кінцях латунні обойми; збоку гвинт фокусування, згори рівень. y це висота осі труби, радіус TUBE_R
def scope(y):
    ring(ibox, "tubeFront", "z", C, y, 2.6, 3.6, TUBE_R, 0.3, "fresh", joined=(False, True))
    ring(ibox, "tube", "z", C, y, 3.6, 12.8, TUBE_R, 0.3, "black", joined=(True, True))
    ring(ibox, "tubeBack", "z", C, y, 12.8, 13.6, TUBE_R, 0.3, "fresh", joined=(True, False))
    ring(ibox, "objective", "z", C, y, 0.6, 3.0, TUBE_R + 0.45, 0.45, "fresh")
    plug(ibox, "lens", "z", C, y, 1.0, 1.1, TUBE_R + 0.45, 0.45, "glass")
    # Окуляр заходить у трубу і стоїть щільно по її внутрішній стінці, тож між ними немає просвіту
    eye_r = TUBE_R - 0.3
    ring(ibox, "eyepiece", "z", C, y, 13.0, 15.6, eye_r, 0.25, "dark")
    plug(ibox, "eyeLens", "z", C, y, 15.0, 15.1, eye_r, 0.25, "glass")
    ring(ibox, "eyecup", "z", C, y, 15.2, 15.8, eye_r + 0.25, 0.25, "dark")
    ibox("focusStem", [C + TUBE_R, y - 0.2, 8.6], [C + TUBE_R + 0.6, y + 0.2, 9.0], "dark")
    ibox("focusKnob", [C + TUBE_R + 0.6, y - 0.55, 8.35], [C + TUBE_R + 0.9, y + 0.55, 9.25], "fresh")
    top = y + TUBE_R
    ibox("vial", [C - 0.45, top, 5.6], [C + 0.45, top + 0.6, 10.4], "fresh")
    ibox("bubble", [C - 0.3, top + 0.6, 6.6], [C + 0.3, top + 0.65, 9.4], "glass")
    ibox("vialCapF", [C - 0.55, top - 0.05, 5.4], [C + 0.55, top + 0.7, 5.8], "dark")
    ibox("vialCapB", [C - 0.55, top - 0.05, 10.2], [C + 0.55, top + 0.7, 10.6], "dark")

scope(TUBE_Y)
LEVELER = inst

# ── Теодоліт: класичний, як XIX століття. Знизу підставка з підйомними гвинтами, широка плита з горизонтальним
# лімбом (срібний обід) і компасом посередині, два рівні на плиті. На плиті дві стійки-«А», нагорі горизонтальна вісь;
# на ній вертикальна дуга з зубцями, по яких ходить фіксатор на кронштейні, а над віссю зорова труба, як у нівеліра
THEODOLITE_SCALE = LEVELER_SCALE
inst = []

# Підставка з трьома підйомними гвинтами і верхня плита підставки над ними
ring(ibox, "tribrach", "y", C, C, 0.0, 0.6, 2.2, 0.7, "fresh", solid=True)
for i, ry in enumerate((30, 150, 270)):
    ibox("screwStem%d" % (i + 1), [C + 1.6, 0.6, C - 0.15], [C + 1.9, 1.6, C + 0.15], "dark", origin=[C, 0, C], ry=ry)
    ibox("screwKnob%d" % (i + 1), [C + 1.4, 0.8, C - 0.35], [C + 2.1, 1.2, C + 0.35], "fresh", origin=[C, 0, C], ry=ry)
ring(ibox, "tribrachTop", "y", C, C, 1.6, 1.9, 2.4, 0.75, "fresh", solid=True)
ring(ibox, "spindle", "y", C, C, 1.9, 2.4, 0.8, 0.3, "fresh")
# Плита з горизонтальним лімбом: латунний диск зі срібним ободом
PLATE_TOP = 2.8
ring(ibox, "limb", "y", C, C, 2.4, PLATE_TOP, 4.0, 1.2, "silver", solid=True, fill_tex="fresh", sides=16)
# Компас посередині плити: латунна коробка, срібна картушка на дні, темна стрілка, скло зверху
ring(ibox, "compass", "y", C, C, PLATE_TOP, PLATE_TOP + 0.5, 1.6, 0.5, "fresh")
plug(ibox, "compassCard", "y", C, C, PLATE_TOP + 2 * EPS, PLATE_TOP + 0.05, 1.6, 0.5, "silver")
ibox("needle", [C - 0.08, PLATE_TOP + 0.1, C - 0.9], [C + 0.08, PLATE_TOP + 0.15, C + 0.9], "dark")
plug(ibox, "compassGlass", "y", C, C, PLATE_TOP + 0.4, PLATE_TOP + 0.45, 1.6, 0.5, "glass")
# Два рівні на плиті під прямим кутом: оправа, скло бульбашки, ковпачки
def plate_vial(name, frm, to, along_x):
    ibox(name, frm, to, "fresh")
    (x0, y0, z0), (x1, y1, z1) = frm, to
    if along_x:
        ibox(name + "Bubble", [x0 + 0.3, y1, z0 + 0.08], [x1 - 0.3, y1 + 0.04, z1 - 0.08], "glass")
    else:
        ibox(name + "Bubble", [x0 + 0.08, y1, z0 + 0.3], [x1 - 0.08, y1 + 0.04, z1 - 0.3], "glass")
plate_vial("plateVialX", [C - 1.0, PLATE_TOP, C + 2.95], [C + 1.0, PLATE_TOP + 0.35, C + 3.35], True)
plate_vial("plateVialZ", [C + 2.95, PLATE_TOP, C - 1.0], [C + 3.35, PLATE_TOP + 0.35, C + 1.0], False)

# Стійки-«А» обабіч кола: по два похилі бруски від плити до осі і поперечка між ними
AXIS_Y = 7.7           # горизонтальна вісь над низом приладу
STAND_X = 2.2          # від середини до внутрішньої грані стійки
FOOT_Z = 2.2           # як далеко від середини стоять ноги стійки
leg_len = math.hypot(FOOT_Z, AXIS_Y - PLATE_TOP)
leg_tilt = math.degrees(math.atan2(FOOT_Z, AXIS_Y - PLATE_TOP))
for side, x0 in (("L", C - STAND_X - 0.4), ("R", C + STAND_X)):
    for end, zf, tilt in (("front", C - FOOT_Z, leg_tilt), ("back", C + FOOT_Z, -leg_tilt)):
        # Брусок заходить у плиту знизу, щоб стійка не стояла на ній лише ребром
        ibox("stand%s-%s" % (side, end), [x0, PLATE_TOP - 0.35, zf - 0.25], [x0 + 0.4, PLATE_TOP + leg_len, zf + 0.25], "fresh",
             origin=[x0, PLATE_TOP, zf], rx=tilt)
    cross_y = PLATE_TOP + 1.6
    cross_z = FOOT_Z * (1 - (cross_y - PLATE_TOP) / (AXIS_Y - PLATE_TOP))
    ibox("standCross" + side, [x0 + 0.05, cross_y, C - cross_z], [x0 + 0.35, cross_y + 0.35, C + cross_z], "fresh")
    ibox("bearing" + side, [x0 - 0.05, AXIS_Y - 0.45, C - 0.45], [x0 + 0.45, AXIS_Y + 0.45, C + 0.45], "fresh")
# Горизонтальна вісь крізь стійки
ibox("hAxis", [C - STAND_X - 0.6, AXIS_Y - 0.3, C - 0.3], [C + STAND_X + 0.6, AXIS_Y + 0.3, C + 0.3], "dark")

# Вертикальна дуга на осі: нижня частина великого кола, вінець з двадцяти чотирьох пластин, з яких лишено нижні
ARC_R = 4.0            # від осі до зовнішньої грані вінця
ARC_T = 0.6            # товщина вінця
ARC_W = 0.35           # пів ширини вінця вздовж осі
ARC = (120, 240)       # які кути кола лишаються (0 це верх)
ring(ibox, "vArc", "x", AXIS_Y, C, C - ARC_W, C + ARC_W, ARC_R, ARC_T, "fresh", sides=24, arc=ARC)
# Спиці від маточини до вінця
for k, rx in enumerate((135, 180, 225)):
    ibox("spoke%d" % (k + 1), [C - 0.12, AXIS_Y + 0.35, C - 0.15], [C + 0.12, AXIS_Y + ARC_R - ARC_T + 0.05, C + 0.15], "fresh",
         origin=[C, AXIS_Y, C], rx=rx)
ibox("hub", [C - 0.55, AXIS_Y - 0.55, C - 0.55], [C + 0.55, AXIS_Y + 0.55, C + 0.55], "fresh")

def on_arc(angle, r):
    # Точка на колі дуги: кут від верху, так само, як повертаються пластини (поворот довкола X)
    a = math.radians(angle)
    return AXIS_Y + r * math.cos(a), C + r * math.sin(a)

# Зубці: квадратні бруски, повернуті кутом назовні й наполовину втоплені у вінець, тож назовні стирчать трикутники
TOOTH = 0.32
for k in range(int((ARC[1] - ARC[0]) / 7.5) - 1):
    angle = ARC[0] + 7.5 * (k + 1)
    ty, tz = on_arc(angle, ARC_R)
    ibox("tooth%d" % (k + 1), [C - ARC_W + 0.05, ty - TOOTH / 2, tz - TOOTH / 2], [C + ARC_W - 0.05, ty + TOOTH / 2, tz + TOOTH / 2], "dark",
         origin=[C, ty, tz], rx=angle + 45)

# Фіксатор, що ходить по зубцях: колодка на дузі з гвинтом, тримається на кронштейні з плити
LOCK_ANGLE = 212
ly, lz = on_arc(LOCK_ANGLE, ARC_R + 0.15)
ibox("lockBlock", [C - ARC_W - 0.25, ly - 0.45, lz - 0.45], [C + ARC_W + 0.25, ly + 0.45, lz + 0.45], "fresh",
     origin=[C, ly, lz], rx=LOCK_ANGLE)
ibox("lockStem", [C + ARC_W + 0.25, ly - 0.12, lz - 0.12], [C + ARC_W + 0.9, ly + 0.12, lz + 0.12], "dark")
ibox("lockKnob", [C + ARC_W + 0.9, ly - 0.35, lz - 0.35], [C + ARC_W + 1.15, ly + 0.35, lz + 0.35], "fresh")
ibox("lockPost", [C - ARC_W - 0.2, PLATE_TOP + 0.2, lz - 0.2], [C - ARC_W + 0.1, ly - 0.2, lz + 0.2], "fresh")
ibox("lockFoot", [C - ARC_W - 0.4, PLATE_TOP, lz - 0.4], [C - ARC_W + 0.3, PLATE_TOP + 0.2, lz + 0.4], "fresh")

# Зорова труба, як у нівеліра, на підставці, що тримається на осі
SCOPE_Y = AXIS_Y + 0.55 + TUBE_R + 0.5
ibox("scopeMount", [C - 0.75, AXIS_Y + 0.55, C - 2.2], [C + 0.75, SCOPE_Y - TUBE_R + 0.01, C + 2.2], "fresh")
scope(SCOPE_Y)

THEODOLITE = inst

def instrument(parts, scale, base):
    # Прилад повертається довкола своєї осі, щоб труба лежала вздовж X: об'єктив над першою ногою (вона стоїть рівно
    # з гранню блока), окуляр між двома іншими, щоб тому, хто дивиться в окуляр, ноги триноги не заважали. Потім
    # прилад зменшується довкола середини свого низу, і низ стає на висоту base
    def placed(p):
        x, y, z = C - (p[2] - C), p[1], C + (p[0] - C)
        return [C + (x - C) * scale, base + y * scale, C + (z - C) * scale]
    out = []
    for name, frm, to, tex, origin, rx, ry, rz in parts:
        # Після повороту довкола Y вісь Z приладу дивиться в бік -X, а вісь X у бік +Z, тож поворот довкола Z стає
        # зворотним поворотом довкола X, а поворот довкола X поворотом довкола Z. Кожна деталь повернута лише довкола
        # однієї осі, тож порядок поворотів не важить
        assert sum(1 for r in (rx, ry, rz) if r) <= 1, name
        a, b = placed(frm), placed(to)
        out.append(box(name, [min(a[i], b[i]) for i in range(3)], [max(a[i], b[i]) for i in range(3)], tex,
                       origin=placed(origin) if origin is not None else None, rx=-rz, ry=ry, rz=rx))
    return out

def check_coplanar(elements):
    # Грані двох елементів з однаковим поворотом, що дивляться в один бік, лежать в одній площині й перекриваються,
    # мерехтять. Стик (грані дивляться назустріч) не заважає
    def rotation(e):
        angles = tuple(e.get(k, 0) for k in ("rotationX", "rotationY", "rotationZ"))
        return (tuple(e["rotationOrigin"]) if any(angles) else None, angles)
    for i, a in enumerate(elements):
        for b in elements[i + 1:]:
            if rotation(a) != rotation(b):
                continue
            for axis in range(3):
                others = [k for k in range(3) if k != axis]
                if not all(min(a["to"][k], b["to"][k]) - max(a["from"][k], b["from"][k]) > 1e-3 for k in others):
                    continue
                for side in ("from", "to"):
                    assert abs(a[side][axis] - b[side][axis]) > 1e-3, ("грані в одній площині", a["name"], b["name"])

def write(path, elements):
    # Форма для гри і її копія для редактора моделей: він не розуміє домен у шляху текстури (game:), тож там шляхи
    # без нього. Тека reference у збірку не йде
    check_coplanar(elements)
    used = {f["texture"][1:] for e in elements for f in e["faces"].values()}
    textures = {k: v for k, v in TEXTURES.items() if k in used}
    shape = {"editor": {"allAngles": True, "entityTextureMode": False}, "textureWidth": 16, "textureHeight": 16,
             "textures": textures, "elements": elements}
    editor = {k: v.split(":", 1)[-1] for k, v in textures.items()}
    for folder, tex in ((os.path.dirname(path), textures), ("reference", editor)):
        with open(os.path.join(SHAPES, folder, os.path.basename(path)), "w", encoding="utf-8") as f:
            json.dump(dict(shape, textures=tex), f, ensure_ascii=False, indent="\t")
    lo, hi = min(e["from"][1] for e in elements), max(e["to"][1] for e in elements)
    print("%-28s elements %3d  y %.2f..%.2f" % (path, len(elements), lo, hi))

folded = tripod(FOLDED_TILT, RETRACT)
strap(folded, FOLDED_TILT)

write("block/tripod.json", tripod(TILT, 0.0))
write("block/tripod-folded.json", folded)
write("item/leveler-instrument.json", instrument(LEVELER, LEVELER_SCALE, 0.0))
write("block/leveler.json", tripod(TILT, 0.0) + instrument(LEVELER, LEVELER_SCALE, BASE))
write("block/leveler-folded.json", folded + instrument(LEVELER, LEVELER_SCALE, BASE))
write("item/theodolite-instrument.json", instrument(THEODOLITE, THEODOLITE_SCALE, 0.0))
write("block/theodolite.json", tripod(TILT, 0.0) + instrument(THEODOLITE, THEODOLITE_SCALE, BASE))
write("block/theodolite-folded.json", folded + instrument(THEODOLITE, THEODOLITE_SCALE, BASE))
print("theodolite eyepiece blocks", round((BASE + SCOPE_Y * THEODOLITE_SCALE) / 16, 3))

# Найнижча точка вістря розкладеної ноги після нахилу довкола шарніра
t = math.radians(TILT)
print("tip y", round(HEAD + RADIUS * math.sin(t) + (FOOT - HEAD) * math.cos(t), 2), "folded tilt", round(FOLDED_TILT, 2))
