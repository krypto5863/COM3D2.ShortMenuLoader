using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class QuickEdit
	{
		public static Dictionary<int, string> texFileIDDic = new Dictionary<int, string>();
		public static Dictionary<string, Texture2D> tex2DDic = new Dictionary<string, Texture2D>();
		public static Dictionary<int, SceneEdit.SMenuItem> idItemDic = new Dictionary<int, SceneEdit.SMenuItem>();
		private static bool editSceneFlg = false;
		private static bool nowMenuFlg = false;
		private static int nowStrMenuFileID;

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyPrefix]
		public static void StartPre()
		{
			editSceneFlg = true;
			nowMenuFlg = false;
			idItemDic = new Dictionary<int, SceneEdit.SMenuItem>();
		}
		//[HarmonyPatch(typeof(SceneEdit), "InitMenuItemScript")]
		//[HarmonyPrefix]
		public static void InitMenuItemScriptPre(SceneEdit.SMenuItem mi)
		{
			if (!editSceneFlg) return;

			nowMenuFlg = true;
			nowStrMenuFileID = mi.m_nMenuFileRID;

			idItemDic[nowStrMenuFileID] = mi;
		}
		//[HarmonyPatch(typeof(SceneEdit), "InitMenuItemScript")]
		//[HarmonyPostfix]
		public static void InitMenuItemScriptEnd()
		{
			nowMenuFlg = false;
		}

		[HarmonyPatch(typeof(ImportCM), "CreateTexture", new Type[] { typeof(string) })]
		[HarmonyPrefix]
		public static bool CreateTexture(ref string __0, ref Texture2D __result)
		{

			if (!nowMenuFlg || !editSceneFlg)
			{
				return true;
			}

			nowMenuFlg = false;

			texFileIDDic[nowStrMenuFileID] = __0;
			Texture2D tempTex = Texture2D.whiteTexture;

			__result = tempTex;

			return false;
		}
		[HarmonyPatch(typeof(SceneEdit), "OnCompleteFadeIn")]
		[HarmonyPrefix]
		public static void OnCompleteFadeIn()
		{
			editSceneFlg = false;
		}

		public static Texture2D GetTexture(int menuFileID)
		{
			string textureFileName = texFileIDDic[menuFileID];
			if (tex2DDic.ContainsKey(textureFileName))
			{
				if (tex2DDic[textureFileName] == null)
				{
					tex2DDic[textureFileName] = ImportCM.CreateTexture(textureFileName);
				}
				return tex2DDic[textureFileName];
			}
			else
			{
				Texture2D tex2D = ImportCM.CreateTexture(textureFileName);
				tex2DDic.Add(textureFileName, tex2D);
				return tex2D;
			}
		}

		[HarmonyPatch(typeof(RandomPresetCtrl), "GetTextureByRid")]
		[HarmonyPatch(typeof(RandomPresetCtrl), "GetColorTextureByRid")]
		[HarmonyPrefix]
		public static void GetTextureByRid(ref int __2)
		{
			if (idItemDic.ContainsKey(__2))
			{
				SceneEdit.SMenuItem tempItem = idItemDic[__2];
				if (tempItem.m_texIcon == null || tempItem.m_texIcon == Texture2D.whiteTexture)
				{
					tempItem.m_texIcon = GetTexture(tempItem.m_nMenuFileRID);
					tempItem.m_texIconRandomColor = tempItem.m_texIcon;
				}
			}
		}
		public static void MenuItemSet(ref SceneEdit.SMenuItem __1)
		{
			if (!texFileIDDic.ContainsKey(__1.m_nMenuFileRID))
			{
				return;
			}
			if (__1.m_texIcon == null || __1.m_texIcon == Texture2D.whiteTexture)
			{
				try
				{
					__1.m_texIcon = GetTexture(__1.m_nMenuFileRID);
					__1.m_texIconRandomColor = __1.m_texIcon;
				}
				catch
				{
					__1.m_texIcon = Texture2D.whiteTexture;
					__1.m_texIconRandomColor = __1.m_texIcon;
				}
			}
		}
		[HarmonyPatch(typeof(CostumePartsEnabledCtrl), "GetTextureByRid")]
		[HarmonyPrefix]
		public static void CostumeGetTextureByRid(int __2)
		{
			if (idItemDic.ContainsKey(__2))
			{
				SceneEdit.SMenuItem tempItem = idItemDic[__2];
				if (tempItem.m_texIcon == null || tempItem.m_texIcon == Texture2D.whiteTexture)
				{
					tempItem.m_texIcon = GetTexture(tempItem.m_nMenuFileRID);
					tempItem.m_texIconRandomColor = tempItem.m_texIcon;
				}
			}
		}
		[HarmonyPatch(typeof(SceneEditWindow.CustomViewItem), "UpdateIcon")]
		[HarmonyPrefix]
		public static void UpdateIcon(ref SceneEditWindow.CustomViewItem __instance, ref Maid __0)
		{
			if (__0 == null)
			{
				__0 = GameMain.Instance.CharacterMgr.GetMaid(0);

				if (__0 == null)
				{
					return;
				}
			}
			if (__instance.mpn == MPN.chikubi)
			{
				__instance.mpn = MPN.chikubicolor;
			}

			SceneEdit.SMenuItem menuItem = __instance.GetMenuItem(__0, __instance.mpn);

			if (menuItem == null || menuItem.m_boDelOnly && __instance.defaultIconTexture != null || menuItem.m_texIcon == null && __instance.defaultIconTexture != null)
			{
				//Do Nothing
			}
			else if (menuItem.m_texIcon != null)
			{
				int rid = menuItem.m_nMenuFileRID;
				if (idItemDic.ContainsKey(rid))
				{
					SceneEdit.SMenuItem tempItem = idItemDic[rid];
					if (tempItem.m_texIcon == null || tempItem.m_texIcon == Texture2D.whiteTexture)
					{
						tempItem.m_texIcon = GetTexture(tempItem.m_nMenuFileRID);
						tempItem.m_texIconRandomColor = tempItem.m_texIcon;
					}
				}
			}

			if (__instance.mpn == MPN.chikubicolor)
			{
				__instance.mpn = MPN.chikubi;
			}
			return;
		}
	}
}
