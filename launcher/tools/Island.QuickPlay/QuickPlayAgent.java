package island.quickplay;

import java.io.File;
import java.lang.instrument.Instrumentation;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.concurrent.Future;

/** Original ISLAND helper: request an existing world once the client reaches its main menu. */
public final class QuickPlayAgent {
    public static void premain(String argument, final Instrumentation instrumentation) {
        try {
            final File saves = new File("saves").getCanonicalFile();
            final File world = new File(new String(Base64.getUrlDecoder().decode(argument), StandardCharsets.UTF_8)).getCanonicalFile();
            if (!saves.equals(world.getParentFile()) || !new File(world, "level.dat").isFile()) return;
            Thread watcher = new Thread(() -> watch(instrumentation, world), "ISLAND world continuation");
            watcher.setDaemon(true);
            watcher.start();
        } catch (Exception error) { report(error); }
    }
    private static void watch(Instrumentation instrumentation, File world) {
        try {
            // Large legacy modpacks can take many minutes to initialize. Never keep the JVM alive.
            long deadline = System.nanoTime() + 15L * 60 * 1000000000;
            Class<?> clientType = null;
            while (System.nanoTime() < deadline) {
                if (clientType == null) {
                    for (Class<?> candidate : instrumentation.getAllLoadedClasses()) {
                        String name = candidate.getName();
                        if (name.equals("net.minecraft.client.Minecraft") || name.equals("bao") || name.equals("bib")) {
                            clientType = candidate; break;
                        }
                    }
                }
                if (clientType != null) {
                    Object client = singleton(clientType);
                    if (client != null && ready(client)) {
                        final Object target = client;
                        Method scheduler = method(clientType, new String[]{"addScheduledTask", "func_152344_a", "a"}, Runnable.class);
                        if (scheduler == null || !Future.class.isAssignableFrom(scheduler.getReturnType())) return;
                        Future<?> result = (Future<?>) scheduler.invoke(client, (Runnable) () -> {
                            try {
                                if (!ready(target) || !new File(world, "level.dat").isFile()) return;
                                for (Method method : target.getClass().getMethods()) {
                                    Class<?>[] types = method.getParameterTypes();
                                    String name = method.getName();
                                    if ((name.equals("launchIntegratedServer") || name.equals("func_71371_a") || name.equals("a"))
                                            && types.length == 3 && types[0] == String.class && types[1] == String.class
                                            && !types[2].isPrimitive() && method.getReturnType() == Void.TYPE) {
                                        method.invoke(target, world.getName(), world.getName(), null);
                                        System.out.println("[ISLAND] Continuing saved world: " + world.getName());
                                        return;
                                    }
                                }
                            } catch (Exception error) { report(error); }
                        });
                        // No further attempts: changing screens while waiting cancels automatic entry.
                        result.get();
                        return;
                    }
                }
                Thread.sleep(500);
            }
        } catch (Exception error) { report(error); }
    }
    private static Object singleton(Class<?> type) throws Exception {
        for (Field field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == type) {
                field.setAccessible(true);
                Object value = field.get(null);
                if (value != null) return value;
            }
        }
        return null;
    }
    private static boolean ready(Object client) throws Exception {
        Class<?> type = client.getClass();
        if (field(type, "theWorld", "world", "field_71441_e", "f").get(client) != null) return false;
        Object screen = (type.getName().equals("bib") ? field(type, "m") : field(type, "currentScreen", "field_71462_r", "n")).get(client);
        if (screen == null) return false;
        for (Class<?> screenType = screen.getClass(); screenType != null; screenType = screenType.getSuperclass()) {
            String name = screenType.getName();
            if (name.equals("net.minecraft.client.gui.GuiMainMenu") || name.equals("bee") || name.equals("blr")) return true;
            if (name.equals("lumien.custommainmenu.gui.GuiCustom")) {
                Object config = field(screenType, "guiConfig").get(screen);
                return config != null && "mainmenu".equals(field(config.getClass(), "name").get(config));
            }
        }
        return false;
    }
    private static Field field(Class<?> type, String... names) throws NoSuchFieldException {
        for (String name : names) {
            try { Field field = type.getDeclaredField(name); field.setAccessible(true); return field; }
            catch (NoSuchFieldException ignored) { }
        }
        throw new NoSuchFieldException(type.getName());
    }
    private static Method method(Class<?> type, String[] names, Class<?> parameter) {
        for (String name : names) {
            try { return type.getMethod(name, parameter); }
            catch (NoSuchMethodException ignored) { }
        }
        return null;
    }
    private static void report(Exception error) {
        System.err.println("[ISLAND] Could not continue saved world; use the game menu. " + error.getClass().getSimpleName());
    }
}
