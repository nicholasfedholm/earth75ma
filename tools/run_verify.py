import json, os, subprocess, sys, shutil, time
S = r"C:\Work\vs\vs_scratch\sessions\20261112-earth-start-merge"
name, era, region = sys.argv[1:4]
run = os.path.join(S, "verify", name)
if not os.path.exists(run):
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", r"C:\Work\vs\master-verify\make-verify-run.ps1",
                    "-SessionDir", S, "-RunName", name, "-TestZip", os.path.join(S, "earth75ma-0.0.8.zip")], check=True)
    for f in os.listdir(os.path.join(run, "mods")):
        if f.lower().startswith("earthworldgen") or f.startswith("earth75ma"):
            continue
        os.remove(os.path.join(run, "mods", f))
data = os.path.join(run, "data")
for f in os.listdir(os.path.join(data, "Saves")):
    os.remove(os.path.join(data, "Saves", f))
p = os.path.join(data, "serverconfig.json"); d = json.load(open(p))
wc = d["WorldConfig"]
wc["SaveFileLocation"] = os.path.join(data, "Saves", "default.vcdbs"); wc["Seed"] = "e75verify"
wc["WorldConfiguration"] = {"earthWorldgen": "true", "earthWorldSize": "100352", "worldWidth": "100352", "worldLength": "50176",
    "earthEra": era, "earthSpawnRegion": region}
d["StartupCommands"] = "/stop" if False else d.get("StartupCommands")
json.dump(d, open(p, "w"), indent=1)
print(sorted(os.listdir(os.path.join(run, "mods"))))
