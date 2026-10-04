#!/usr/bin/env python3
# Compara o dump do RESULTADO contra o seed embutido (referencia).
import re, json

SRC = r"F:/Temp/claude/F--dev-RevitPlugins-expert-tools-ExpertTools/1faee0d3-e679-48b0-968e-82f433b1b5d8/scratchpad/dump-result.txt"
SEED = r"C:/dev/RevitPlugins/revit-wire-editor/RevitWireEditor/Resources/config-seed.json"

CLASSES = {"ConductorMaterial","ConductorSize","InsulationMaterial",
           "TemperatureRating","CableType","CableSize"}
hdr = re.compile(r"^### (\w+)")
obj = re.compile(r"^  - (.*?)\s+\[id \d+\]\s*$")
prop = re.compile(r"^\s+([A-Za-z]+)\(W\) = (.*)$")

def ref(v):
    v=v.strip()
    if v in ("<nenhum>","null"): return None
    m=re.match(r"^'(.*)' \(id .*\)$",v); return m.group(1) if m else v
def num(v): return float(v.strip().replace(",","."))

# parse usable
usable={}; inu=False; um=re.compile(r"^  (.+?) -> \[(.*)\]\s*$")
for line in open(SRC,encoding="utf-8"):
    if line.strip().startswith("### CABLETYPE: metodos"): inu=True; continue
    if inu and line.startswith("### ") and "CABLETYPE: metodos" not in line: inu=False
    if inu:
        m=um.match(line.rstrip("\n"))
        if m: usable[m.group(1)]=[s for s in m.group(2).split(" | ") if s]

data={c:[] for c in CLASSES}; cc=None; cur=None
for line in open(SRC,encoding="utf-8"):
    h=hdr.match(line)
    if h: cc=h.group(1) if h.group(1) in CLASSES else None; cur=None; continue
    if cc is None: continue
    o=obj.match(line)
    if o: cur={"_name":o.group(1)}; data[cc].append(cur); continue
    p=prop.match(line)
    if p and cur is not None: cur[p.group(1)]=p.group(2).strip()

def nm(d): return d.get("_name")
res={}
res["conductorMaterials"]=sorted(nm(d) for d in data["ConductorMaterial"])
res["insulationMaterials"]=sorted(nm(d) for d in data["InsulationMaterial"])
res["temperatureRatings"]=sorted(nm(d) for d in data["TemperatureRating"])
res["conductorSizes"]={nm(d):round(num(d["Diameter"]),9) for d in data["ConductorSize"]}
res["cableTypes"]={nm(d):(ref(d.get("ConductorMaterial","<nenhum>")),ref(d.get("InsulationMaterial","<nenhum>")),ref(d.get("TemperatureRating","<nenhum>")),d.get("CoreType"),tuple(sorted(usable.get(nm(d),[])))) for d in data["CableType"]}
res["cableSizes"]={nm(d):(ref(d.get("HotConductorSize","<nenhum>")),ref(d.get("NeutralConductorSize","<nenhum>")),ref(d.get("GroundConductorSize","<nenhum>")),ref(d.get("OtherConductorSize","<nenhum>")),int(d.get("NumberOfHotConductors","0")),int(d.get("NumberOfNeutralConductors","0")),int(d.get("NumberOfGroundConductors","0")),int(d.get("NumberOfOtherConductors","0")),d.get("Comments","")) for d in data["CableSize"]}

seed=json.load(open(SEED,encoding="utf-8"))
ref_={}
ref_["conductorMaterials"]=sorted(seed["conductorMaterials"])
ref_["insulationMaterials"]=sorted(seed["insulationMaterials"])
ref_["temperatureRatings"]=sorted(seed["temperatureRatings"])
ref_["conductorSizes"]={s["name"]:round(s["diameter"],9) for s in seed["conductorSizes"]}
ref_["cableTypes"]={c["name"]:(c["conductorMaterial"],c["insulationMaterial"],c["temperatureRating"],c["coreType"],tuple(sorted(c["usableCableSizes"]))) for c in seed["cableTypes"]}
ref_["cableSizes"]={s["name"]:(s["hot"],s["neutral"],s["ground"],s["other"],s["nHot"],s["nNeutral"],s["nGround"],s["nOther"],s["comments"]) for s in seed["cableSizes"]}

def diff_list(name,a,b):
    sa,sb=set(a),set(b)
    extra=sb-sa; falta=sa-sb
    print(f"[{name}] resultado={len(b)} ref={len(a)} | sobrando_no_resultado={sorted(extra) or '-'} | faltando={sorted(falta) or '-'}")
def diff_map(name,a,b):
    sa,sb=set(a),set(b)
    extra=sb-sa; falta=sa-sb
    difval=[k for k in (sa&sb) if a[k]!=b[k]]
    print(f"[{name}] resultado={len(b)} ref={len(a)} | sobrando={sorted(extra) or '-'} | faltando={sorted(falta) or '-'} | valores_diferentes={len(difval)}")
    for k in difval[:8]:
        print(f"     * {k}:\n         ref={a[k]}\n         res={b[k]}")

diff_list("conductorMaterials",ref_["conductorMaterials"],res["conductorMaterials"])
diff_list("insulationMaterials",ref_["insulationMaterials"],res["insulationMaterials"])
diff_list("temperatureRatings",ref_["temperatureRatings"],res["temperatureRatings"])
diff_map("conductorSizes",ref_["conductorSizes"],res["conductorSizes"])
diff_map("cableTypes",ref_["cableTypes"],res["cableTypes"])
diff_map("cableSizes",ref_["cableSizes"],res["cableSizes"])
