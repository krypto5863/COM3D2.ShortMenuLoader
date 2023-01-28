using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MaidStatus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using UnityEngine;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace ShortMenuLoader
{
	[BepInPlugin("ShortMenuLoader", "ShortMenuLoader", "1.5.8")]
	[BepInDependency("ShortMenuVanillaDatabase", BepInDependency.DependencyFlags.SoftDependency)]
	internal class Main : BaseUnityPlugin
	{
		private Harmony _harmony;
		public static Main PlugInstance;
		public static SceneEdit SceneEditInstance;

		public static float Time;
		public static int ThreadsDone;
		internal static string[] FilesInModFolder;

		public static BepInEx.Logging.ManualLogSource PLogger;

		public static Stopwatch WatchOverall = new Stopwatch();
		public static ConfigEntry<float> BreakInterval;
		public static ConfigEntry<int> TimeoutLimit;
		public static ConfigEntry<bool> UseVanillaCache;
		public static ConfigEntry<bool> ChangeModPriority;
		public static ConfigEntry<bool> PutMenuFileNameInItemDescription;
		public static ConfigEntry<bool> UseIconPreloader;

		internal static bool SmvdLoaded;

		private void Awake()
		{
			PLogger = Logger;

			PLogger.LogDebug("Starting SML awake now...");

			PlugInstance = this;

			//We set our patcher so we can call it back and patch dynamically as needed.
			_harmony = Harmony.CreateAndPatchAll(typeof(Main));

			MethodBase menuItemSetConstructor = typeof(SceneEdit)
			.GetNestedType("MenuItemSet", BindingFlags.NonPublic)
			.GetConstructor(
			new[]
			{ typeof(GameObject),
			typeof(SceneEdit.SMenuItem),
			typeof(string),
			typeof(bool)
			});

			if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("ShortMenuVanillaDatabase"))
			{
				PLogger.LogDebug("SMVD is loaded! Optimizing for SMVD!");

				SmvdLoaded = true;
			}
			else
			{
				PLogger.LogWarning("SMVD is not loaded! Consider installing ShortMenuVanillaDatabase for even better performance!");
			}

			MethodBase colorItemSetConstructor = typeof(SceneEdit).GetNestedType("ColorItemSet", BindingFlags.NonPublic).GetConstructor(new[] { typeof(GameObject), typeof(SceneEdit.SMenuItem) });

			_harmony.PatchAll(typeof(QuickEdit));
			_harmony.PatchAll(typeof(QuickEditVanilla));

			_harmony.Patch(menuItemSetConstructor, new HarmonyMethod(typeof(QuickEdit), "MenuItemSet"));
			_harmony.Patch(colorItemSetConstructor, new HarmonyMethod(typeof(QuickEdit), "MenuItemSet"));

			PlugInstance.StartCoroutine(GsModMenuLoad.LoadCache());

			BreakInterval = Config.Bind("General", "Time Between Breaks in Seconds", 0.5f, "The break interval is the time between each break that the co-routine takes where it returns processing back to the main thread. After one frame, processing is given back to the co-routine. Higher values can help with low-end processing times but can cause instability if set too high. If after all of this you're still confused, leave it alone.");

			TimeoutLimit = Config.Bind("General", "Mutex Timeout Limit", 50000, "The time in milliseconds to wait for a mutex to unlock before declaring it stalled and restarting the work task. Raise this if you're getting erroneous timed out waiting for mutex errors. Higher values are perfectly safe but you'll be waiting around longer if an error really does occur and the mutex never unlocks. Values below the default are not recommended, this can and will cause errors.");

			if (!SmvdLoaded)
			{
				UseVanillaCache = Config.Bind("General", "Use Vanilla Cache", false, "SceneEditInstance decides whether a vanilla cache is created, maintained and used on load. Kiss has it's own questionable implementation of a cache, but this cache is questionable in it's own right too. Disabled when you use SMVD.");
			}

			ChangeModPriority = Config.Bind("General", "Add 10,000 to Mod Item Priority", false, "SceneEditInstance option simply adds 10,000 priority to all mod items loaded. Handy if you don't want mod items mix and matching with vanilla stuff or appearing before the remove button.");

			PutMenuFileNameInItemDescription = Config.Bind("General", "Append Menu file Names to Descriptions", false, "SceneEditInstance option appends menu file names to the descriptions of the items. Useful for modders or for users looking to take out certain mods. Will not work if activated when already in edit mode.");

			UseIconPreloader = Config.Bind("General", "Whether to use the IconPreloader for mod files.", true, "In some users with weaker computers, this can cause massive slowdowns or ram over-usage. For these users, it might be more desirable to leave this off.");

			PlugInstance.StartCoroutine(VanillaMenuLoad.LoadCache());
		}

		//Slightly out of scope but it serves to accomadate placing menu paths in descriptions. Silly kiss.
		[HarmonyPatch(typeof(ItemInfoWnd), "Open")]
		[HarmonyPostfix]
		private static void GetInstance(ref ItemInfoWnd __instance)
		{
			if (!PutMenuFileNameInItemDescription.Value)
			{
				return;
			}

			__instance.m_uiInfo.overflowMethod = UILabel.Overflow.ResizeHeight;

			__instance.m_uiInfo.Wrap(__instance.m_uiInfo.text, out var wrapped);

			__instance.m_uiInfo.text = wrapped;
		}

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyPrefix]
		private static void GetInstance(ref SceneEdit __instance)
		{
			WatchOverall.Reset();

			WatchOverall.Start();
			SceneEditInstance = __instance;
		}

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> Transpiler1(IEnumerable<CodeInstruction> instructions)
		{
			var customInstruction = new CodeMatcher(instructions)
			.MatchForward(false,
			new CodeMatch(OpCodes.Ldarg_0),
			new CodeMatch(OpCodes.Ldarg_0),
			new CodeMatch(l => l.opcode == OpCodes.Call && l.Calls(AccessTools.Method(typeof(SceneEdit), "InitMenuNative"))))
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.Insert(
			new CodeInstruction(OpCodes.Ldarg_0),
			new CodeInstruction(OpCodes.Ldarg_0),
			Transpilers.EmitDelegate<Action<SceneEdit>>(sceneEdit =>
			{
				SceneEditInstance = sceneEdit;

				PLogger.LogDebug("Calling your modified CoRoutine");
				SceneEditInstance.StartCoroutine(InitMenuNative());
			}),
			new CodeInstruction(OpCodes.Pop)
			)
			.InstructionEnumeration();

			return customInstruction;
		}

		private static IEnumerator InitMenuNative()
		{
			var menuList = new List<SceneEdit.SMenuItem>();
			var menuGroupMemberDic = new Dictionary<int, List<int>>();

			SceneEditInstance.m_menuRidDic = new Dictionary<int, SceneEdit.SMenuItem>();

			//We set time so the co-routines to be called can coordinate themselves.
			Time = UnityEngine.Time.realtimeSinceStartup;

			//Setting threads back to 0 in-case last load had a higher number.
			ThreadsDone = 0;
			//Calling it directly after threads are refixed. It'll load the directory in the background while other things load.
			//Temporarily disabled. It's a wittle buggy.

			FilesInModFolder = Directory.GetFiles(Paths.GameRootPath + "\\Mod", "*.*", SearchOption.AllDirectories);

			if (UseIconPreloader.Value)
			{
				QuickEdit.EngageModPreLoader();
			}

			SceneEditInstance.InitCategoryList();

			PLogger.LogInfo($"Files began loading at: {WatchOverall.Elapsed}");

			//These coroutines hold the loading code for each type of item related file.
			PlugInstance.StartCoroutine(GsModMenuLoad.GsMenuLoadStart(menuList, menuGroupMemberDic));
			PlugInstance.StartCoroutine(ModMenuLoad.ModMenuLoadStart(menuList, menuGroupMemberDic));
			PlugInstance.StartCoroutine(VanillaMenuLoad.VanillaMenuLoadStart(menuList, menuGroupMemberDic));

			//In a sorta async fashion, while the threads are no complete, the coroutine will pass on processing to someone else.
			while (ThreadsDone != 3)
			{
				yield return null;
			}

			PLogger.LogInfo($"All loaders finished at: {WatchOverall.Elapsed}.");

			//Calls the final function to complete setting up menu items.
			yield return SceneEditInstance.StartCoroutine(FixedInitMenu(menuList, SceneEditInstance.m_menuRidDic, menuGroupMemberDic));

			//Does something...
			yield return SceneEditInstance.StartCoroutine(SceneEditInstance.CoLoadWait());

			PLogger.LogInfo($"Loading completely done at: {WatchOverall.Elapsed}.");
		}

		private static IEnumerator FixedInitMenu(List<SceneEdit.SMenuItem> menuList, IDictionary<int, SceneEdit.SMenuItem> menuRidDic, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			var watch = Stopwatch.StartNew();

			var time = UnityEngine.Time.realtimeSinceStartup;

			var listCategory = SceneEditInstance.m_listCategory;

			foreach (var keyValuePair in menuGroupMemberDic)
			{
				if (menuRidDic.ContainsKey(keyValuePair.Key) && keyValuePair.Value.Count >= 1 && keyValuePair.Value != null)
				{
					var sMenuItem = menuRidDic[keyValuePair.Key];
					sMenuItem.m_bGroupLeader = true;
					sMenuItem.m_listMember = new List<SceneEdit.SMenuItem> { sMenuItem };

					foreach (var t in keyValuePair.Value)
					{
						sMenuItem.m_listMember.Add(menuRidDic[t]);
						sMenuItem.m_listMember[sMenuItem.m_listMember.Count - 1].m_bMember = true;
						sMenuItem.m_listMember[sMenuItem.m_listMember.Count - 1].m_leaderMenu = sMenuItem;
					}

					sMenuItem.m_listMember.Sort((x, y) => x.m_fPriority == y.m_fPriority ? 0 :
						x.m_fPriority < y.m_fPriority ? -1 :
						x.m_fPriority > y.m_fPriority ? 1 : 0);

					sMenuItem.m_listMember.Sort((x, y) => string.Compare(x.m_strMenuFileName, y.m_strMenuFileName, StringComparison.Ordinal));
				}
				else if (keyValuePair.Value == null)
				{
					PLogger.LogError("A key value in menuGroupMemberDic was nulled. SceneEditInstance value was skipped for processing as a result.");
				}
			}

			foreach (var keyValuePair2 in SceneEditInfo.m_dicPartsTypePair)
			{
				if (keyValuePair2.Value.m_eType == SceneEditInfo.CCateNameType.EType.Slider)
				{
					/*
					AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { new SceneEdit.SMenuItem
					{
						m_mpn = keyValuePair2.Key,
						m_nSliderValue = 500,
						m_strCateName = keyValuePair2.Key.ToString(),
						m_strMenuName = keyValuePair2.Value.m_strBtnPartsTypeName,
						m_requestNewFace = keyValuePair2.Value.m_requestNewFace,
						m_requestFBFace = keyValuePair2.Value.m_requestFBFace
					}});
					*/
					SceneEditInstance.AddMenuItemToList(new SceneEdit.SMenuItem
					{
						m_mpn = keyValuePair2.Key,
						m_nSliderValue = 500,
						m_strCateName = keyValuePair2.Key.ToString(),
						m_strMenuName = keyValuePair2.Value.m_strBtnPartsTypeName,
						m_requestNewFace = keyValuePair2.Value.m_requestNewFace,
						m_requestFBFace = keyValuePair2.Value.m_requestFBFace
					});
				}
			}

			//for (int nM = 0; nM < menuList.Count; nM++)
			foreach (var mi in menuList)
			{
				//SceneEdit.SMenuItem mi = menuList[nM];
				if (!SceneEditInfo.m_dicPartsTypePair.ContainsKey(mi.m_eColorSetMPN))
				{
					continue;
				}

				if (mi.m_eColorSetMPN != MPN.null_mpn)
				{
					if (mi.m_strMenuNameInColorSet != null)
					{
						mi.m_strMenuNameInColorSet = mi.m_strMenuNameInColorSet.Replace("*", ".*");
						mi.m_listColorSet = SceneEditInstance.m_dicColor[mi.m_eColorSetMPN].FindAll(i => new Regex(mi.m_strMenuNameInColorSet).IsMatch(i.m_strMenuFileName));
					}
					else
					{
						mi.m_listColorSet = SceneEditInstance.m_dicColor[mi.m_eColorSetMPN];
					}
				}
				if (0.5f < UnityEngine.Time.realtimeSinceStartup - time)
				{
					yield return null;
					time = UnityEngine.Time.realtimeSinceStartup;
				}
			}

			/*I don't really get why they're running three different loops to process the exact same lists? But we're gonna merge that in to save some looping time.
			//for (int j = 0; j < listCategory.Count; j++)
			foreach(SceneEdit.SCategory category in listCategory)
			{
				category.SortPartsType();
			}

			//for (int k = 0; k < listCategory.Count; k++)
			foreach (SceneEdit.SCategory category in listCategory)
			{
				category.SortItem();
			}
			*/

			foreach (var sCategory in listCategory)
			{
				sCategory.SortPartsType();
				sCategory.SortItem();

				if (sCategory.m_eCategory == SceneEditInfo.EMenuCategory.プリセット || sCategory.m_eCategory == SceneEditInfo.EMenuCategory.ランダム || sCategory.m_eCategory == SceneEditInfo.EMenuCategory.プロフィ\u30FCル || sCategory.m_eCategory == SceneEditInfo.EMenuCategory.着衣設定)
				{
					sCategory.m_isEnabled = true;
				}
				else
				{
					sCategory.m_isEnabled = false;
					foreach (var sPartsType in sCategory.m_listPartsType)
					{
						if (!sPartsType.m_isEnabled)
						{
							continue;
						}

						sCategory.m_isEnabled = true;
						break;
					}
				}
			}

			if (SceneEditInstance.modeType == SceneEdit.ModeType.CostumeEdit)
			{
				var array = new[]
				{
				SceneEditInfo.EMenuCategory.セット,
				SceneEditInfo.EMenuCategory.プリセット,
				SceneEditInfo.EMenuCategory.ランダム,
				SceneEditInfo.EMenuCategory.プロフィ\u30FCル
				};
				foreach (var category in array)
				{
					var category1 = category;
					listCategory.Find(c => c.m_eCategory == category1).m_isEnabled = false;
				}
			}
			else if (SceneEditInstance.maid.status.heroineType == HeroineType.Sub || SceneEditInstance.maid.boNPC)
			{
				var array3 = new[]
				{
				SceneEditInfo.EMenuCategory.プロフィ\u30FCル
				};
				foreach (var category in array3)
				{
					var category1 = category;
					listCategory.Find(c => c.m_eCategory == category1).m_isEnabled = false;
				}
			}

			SceneEditInstance.UpdatePanel_Category();

			PLogger.LogInfo($"FixedInitMenu done in {watch.Elapsed}");

			//AccessTools.Method(typeof(SceneEdit), "UpdatePanel_Category").Invoke(@this, null);
		}
	}
}