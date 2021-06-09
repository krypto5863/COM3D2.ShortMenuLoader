using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Experiment5ParallelMenuLoading
{
	[BepInPlugin("Experiment5ParallelMenuLoading", "Experiment5ParallelMenuLoading", "1.0.0.0")]
	class ControlClass : BaseUnityPlugin
	{
		private object harmony;
		private static ControlClass this2;
		private static SceneEdit @this;

		void Awake()
		{
			//We set our patcher so we can call it back and patch dynamically as needed.
			harmony = Harmony.CreateAndPatchAll(typeof(ControlClass));
			@this2 = this;
		}

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyPrefix]
		static void GetInstance(ref SceneEdit __instance)
		{
			@this = __instance;
		}

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler1(IEnumerable<CodeInstruction> instructions)
		{
			var custominstruc = new CodeMatcher(instructions)
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

				Main.logger.LogDebug("Calling your control test coroutine.");
				@this2.StartCoroutine(InitMenuNative());

			}),
			new CodeInstruction(OpCodes.Pop)
			)
			.InstructionEnumeration();

			return custominstruc;
		}
		private static IEnumerator InitMenuNative()
		{
			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
			}
			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;
			while (!menuDataBase.JobFinished())
			{
				yield return null;
			}

			Stopwatch watch1 = new Stopwatch();

			Main.logger.LogDebug("Vanilla menu file load has now begun.");

			watch1.Start();

			AccessTools.Method(typeof(SceneEdit), "InitCategoryList").Invoke(@this, null);
			int fileCount = menuDataBase.GetDataSize();
			List<SceneEdit.SMenuItem> menuList = new List<SceneEdit.SMenuItem>(fileCount);
			@this.m_menuRidDic = new Dictionary<int, SceneEdit.SMenuItem>(fileCount);
			Dictionary<int, List<int>> menuGroupMemberDic = new Dictionary<int, List<int>>();
			float time = Time.realtimeSinceStartup;
			for (int i = 0; i < fileCount; i++)
			{
				menuDataBase.SetIndex(i);
				string fileName = menuDataBase.GetMenuFileName();
				string parent_filename = menuDataBase.GetParentMenuFileName();
				if (GameMain.Instance.CharacterMgr.status.IsHavePartsItem(fileName))
				{
					SceneEdit.SMenuItem mi = new SceneEdit.SMenuItem();
					mi.m_strMenuFileName = fileName;
					mi.m_nMenuFileRID = fileName.GetHashCode();
					try
					{
						SceneEdit.ReadMenuItemDataFromNative(mi, i);
					}
					catch (Exception ex)
					{
						Main.logger.LogError(string.Concat(new string[]
						{
							"ReadMenuItemDataFromNative (例外) tossed an exception while reading: ",
							fileName,
							"\n",
							ex.Message,
							"\n",
							ex.StackTrace
						}));
					}
					if (!mi.m_bMan && @this.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
					{
						AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { mi });
						menuList.Add(mi);
						if (!@this.m_menuRidDic.ContainsKey(mi.m_nMenuFileRID))
						{
							@this.m_menuRidDic.Add(mi.m_nMenuFileRID, mi);
						}
						string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi }) as string;
						if (!string.IsNullOrEmpty(parentMenuName))
						{
							int hashCode = parentMenuName.GetHashCode();
							if (!menuGroupMemberDic.ContainsKey(hashCode))
							{
								menuGroupMemberDic.Add(hashCode, new List<int>());
							}
							menuGroupMemberDic[hashCode].Add(mi.m_strMenuFileName.ToLower().GetHashCode());
						}
						else if (mi.m_strCateName.IndexOf("set_") != -1 && mi.m_strMenuFileName.IndexOf("_del") == -1)
						{
							mi.m_bGroupLeader = true;
							mi.m_listMember = new List<SceneEdit.SMenuItem>();
							mi.m_listMember.Add(mi);
						}
						if (0.5f < Time.realtimeSinceStartup - time)
						{
							yield return null;
							time = Time.realtimeSinceStartup;
						}
					}
				}
			}
			foreach (string strFileName in GameUty.ModOnlysMenuFiles)
			{
				SceneEdit.SMenuItem mi2 = new SceneEdit.SMenuItem();
				if (SceneEdit.GetMenuItemSetUP(mi2, strFileName, false))
				{
					if (!mi2.m_bMan && !(mi2.m_texIconRef == null))
					{
						AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { mi2 });
						menuList.Add(mi2);
						if (!@this.m_menuRidDic.ContainsKey(mi2.m_nMenuFileRID))
						{
							@this.m_menuRidDic.Add(mi2.m_nMenuFileRID, mi2);
						}
						string parentMenuName2 = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi2 }) as string;
						if (!string.IsNullOrEmpty(parentMenuName2))
						{
							int hashCode2 = parentMenuName2.GetHashCode();
							if (!menuGroupMemberDic.ContainsKey(hashCode2))
							{
								menuGroupMemberDic.Add(hashCode2, new List<int>());
							}
							menuGroupMemberDic[hashCode2].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
						}
						else if (mi2.m_strCateName.IndexOf("set_") != -1 && mi2.m_strMenuFileName.IndexOf("_del") == -1)
						{
							mi2.m_bGroupLeader = true;
							mi2.m_listMember = new List<SceneEdit.SMenuItem>();
							mi2.m_listMember.Add(mi2);
						}
						if (0.5f < Time.realtimeSinceStartup - time)
						{
							yield return null;
							time = Time.realtimeSinceStartup;
						}
					}
				}
			}

			yield return @this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "FixedInitMenu").Invoke(@this, new object[] { menuList, @this.m_menuRidDic, menuGroupMemberDic }) as IEnumerator);

			yield return @this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "CoLoadWait").Invoke(@this, null) as IEnumerator);

			watch1.Stop();

			Main.logger.LogInfo($"Vanilla menu file load finished in: {watch1.Elapsed}");

			yield break;
		}
	}
}
