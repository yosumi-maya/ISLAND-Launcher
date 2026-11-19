"""Exercise the shipped agent in a real JVM against minimal Minecraft API fixtures."""
import base64
import pathlib
import subprocess
import sys
import tempfile

jdk = pathlib.Path(sys.argv[1])
agent = pathlib.Path(__file__).resolve().parents[2] / "src/Island.Core/Assets/island-quick-play-1.0.jar"
with tempfile.TemporaryDirectory(prefix="island-continue-") as temporary:
    root = pathlib.Path(temporary)
    sources = {
        "net/minecraft/client/gui/GuiMainMenu.java": "package net.minecraft.client.gui; public class GuiMainMenu {}",
        "lumien/custommainmenu/gui/GuiCustom.java": """package lumien.custommainmenu.gui;
public class GuiCustom { public Config guiConfig = new Config(); public static class Config { public String name = "mainmenu"; } }""",
        "net/minecraft/client/Minecraft.java": """package net.minecraft.client;
import java.util.concurrent.*;
public class Minecraft {
 public static final Minecraft instance = new Minecraft();
 public volatile Object field_71441_e;
 public volatile Object field_71462_r;
 public final BlockingQueue<FutureTask<?>> queue = new LinkedBlockingQueue<>();
 public Future<?> func_152344_a(Runnable action) { FutureTask<?> task = new FutureTask<Object>(action, null); queue.add(task); return task; }
 public void func_71371_a(String folder, String name, Settings settings) {
  if (!Thread.currentThread().getName().equals("main")) throw new AssertionError("Wrong thread");
  if (!new java.io.File("saves/" + folder + "/level.dat").isFile()) throw new AssertionError("Missing world");
  field_71441_e = new Object(); System.out.println("OPEN=" + folder);
 }
 public static class Settings {}
}""",
        "Fixture.java": """import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiMainMenu;
import lumien.custommainmenu.gui.GuiCustom;
import java.util.concurrent.*;
public class Fixture {
 public static void main(String[] args) throws Exception {
  Minecraft client = Minecraft.instance;
  if (args[0].equals("custom") || args[0].equals("credits")) {
   GuiCustom custom = new GuiCustom();
   if (args[0].equals("credits")) custom.guiConfig.name = "credits";
   client.field_71462_r = custom;
  } else client.field_71462_r = new GuiMainMenu();
  long end = System.currentTimeMillis() + 2200;
  while (System.currentTimeMillis() < end) {
   FutureTask<?> task = client.queue.poll(100, TimeUnit.MILLISECONDS);
   if (task != null) {
    if (args[0].equals("deleted")) new java.io.File("saves/世界 with spaces/level.dat").delete();
    if (args[0].equals("changed")) client.field_71462_r = new Object();
    task.run(); task.get();
   }
  }
 }
}""",
        "bee.java": "public class bee {}",
        "bao.java": """import java.util.concurrent.*;
public class bao {
 public static final bao M = new bao(); public volatile Object f; public volatile Object n = new bee();
 private final BlockingQueue<FutureTask<?>> queue = new LinkedBlockingQueue<>();
 public Future<?> a(Runnable action) { FutureTask<?> task = new FutureTask<Object>(action, null); queue.add(task); return task; }
 public void a(String folder, String title, Settings settings) {
  if (!Thread.currentThread().getName().equals("main")) throw new AssertionError("Wrong thread");
  f = new Object(); System.out.println("OPEN=" + folder);
 }
 public static class Settings {}
 public static void main(String[] args) throws Exception {
  long end = System.currentTimeMillis() + 2200;
  while (System.currentTimeMillis() < end) { FutureTask<?> task = M.queue.poll(100, TimeUnit.MILLISECONDS); if (task != null) { task.run(); task.get(); } }
 }
}"""
    }
    sources["blr.java"] = "public class blr {}"
    sources["bib.java"] = sources["bao.java"].replace("bao", "bib").replace("bee", "blr").replace("Object n =", "Object m =").replace("public volatile Object f;", "public volatile Object f; public Object n = new Object();")
    source_paths = []
    for name, content in sources.items():
        path = root / "src" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        source_paths.append(str(path))
    classes = root / "classes"
    subprocess.run([str(jdk / "bin/javac.exe"), "--release", "8", "-encoding", "UTF-8", "-d", str(classes)] + source_paths, check=True)
    for mode, main, opens in [("normal", "Fixture", True), ("custom", "Fixture", True), ("obfuscated", "bao", True), ("obfuscated-1.12", "bib", True),
                              ("credits", "Fixture", False), ("missing", "Fixture", False), ("deleted", "Fixture", False),
                              ("changed", "Fixture", False), ("outside", "Fixture", False), ("no-agent", "Fixture", False)]:
        work = root / mode
        world = work / ("other" if mode == "outside" else "saves") / "世界 with spaces"
        world.mkdir(parents=True)
        if mode != "missing":
            (world / "level.dat").write_bytes(b"fixture")
        encoded = base64.urlsafe_b64encode(str(world).encode("utf-8")).decode("ascii").rstrip("=")
        command = [str(jdk / "bin/java.exe"), "-Dfile.encoding=UTF-8"]
        if mode != "no-agent": command.append("-javaagent:" + str(agent) + "=" + encoded)
        command += ["-cp", str(classes), main, mode]
        result = subprocess.run(command, cwd=work, capture_output=True, encoding="utf-8", timeout=15)
        assert result.returncode == 0, result.stderr
        assert ("OPEN=世界 with spaces" in result.stdout) == opens, (mode, result.stdout, result.stderr)
        assert "Could not continue" not in result.stderr, (mode, result.stderr)
        print("PASS", mode, "opens exactly once on main thread" if opens else "does not open a world")
        if opens: assert result.stdout.count("OPEN=") == 1
print("10 real JVM continuation checks passed")
