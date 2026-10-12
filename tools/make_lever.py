# Збирає модель важеля стрілки для гри зі сцени shapes/reference/switchlever-scene.json.
# У сцені, крім важеля, лежать бруси, рейки й гостряки для масштабу (Ref...); у гру йдуть лише деталі Lever...
# та анімація throw: переведення з першого положення в друге. Гра сама ставить її на потрібний кадр.
# Запускати з кореня репозиторію: python tools/make_lever.py
import json

SCENE = "RailWorld/assets/RailWorld/shapes/reference/switchlever-scene.json"
SHAPE = "RailWorld/assets/RailWorld/shapes/part/switchlever.json"
ANIMATION = "throw"


def textures_of(elements, found):
    for element in elements:
        for face in element.get("faces", {}).values():
            found.add(face["texture"].lstrip("#"))
        textures_of(element.get("children", []), found)
    return found


def main():
    scene = json.load(open(SCENE, encoding="utf-8"))
    elements = [e for e in scene["elements"] if e["name"].startswith("Lever")]
    used = sorted(textures_of(elements, set()))
    animations = [a for a in scene.get("animations", []) if a["code"] == ANIMATION]
    if not elements or not animations:
        raise SystemExit("у сцені немає деталей Lever... або анімації " + ANIMATION)

    shape = {
        "textureWidth": scene["textureWidth"],
        "textureHeight": scene["textureHeight"],
        "textureSizes": {code: scene["textureSizes"][code] for code in used},
        # У сцені шляхи без домену, як їх розуміє редактор; грі кажемо прямо, що текстури її власні
        "textures": {code: "game:" + scene["textures"][code].split(":")[-1] for code in used},
        "elements": elements,
        "animations": animations,
    }
    json.dump(shape, open(SHAPE, "w", encoding="utf-8", newline="\n"), indent="\t", ensure_ascii=False)
    print("%s: %d деталей верхнього рівня, текстури %s" % (SHAPE, len(elements), ", ".join(used)))


main()
