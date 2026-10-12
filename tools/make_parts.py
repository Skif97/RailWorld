"""Переносить деталі колії з заготовки для редактора в моделі, які малює гра.

Заготовка: RailWorld/assets/RailWorld/shapes/reference/section.json. Це одна секція колії в масштабі гри,
16 вокселів на блок: шпала, дві рейки і деталі, домальовані під лівою рейкою. Деталі розпізнаються за
початком назви кубика:

    Fastening...       скріплення звичайної колії: підкладка і два костилі  ->  shapes/part/fastening.json
    FasteningPlate...  сама підкладка, без костилів                         ->  shapes/part/plate.json
    Joint...           стикова накладка                                     ->  shapes/part/joint.json
    Bolt...            головка болта на шийці                               ->  shapes/part/bolt.json
    SpikeSidePad       прокладка під половиною підошви; з двох таких        ->  shapes/part/pad.json
    SpikeSide...       пара костилів з одного боку підошви                  ->  shapes/part/spikepair.json

Скріплення і накладка намальовані під лівою рейкою. Болт, прокладка і пара костилів на правій, із зовнішнього
її боку: ліва зайнята. Болт гра ставить на шийки хрестовини і контррейок, головкою назовні.

У стрілках і перетинах скріплення складається з частин: підкладка (або прокладка, де підкладці немає місця)
і пари костилів, окремо з кожного боку рейки. Так з кожного боку костилі можна не ставити, якщо там до рейки
прилягає гостряк, контррейка чи інша рейка.

Гра ставить кожну таку модель точкою (8, 0, 8) на вісь рейки посередині секції, віссю Z уздовж рейки,
Y = 0 на рівні низу підошви рейки. Тому кубики переносяться так, щоб вісь рейки, біля якої їх намальовано,
і середина шпали стали в цю точку. Під другу рейку гра ставить ту саму модель, повернуту на пів оберта.

Запуск із кореня репозиторію:  python tools/make_parts.py
"""
import copy
import json
import os

SHAPES = os.path.join("RailWorld", "assets", "RailWorld", "shapes")
SOURCE = os.path.join(SHAPES, "reference", "section.json")

# Осі двох рейок заготовки і середина її шпали, вокселів: вісь колії X = 8, пів ширини колії 0.39 блока,
# секція завдовжки 8 зі шпалою посередині
LEFT_RAIL = 8.0 - 0.39 * 16
RIGHT_RAIL = 8.0 + 0.39 * 16
SLEEPER_MIDDLE = 4.0

# На якій відстані від осі рейки стоїть стрижень костиля, вокселів: край підошви і пів стрижня
SPIKE_ASIDE = 1.15

# Назва моделі -> які кубики заготовки в неї йдуть і вісь, що стає в точку X = 8 моделі.
# Пара костилів ставиться не на вісь рейки, а в місце самих костилів, тому її вісь зсунута на SPIKE_ASIDE
PARTS = {
    "fastening": (lambda name: name.startswith("Fastening"), LEFT_RAIL),
    "plate": (lambda name: name.startswith("FasteningPlate"), LEFT_RAIL),
    "joint": (lambda name: name.startswith("Joint"), LEFT_RAIL),
    "bolt": (lambda name: name.startswith("Bolt"), RIGHT_RAIL),
    "spikepair": (lambda name: name.startswith("SpikeSide") and name != "SpikeSidePad", RIGHT_RAIL + SPIKE_ASIDE),
    # Половина прокладки: сама в гру не йде, з двох таких складається pad
    "_padhalf": (lambda name: name == "SpikeSidePad", RIGHT_RAIL),
}
# Товщина підкладки і на скільки пластина під гостряки виступає за підошву рамної рейки, вокселів
PLATE_THICKNESS = 0.25
SLIDE_OVERHANG = 1.4

# Моделі, зібрані з іншої моделі та її копії, повернутої на пів оберта довкола осі рейки
DOUBLED = {"pad": "_padhalf"}


def moved(element, axis):
    assert "children" not in element, "вкладені кубики не переносяться: " + element["name"]
    result = copy.deepcopy(element)
    for key in ("from", "to", "rotationOrigin"):
        if key in result:
            x, y, z = result[key]
            result[key] = [round(x - axis + 8.0, 4), y, round(z - SLEEPER_MIDDLE + 8.0, 4)]
    # Метал підставляє гра: той самий, що в рейки над деталлю
    for face in result["faces"].values():
        face["texture"] = "#metal"
    return result


def turned(element):
    # Пів оберта довкола прямовисної осі через точку (8, 0, 8)
    result = copy.deepcopy(element)
    result["name"] = element["name"] + "Turned"
    low = [round(16.0 - element["to"][0], 4), element["from"][1], round(16.0 - element["to"][2], 4)]
    high = [round(16.0 - element["from"][0], 4), element["to"][1], round(16.0 - element["from"][2], 4)]
    result["from"], result["to"] = low, high
    if "rotationOrigin" in result:
        x, y, z = result["rotationOrigin"]
        result["rotationOrigin"] = [round(16.0 - x, 4), y, round(16.0 - z, 4)]
    return result


def write(name, elements):
    shape = {
        "editor": {"allAngles": False, "entityTextureMode": False},
        "textureWidth": 32,
        "textureHeight": 32,
        "textureSizes": {"metal": [32, 32]},
        "textures": {"metal": "game:block/metal/plate/iron"},
        "elements": elements,
    }
    target = os.path.join(SHAPES, "part", name + ".json")
    with open(target, "w", encoding="utf-8", newline="\n") as out:
        json.dump(shape, out, indent="\t", ensure_ascii=False)
    print(target + ": кубиків " + str(len(elements)))


def main():
    source = json.load(open(SOURCE, encoding="utf-8-sig"))
    os.makedirs(os.path.join(SHAPES, "part"), exist_ok=True)

    made = {}
    for name, (belongs, axis) in PARTS.items():
        elements = [moved(e, axis) for e in source["elements"] if belongs(e["name"])]
        if not elements:
            print("у заготовці немає кубиків для " + name + ": модель не чіпаю")
            continue
        made[name] = elements
        if not name.startswith("_"):
            write(name, elements)

    for name, single in DOUBLED.items():
        if single in made:
            write(name, made[single] + [turned(e) for e in made[single]])

    write("slideplate", [slide_plate()])


def slide_plate():
    # Пластина на брусі стрілки, по якій ковзають гостряки: упоперек колії, від рамної рейки до рамної
    # і ще на SLIDE_OVERHANG за підошву кожної. Завтовшки з підкладку, завширшки з неї ж уздовж колії.
    # У заготовці її немає: вона лягла б точно на підкладки. Вісь колії в точці (8, 0, 8), X упоперек колії
    half = 0.39 * 16 + 1.0 + SLIDE_OVERHANG
    low, high = [8.0 - half, -PLATE_THICKNESS, 6.4], [8.0 + half, 0.0, 9.6]
    width, length = high[0] - low[0], high[2] - low[2]

    def face(u, v):
        return {"texture": "#metal", "uv": [0.0, 0.0, round(min(u, 32.0), 2), round(min(v, 32.0), 2)]}

    return {
        "name": "SlidePlate",
        "from": [round(low[0], 4), low[1], low[2]],
        "to": [round(high[0], 4), high[1], high[2]],
        "rotationOrigin": [8.0, low[1], 8.0],
        "faces": {
            "north": face(width, PLATE_THICKNESS), "south": face(width, PLATE_THICKNESS),
            "east": face(length, PLATE_THICKNESS), "west": face(length, PLATE_THICKNESS),
            "up": face(width, length), "down": face(width, length),
        },
    }


if __name__ == "__main__":
    main()
