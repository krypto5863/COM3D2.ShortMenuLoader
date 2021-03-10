using BepInEx;
using HarmonyLib;
using MaidStatus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShortMenuLoader
{
	[BepInPlugin("ShortMenuLoader", "ShortMenuLoader", "1.0.0.0")]
	internal class Main : BaseUnityPlugin
	{
		private Harmony harmony;
		private static Main this2;
		public static SceneEdit @this;

		public static float time;
		public static int ThreadsDone = 0;

		public static Stopwatch WatchOverall = new Stopwatch();

		private void Awake()
		{
			//We set our patcher so we can call it back and patch dynamically as needed.
			harmony = Harmony.CreateAndPatchAll(typeof(Main));

			MethodBase menuItemSetConstructor = typeof(SceneEdit)
			.GetNestedType("MenuItemSet", BindingFlags.NonPublic)
			.GetConstructor(
			new Type[]
			{ typeof(GameObject),
			typeof(SceneEdit.SMenuItem),
			typeof(string),
			typeof(bool)
			});

			MethodBase colorItemSetConstructor = typeof(SceneEdit).GetNestedType("ColorItemSet", BindingFlags.NonPublic).GetConstructor(new Type[] { typeof(GameObject), typeof(SceneEdit.SMenuItem) });

			harmony.PatchAll(typeof(QuickEdit));

			harmony.Patch(menuItemSetConstructor, new HarmonyMethod(typeof(QuickEdit), "MenuItemSet"));
			harmony.Patch(colorItemSetConstructor, new HarmonyMethod(typeof(QuickEdit), "MenuItemSet"));

			GSModMenuLoad.LoadCache();

			@this2 = this;
		}
		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyPrefix]
		private static void GetInstance(ref SceneEdit __instance)
		{
			WatchOverall.Reset();

			WatchOverall.Start();
			@this = __instance;
		}
		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> Transpiler1(IEnumerable<CodeInstruction> instructions)
		{
			IEnumerable<CodeInstruction> custominstruc = new CodeMatcher(instructions)
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
			Transpilers.EmitDelegate<Action>(() =>
			{

				Debug.Log("Calling your modified CoRoutine");
				@this2.StartCoroutine(InitMenuNative());

			}),
			new CodeInstruction(OpCodes.Pop)
			)
			.InstructionEnumeration();

			return custominstruc;
		}
		private static IEnumerator InitMenuNative()
		{
			List<SceneEdit.SMenuItem> menuList = new List<SceneEdit.SMenuItem>();
			Dictionary<int, List<int>> menuGroupMemberDic = new Dictionary<int, List<int>>();

			@this.m_menuRidDic = new Dictionary<int, SceneEdit.SMenuItem>();

			//We set time so the coroutines to be called can coordinate themselves.
			time = Time.realtimeSinceStartup;

			AccessTools.Method(typeof(SceneEdit), "InitCategoryList").Invoke(Main.@this, null);

			Debug.Log($"Files began loading at: {WatchOverall.Elapsed}");

			//These coroutines hold the loading code for each type of item related file.
			this2.StartCoroutine(GSModMenuLoad.GSMenuLoadStart(menuList, menuGroupMemberDic));
			this2.StartCoroutine(ModMenuLoad.ModMenuLoadStart(menuList, menuGroupMemberDic));
			this2.StartCoroutine(VanillaMenuLoad.VanillaMenuLoadStart(menuList, menuGroupMemberDic));

			//In a sorta async fashion, while the threads are no complete, the coroutine will pass on processing to someone else.
			while (ThreadsDone != 3)
			{
				yield return null;
			}

			Debug.Log($"All loaders finished at: {WatchOverall.Elapsed}.");
			//Setting threads back to 0 for next loads.
			ThreadsDone = 0;

			//Calls the final function to complete setting up menu items.
			yield return @this.StartCoroutine(Main.FixedInitMenu(menuList, @this.m_menuRidDic, menuGroupMemberDic));

			//Does something...
			yield return @this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "CoLoadWait").Invoke(@this, null) as IEnumerator);

			Debug.Log($"Loading completely done at: {WatchOverall.Elapsed}.");
		}
		private static IEnumerator FixedInitMenu(List<SceneEdit.SMenuItem> menuList, Dictionary<int, SceneEdit.SMenuItem> menuRidDic, Dictionary<int, List<int>> menuGroupMemberDic)
		{

			float time = Time.realtimeSinceStartup;

			List<SceneEdit.SCategory> listCategory = (List<SceneEdit.SCategory>)AccessTools.Field(typeof(SceneEdit), "m_listCategory").GetValue(@this);

			foreach (KeyValuePair<int, List<int>> keyValuePair in menuGroupMemberDic)
			{
				if (menuRidDic.ContainsKey(keyValuePair.Key) && keyValuePair.Value.Count >= 1)
				{
					SceneEdit.SMenuItem smenuItem = menuRidDic[keyValuePair.Key];
					smenuItem.m_bGroupLeader = true;
					smenuItem.m_listMember = new List<SceneEdit.SMenuItem>();
					smenuItem.m_listMember.Add(smenuItem);
					for (int n = 0; n < keyValuePair.Value.Count; n++)
					{
						smenuItem.m_listMember.Add(menuRidDic[keyValuePair.Value[n]]);
						smenuItem.m_listMember[smenuItem.m_listMember.Count - 1].m_bMember = true;
						smenuItem.m_listMember[smenuItem.m_listMember.Count - 1].m_leaderMenu = smenuItem;
					}
					smenuItem.m_listMember.Sort(delegate (SceneEdit.SMenuItem x, SceneEdit.SMenuItem y)
									{
										if (x.m_fPriority == y.m_fPriority)
										{
											return 0;
										}
										if (x.m_fPriority < y.m_fPriority)
										{
											return -1;
										}
										if (x.m_fPriority > y.m_fPriority)
										{
											return 1;
										}
										return 0;
									});
					smenuItem.m_listMember.Sort((SceneEdit.SMenuItem x, SceneEdit.SMenuItem y) => x.m_strMenuFileName.CompareTo(y.m_strMenuFileName));
				}
			}

			foreach (KeyValuePair<MPN, SceneEditInfo.CCateNameType> keyValuePair2 in SceneEditInfo.m_dicPartsTypePair)
			{
				if (keyValuePair2.Value.m_eType == SceneEditInfo.CCateNameType.EType.Slider)
				{
					AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { new SceneEdit.SMenuItem
					{
						m_mpn = keyValuePair2.Key,
						m_nSliderValue = 500,
						m_strCateName = keyValuePair2.Key.ToString(),
						m_strMenuName = keyValuePair2.Value.m_strBtnPartsTypeName,
						m_requestNewFace = keyValuePair2.Value.m_requestNewFace,
						m_requestFBFace = keyValuePair2.Value.m_requestFBFace
					}});
					/*
					this.AddMenuItemToList(new SceneEdit.SMenuItem
					{
						m_mpn = keyValuePair2.Key,
						m_nSliderValue = 500,
						m_strCateName = keyValuePair2.Key.ToString(),
						m_strMenuName = keyValuePair2.Value.m_strBtnPartsTypeName,
						m_requestNewFace = keyValuePair2.Value.m_requestNewFace,
						m_requestFBFace = keyValuePair2.Value.m_requestFBFace
					});*/
				}
			}

			for (int nM = 0; nM < menuList.Count; nM++)
			{
				SceneEdit.SMenuItem mi = menuList[nM];
				if (SceneEditInfo.m_dicPartsTypePair.ContainsKey(mi.m_eColorSetMPN))
				{
					if (mi.m_eColorSetMPN != MPN.null_mpn)
					{
						if (mi.m_strMenuNameInColorSet != null)
						{
							mi.m_strMenuNameInColorSet = mi.m_strMenuNameInColorSet.Replace("*", ".*");
							mi.m_listColorSet = @this.m_dicColor[mi.m_eColorSetMPN].FindAll((SceneEdit.SMenuItem i) => new Regex(mi.m_strMenuNameInColorSet).IsMatch(i.m_strMenuFileName));
						}
						else
						{
							mi.m_listColorSet = @this.m_dicColor[mi.m_eColorSetMPN];
						}
					}
					if (0.5f < Time.realtimeSinceStartup - time)
					{
						yield return null;
						time = Time.realtimeSinceStartup;
					}
				}
			}

			for (int j = 0; j < listCategory.Count; j++)
			{
				listCategory[j].SortPartsType();
			}
			for (int k = 0; k < listCategory.Count; k++)
			{
				listCategory[k].SortItem();
			}
			foreach (SceneEdit.SCategory scategory in listCategory)
			{
				if (scategory.m_eCategory == SceneEditInfo.EMenuCategory.プリセット || scategory.m_eCategory == SceneEditInfo.EMenuCategory.ランダム || scategory.m_eCategory == SceneEditInfo.EMenuCategory.プロフィ\u30FCル || scategory.m_eCategory == SceneEditInfo.EMenuCategory.着衣設定)
				{
					scategory.m_isEnabled = true;
				}
				else
				{
					scategory.m_isEnabled = false;
					foreach (SceneEdit.SPartsType spartsType in scategory.m_listPartsType)
					{
						if (spartsType.m_isEnabled)
						{
							scategory.m_isEnabled = true;
							break;
						}
					}
				}
			}

			if (@this.modeType == SceneEdit.ModeType.CostumeEdit)
			{
				SceneEditInfo.EMenuCategory[] array = new SceneEditInfo.EMenuCategory[]
				{
				SceneEditInfo.EMenuCategory.セット,
				SceneEditInfo.EMenuCategory.プリセット,
				SceneEditInfo.EMenuCategory.ランダム,
				SceneEditInfo.EMenuCategory.プロフィ\u30FCル
				};
				SceneEditInfo.EMenuCategory[] array2 = array;
				for (int l = 0; l < array2.Length; l++)
				{
					SceneEditInfo.EMenuCategory cate = array2[l];
					listCategory.Find((SceneEdit.SCategory c) => c.m_eCategory == cate).m_isEnabled = false;
				}
			}
			else if (@this.maid.status.heroineType == HeroineType.Sub || @this.maid.boNPC)
			{
				SceneEditInfo.EMenuCategory[] array3 = new SceneEditInfo.EMenuCategory[]
				{
				SceneEditInfo.EMenuCategory.プロフィ\u30FCル
				};
				SceneEditInfo.EMenuCategory[] array4 = array3;
				for (int m = 0; m < array4.Length; m++)
				{
					SceneEditInfo.EMenuCategory cate = array4[m];
					listCategory.Find((SceneEdit.SCategory c) => c.m_eCategory == cate).m_isEnabled = false;
				}
			}


			//@this.UpdatePanel_Category();
			AccessTools.Method(typeof(SceneEdit), "UpdatePanel_Category").Invoke(@this, null);
			yield break;
		}
	}
}