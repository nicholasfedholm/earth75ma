"""usage: run_server.py <outname> <era> <region> [place]  -- prep config via run_verify.py, run headless server in verify\run-site, /stop after spawn is set."""
import json, os, subprocess, sys, threading, time
S = r"C:\Work\vs\vs_scratch\sessions\20261112-earth-start-merge"
out, era, region = sys.argv[1:4]
place = sys.argv[4] if len(sys.argv) > 4 else ""
run = os.path.join(S, "verify", "run-site")
subprocess.run([sys.executable, os.path.join(S, "tools", "run_verify.py"), "run-site", era, region], check=True, stdout=subprocess.DEVNULL)
p_ = os.path.join(run, "data", "serverconfig.json"); d = json.load(open(p_))
if place: d["WorldConfig"]["WorldConfiguration"]["earthSpawnPlace"] = place
d["Port"] = 42420
json.dump(d, open(p_, "w"), indent=1)
VS = os.environ.get("VS_GAME_DIR", os.path.join(os.environ["APPDATA"], "Vintagestory"))
f = open(os.path.join(S, "verify", out), "w", encoding="utf-8", errors="replace")
p = subprocess.Popen([os.path.join(VS, "VintagestoryServer.exe"), "--dataPath", os.path.join(run, "data"), "--addModPath", os.path.join(run, "mods")],
                     cwd=VS, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, stdin=subprocess.PIPE, text=True, encoding="utf-8", errors="replace")
done = threading.Event()
def rd():
    for line in p.stdout:
        f.write(line); f.flush()
        if "Safe spawn set" in line or "Safe spawn" in line and "kept" in line or "[Server Notification] Safe" in line or "Dedicated Server now running" in line:
            threading.Timer(25, done.set).start()
threading.Thread(target=rd, daemon=True).start()
t0 = time.time()
while time.time() - t0 < 500 and not done.is_set() and p.poll() is None: time.sleep(2)
try: p.stdin.write("/stop\n"); p.stdin.flush()
except Exception: pass
try: p.wait(timeout=60)
except subprocess.TimeoutExpired: p.kill(); print("killed own pid", p.pid)
print("exit", p.returncode, "wall", int(time.time() - t0))
