# COM3D2.ShortMenuLoader
A plugin dedicated to fixing Kiss's horrendous code and making edit mode loading as fast as it can be.

As the name suggests, this plugin will make your load into Edit mode a lot quicker. You will see large buffs to your loading speed if you have large mod folders. Players with little to no mods (inside of their Mod folder), OR players who already utilized CacheEditMenu and QuickEditStart may not notice any large differences.

In testing, this plugin has managed to cut the load times for some users by as much as 80-95% FROM VANILLA. Yes, it works.

I want to clarify that users who already have QuickEditStart and/or CacheEditMenu may not notice a huge improvement. But there should still be some improvement(usually a few seconds) and SML offers tweaks and a smart cache you won't get with those two plugins. All text here is written within the context of vanilla speeds.

## Highlights
- Self-correcting smart cache: Caches mods intelligently and uses it when it is available. Corrects itself when a mod is modified, deleted or added. This allows users with weaker computers to still attain fast speeds with no overhead.
- Multi-threading: Uses threads to do several load tasks in order to finish faster while causing your game no lag. Combined with Coroutines, it creates a faux async-await.
- Optimized code: Code was optimized to hell and back to ensure that it was as fast as could be without losing functionality.
- When-needed Icon Loads: QuickEditStart was integrated into ShortMenuLoader. Instead of loading every single icon during loading, icons are loaded when you open a category and only the icons for that category are loaded.
- BepinEx only.

## Cons
Changes this big bring some downsides. Luckily, this one has few.
- ~~Errors may hide: Some errors with menu files will not show anymore on your console. This is due to the multi-threading. However, they may still be available to see. You do need to locate and view your `output_log.txt` to do so though.~~ Corrected sorta. Logging now works properly.
- Compatibility: Changes this large have some compatibility issues with other plugins of a similar nature or that hook into modified functions. Luckily not many plugins do.

## Usage
This plugin is as simple as drop in and play. From the releases section, drop the two DLLs into `BepinEx/plugins`.

The following plugins can and should be removed as they are no longer utilized if SML is installed:
- QuickEditStart
- CacheEditMenu

The first time you enter edit mode will be when a cache is actually built so it will be slower but even then, you may notice an extreme speed difference.

Cache is saved to `BepinEx/config`, it can be deleted if you find it has become corrupt or is presenting an issue, which would be odd and rare.

Vanilla menus cache is available but optional as it cannot detect when a menu file has changed, it can only detect a difference in `paths.dat` file timestamps. It is disabled by default in the F1 menu (ConfigurationManager) and in the config file.

An optional feature was recently added that gave mods +10,000 to priority. This forces all mod icons to appear way after vanilla items, giving you a slightly more organized view.

Duplicates are reported in your console at load time. These are considered bad practice and can slow down load times and cause confusion and bugs. These should be corrected when possible.

Your times may not always be 100% consistent. For one, this will not make vanilla loading THAT much faster. Even with vanilla cache, we still have to wait for an unmodified database to be loaded to even begin loading the vanilla menus. This currently accounts for 70-90% of the delay in most if not all cases. Nothing can be done without some drastic measures. Said database begins loading as soon as you enter the main menu. As a result, your times can appear to fluctuate wildly, but that's because the database may have loaded earlier or later than you expected. To check this, you can refer to the times posted in your console after load and look for the amount of time spent waiting for the unmodified database to finish.
