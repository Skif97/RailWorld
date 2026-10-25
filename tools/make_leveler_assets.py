# Будує описи триноги і приладів: blocktypes (тринога, нівелір, теодоліт), itemtypes (прилади) і рецепти. У них
# повторюються ті самі частини (породи для творчого меню, текстури за матеріалами, положення в руці й на землі), тож
# вони пишуться звідси, щоб не розходилися. Моделі будує make_leveler_shape.py. Запуск: python tools/make_leveler_assets.py
import json, os

R = os.path.join(os.path.dirname(__file__), "..", "RailWorld", "assets", "RailWorld") + os.sep

WOODS = ["birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony", "walnut", "purpleheart", "aged"]

TRIPOD_TEXTURES = {
    "wood": "game:block/wood/planks/{wood}1",
    "nails": "game:block/metal/sheet/{nails}1",
    "plate": "game:block/metal/sheet/{plate}1",
    "strap": "game:block/leather/plain",
}
INSTRUMENT_TEXTURES = {
    "fresh": "game:block/metal/plate/brass",
    "silver": "game:block/metal/plate/silver",
    "black": "game:block/metal/plate/blackbronze",
    "dark": "game:block/metal/plate/steel",
    "glass": "game:block/glass/smoky",
}

def block(code, instrument, track, height, gui, ground):
    # instrument: предмет приладу на тринозі або None; track: ним прокладають колію; height: висота рамок, блоків
    shape, folded = "block/" + code, "block/" + code + "-folded"
    mapping = dict(TRIPOD_TEXTURES)
    if instrument:
        mapping.update(INSTRUMENT_TEXTURES)
    attributes = {
        "materialTypes": ["wood", "nails", "plate"],
        "translationEntries": ["material-{wood}", "material-{nails}", "material-{plate}"],
        "defaults": ["oak", "iron", "iron"],
        "textureMapping": mapping,
        "shape": {"base": "railworld:" + folded},
    }
    if instrument:
        attributes["instrument"] = instrument
    if track:
        attributes["trackTool"] = True
    # Текстури для частинок при ламанні і як типові
    textures = {k: {"base": v.replace("{wood}", "oak").replace("{nails}", "iron").replace("{plate}", "iron")} for k, v in mapping.items()}
    return {
        "code": code,
        "class": "BlockLevelerTripod",
        "entityClass": "BlockEntityLevelerTripod",
        "behaviors": [
            {"name": "TypedTexture"},
            {"name": "RotateablePlaceable", "properties": {"intervalDeg": 45, "offsetDeg": 90}},
            {"name": "Multiblock", "properties": {"sizex": 1, "sizey": 2, "sizez": 1, "cposition": {"x": 0, "y": 0, "z": 0}}},
        ],
        "entityBehaviors": [{"name": "LevelerRotation", "properties": {"height": 2}}],
        "attributes": attributes,
        "shape": {"base": "railworld:" + shape},
        # Складена модель для меню й рук. Гра кешує лише форми, на які посилаються блоки, а TypedTexture бере форму
        # для рук саме з кешу, тож вона мусить бути формою блока для меню
        "shapeInventory": {"base": "railworld:" + folded},
        "textures": textures,
        "drawtype": "JSON",
        "blockmaterial": "Wood",
        "resistance": 1.5,
        "lightAbsorption": 0,
        "faceCullMode": "NeverCull",
        "sidesolid": {"all": False},
        "sideopaque": {"all": False},
        "replaceable": 90,
        "maxstacksize": 1,
        "creativeinventoryStacks": [{
            "tabs": ["general", "decorative"],
            "stacks": [{"type": "block", "code": "railworld:" + code, "attributes": {"wood": w}} for w in WOODS],
        }],
        "collisionbox": {"x1": 0.3, "y1": 0, "z1": 0.3, "x2": 0.7, "y2": height, "z2": 0.7},
        "selectionbox": {"x1": 0.15, "y1": 0, "z1": 0.15, "x2": 0.85, "y2": height, "z2": 0.85},
        "sounds": {"place": "game:block/planks", "break": "game:block/planks", "hit": "game:block/planks", "walk": "game:walk/wood"},
        # У меню розкладена модель: тринога цілком, у триноги з приладом збільшено верх, тобто сам прилад
        "guiTransform": gui,
        "fpHandTransform": {"translation": {"x": 0, "y": -0.3, "z": 0}, "rotation": {"x": 0, "y": -45, "z": 0}, "origin": {"x": 0.5, "y": 1, "z": 0.5}, "scale": 0.8},
        # Підібрано в грі через .tfedit
        "tpHandTransform": {"translation": {"x": 0.03, "y": -0.15, "z": -0.8}, "rotation": {"x": -35, "y": 0, "z": -84}, "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 0.65},
        "groundTransform": ground,
    }

tripod_gui = {"translation": {"x": 0, "y": 6.05, "z": 0}, "rotation": {"x": -22, "y": -27, "z": 0}, "origin": {"x": 0.5, "y": 0.62, "z": 0.5}, "scale": 1.58}
leveler_gui = {"translation": {"x": 0, "y": -7.66, "z": 0}, "rotation": {"x": -13, "y": -34, "z": -2}, "origin": {"x": 0.5, "y": 1.41, "z": 0.5}, "scale": 2.4}

def write(path, obj):
    with open(R + path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent="\t")

# Положення на землі підібрано в грі через .tfedit; в триноги центр не вказано, тож він типовий
tripod_ground = {"translation": {"x": 0, "y": 0.13, "z": 0}, "rotation": {"x": 87, "y": -53, "z": -30}, "scale": 4.58}
leveler_ground = {"translation": {"x": 0, "y": 0.15, "z": 0}, "rotation": {"x": 89, "y": -28, "z": -24}, "origin": {"x": 0.5, "y": 0.83, "z": 0.5}, "scale": 4.58}

# Теодоліт поки бере положення нівеліра: у них однакова тринога і схожий прилад
write("blocktypes/tripod.json", block("tripod", None, False, 1.3, tripod_gui, tripod_ground))
write("blocktypes/leveler.json", block("leveler", "railworld:levelerinstrument", False, 1.6, leveler_gui, leveler_ground))
write("blocktypes/theodolite.json", block("theodolite", "railworld:theodoliteinstrument", True, 1.65, leveler_gui, leveler_ground))

def instrument_item(code, shape):
    return {
        "code": code,
        "shape": {"base": "railworld:" + shape},
        "textures": {k: {"base": v} for k, v in INSTRUMENT_TEXTURES.items()},
        "maxstacksize": 1,
        "creativeinventory": {"general": ["*"], "items": ["*"]},
        "behaviors": [{
            "name": "GroundStorable",
            "properties": {
                "layout": "SingleCenter",
                "placeRemoveSound": "game:sounds/block/ingot",
                "collisionBox": {"x1": 0.25, "y1": 0, "z1": 0.25, "x2": 0.75, "y2": 0.3, "z2": 0.75},
            },
        }],
        # Предмет у меню гра малює перевернутим відносно блока, тож поворот як у ванільної пластини (metalplate.json)
        "guiTransform": {"translation": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": -30, "y": -44, "z": -180}, "origin": {"x": 0.5, "y": 0.15, "z": 0.5}, "scale": 3.0},
        "fpHandTransform": {"translation": {"x": 0, "y": 0.1, "z": 0}, "rotation": {"x": 0, "y": -45, "z": 0}, "origin": {"x": 0.5, "y": 0.2, "z": 0.5}, "scale": 1.6},
        "tpHandTransform": {"translation": {"x": -0.8, "y": -0.3, "z": -0.6}, "rotation": {"x": 0, "y": 0, "z": 0}, "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 0.9},
        "groundTransform": {"translation": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 0, "y": 0, "z": 0}, "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 2.5},
    }

write("itemtypes/levelerinstrument.json", instrument_item("levelerinstrument", "item/leveler-instrument"))
write("itemtypes/theodoliteinstrument.json", instrument_item("theodoliteinstrument", "item/theodolite-instrument"))

tool = lambda tag: {"type": "item", "tags": [tag], "isTool": True, "toolDurabilityCost": 10}

# Лінзи з кварцу або з прозорого скла, як у рецептах ванільного ліхтаря (lantern.json): кожен рецепт приладу двома
# варіантами, що відрізняються лише лінзами
LENSES = [{"type": "item", "code": "game:clearquartz"}, {"type": "block", "code": "game:glassslab-plain-down-free"}]

def with_lenses(recipe):
    variants = []
    for lens in LENSES:
        r = json.loads(json.dumps(recipe))
        r["ingredients"]["Q"] = lens
        variants.append(r)
    return variants
os.makedirs(R + "recipes/grid", exist_ok=True)
# Тринога: 4 однакові дошки, 2 пластини будь-якого металу, 6 цвяхів одного металу; інструменти лише втрачають міцність
write("recipes/grid/tripod.json", [{
    "ingredientPattern": "MNH,PPC,NNS",
    "ingredients": {
        "P": {"type": "item", "code": "game:plank-{wood}", "quantity": 2},
        "M": {"type": "item", "code": "game:metalplate-*", "quantity": 2},
        "N": {"type": "item", "code": "game:metalnailsandstrips-{nails}", "quantity": 2},
        "S": tool("tool-saw"),
        "C": tool("tool-chisel"),
        "H": tool("tool-hammer"),
    },
    "allowedVariants": {"wood": WOODS},
    "width": 3,
    "height": 3,
    "output": {"type": "block", "code": "railworld:tripod"},
}])
# Прилад: з чого він на моделі. Латунь на корпус і трубу, срібло на обід лімба, чорна бронза на окуляр і стрижні,
# кварц чи скло на лінзи
write("recipes/grid/levelerinstrument.json", with_lenses({
    "ingredientPattern": "QBQ,SNB,HC_",
    "ingredients": {
        "B": {"type": "item", "code": "game:metalplate-brass"},
        "S": {"type": "item", "code": "game:metalbit-silver", "quantity": 2},
        "N": {"type": "item", "code": "game:metalnailsandstrips-blackbronze"},
        "Q": {"type": "item", "code": "game:clearquartz"},
        "C": tool("tool-chisel"),
        "H": tool("tool-hammer"),
    },
    "width": 3,
    "height": 3,
    "output": {"type": "item", "code": "railworld:levelerinstrument"},
}))
# Теодоліт: латунь на корпус, чорна бронза на вісь і зубці, срібна пластина на обід лімба, кварц чи скло на лінзи. Деталі
# паяють, як у ванільного конденсатора: паяльник лише втрачає міцність, а припій (олов'яний чи срібний) витрачається
write("recipes/grid/theodoliteinstrument.json", with_lenses({
    "ingredientPattern": "QKQ,SBN,HIL",
    "ingredients": {
        "K": {"type": "item", "code": "game:metalplate-blackbronze"},
        "B": {"type": "item", "code": "game:metalplate-brass", "quantity": 2},
        "S": {"type": "item", "code": "game:metalplate-silver"},
        "N": {"type": "item", "code": "game:metalnailsandstrips-blackbronze"},
        "Q": {"type": "item", "code": "game:clearquartz"},
        "H": tool("tool-hammer"),
        "I": {"type": "item", "tags": ["tool-solderingiron"], "isTool": True, "toolDurabilityCost": 2},
        "L": {"type": "item", "code": "game:solderbar-*", "allowedVariants": ["tin", "silver"]},
    },
    "width": 3,
    "height": 3,
    "output": {"type": "item", "code": "railworld:theodoliteinstrument"},
}))
print("ok")
