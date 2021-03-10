# COM3D2.ShortMenuLoader
A plugin dedicated to fixing Kiss's horrendous code and making edit mode loading as fast as it can be.

As the name suggests, this plugin will make your load into Edit mode a lot quicker. You will see large buffs to your loading speed if you have large mod folders. Players with little to no mods (inside of their Mod folder) may not notice any differences.

In testing, this plugin has managed to cut the load times for some users by as much as 80-95%. Yes, it works.

## Highlights
- Self-correcting smart cache: Caches mods intelligently and uses it when it is available. Corrects itself when a mod is modified, deleted or added. This allows users with weaker computers to still attain fast speeds with no overhead.
- Multi-threading: Uses threads to do several load tasks in order to finish faster while causing your game no lag. Combined with Coroutines, it creates a faux async-await.
- Optimized code: Code was optimized to hell and back to ensure that it was as fast as could be without losing functionality.
- When-needed Icon Loads: QuickEditStart was integrated into ShortMenuLoader. Instead of loading every single icon during loading, icons are loaded when you open a category and only the icons for that category are loaded.
