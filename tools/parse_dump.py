#!/usr/bin/env python3
# Parseia o dump de propriedades do RevitWireEditor num seed JSON estruturado.
import re, json, sys

SRC = r"F:/Temp/claude/F--dev-RevitPlugins-expert-tools-ExpertTools/1faee0d3-e679-48b0-968e-82f433b1b5d8/scratchpad/dump-usable.txt"
OUT = r"F:/dev/RevitPlugins/revit-wire-editor/RevitWireEditor/Resources/config-seed.json"

CLASSES = {"ConductorMaterial","ConductorSize","InsulationMaterial",
           "TemperatureRating","CableType","CableSize"}

hdr = re.compile(r"^### (\w+)")
obj = re.compile(r"^  - (.*?)\s+\[id \d+\]\s*$")
prop = re.compile(r"^\s+([A-Za-z]+)\(W\) = (.*)$")   # so propriedades gravaveis

def ref(v):
    v = v.strip()
    if v == "<nenhum>" or v == "null":
        return None
    m = re.match(r"^'(.*)' \(id .*\)$", v)
    return m.group(1) if m else v

def num(v):
    return float(v.strip().replace(",", "."))

data = {c: [] for c in CLASSES}
cur_class = None
cur = None

# --- passada extra: mapeamento UTILIZAVEL (CableType -> [CableSize names]) ---
usable = {}
in_usable = False
umatch = re.compile(r"^  (.+?) -> \[(.*)\]\s*$")
for line in open(SRC, encoding="utf-8"):
    if line.strip().startswith("### CABLETYPE: metodos"):
        in_usable = True; continue
    if in_usable and line.startswith("### ") and "CABLETYPE: metodos" not in line:
        in_usable = False
    if in_usable:
        m = umatch.match(line.rstrip("\n"))
        if m:
            usable[m.group(1)] = [s for s in m.group(2).split(" | ") if s]

with open(SRC, encoding="utf-8") as f:
    for line in f:
        h = hdr.match(line)
        if h:
            cur_class = h.group(1) if h.group(1) in CLASSES else None
            cur = None
            continue
        if cur_class is None:
            continue
        o = obj.match(line)
        if o:
            cur = {"_name": o.group(1)}   # nome do cabecalho = autoritativo
            data[cur_class].append(cur)
            continue
        p = prop.match(line)
        if p and cur is not None:
            cur[p.group(1)] = p.group(2).strip()

# Normaliza cada classe para um formato limpo.
def name(d): return d.get("_name")

seed = {}
seed["conductorMaterials"] = [name(d) for d in data["ConductorMaterial"]]
seed["insulationMaterials"] = [name(d) for d in data["InsulationMaterial"]]
seed["temperatureRatings"] = [name(d) for d in data["TemperatureRating"]]
seed["conductorSizes"] = [{"name": name(d), "diameter": num(d["Diameter"])}
                          for d in data["ConductorSize"]]
seed["cableTypes"] = [{
    "name": name(d),
    "conductorMaterial": ref(d.get("ConductorMaterial","<nenhum>")),
    "insulationMaterial": ref(d.get("InsulationMaterial","<nenhum>")),
    "temperatureRating": ref(d.get("TemperatureRating","<nenhum>")),
    "coreType": d.get("CoreType"),
    "usableCableSizes": usable.get(name(d), []),
} for d in data["CableType"]]
seed["cableSizes"] = [{
    "name": name(d),
    "comments": d.get("Comments",""),
    "hot": ref(d.get("HotConductorSize","<nenhum>")),
    "neutral": ref(d.get("NeutralConductorSize","<nenhum>")),
    "ground": ref(d.get("GroundConductorSize","<nenhum>")),
    "other": ref(d.get("OtherConductorSize","<nenhum>")),
    "nHot": int(d.get("NumberOfHotConductors","0")),
    "nNeutral": int(d.get("NumberOfNeutralConductors","0")),
    "nGround": int(d.get("NumberOfGroundConductors","0")),
    "nOther": int(d.get("NumberOfOtherConductors","0")),
} for d in data["CableSize"]]

import os
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(seed, f, ensure_ascii=False, indent=1)

# Resumo
for k, v in seed.items():
    print(f"{k}: {len(v)}")
print("--- amostras ---")
print("mat:", seed["conductorMaterials"])
print("ins:", seed["insulationMaterials"])
print("temp:", seed["temperatureRatings"])
print("size[0]:", seed["conductorSizes"][0])
print("cabletype[0]:", seed["cableTypes"][0])
print("cablesize[0]:", seed["cableSizes"][0])
# checagem de integridade: refs de cablesize/cabletype existem?
sizenames = {s["name"] for s in seed["conductorSizes"]}
matnames = set(seed["conductorMaterials"])
badrefs = []
for cs in seed["cableSizes"]:
    for r in (cs["hot"],cs["neutral"],cs["ground"],cs["other"]):
        if r and r not in sizenames: badrefs.append((cs["name"], r))
for ct in seed["cableTypes"]:
    if ct["conductorMaterial"] and ct["conductorMaterial"] not in matnames:
        badrefs.append((ct["name"], ct["conductorMaterial"]))
csizenames = {s["name"] for s in seed["cableSizes"]}
for ct in seed["cableTypes"]:
    for u in ct["usableCableSizes"]:
        if u not in csizenames: badrefs.append((ct["name"], "usable:"+u))
print("refs quebradas:", badrefs if badrefs else "nenhuma")
print("--- utilizaveis por tipo ---")
for ct in seed["cableTypes"]:
    print(f"  {ct['name']}: {len(ct['usableCableSizes'])} utilizaveis")
