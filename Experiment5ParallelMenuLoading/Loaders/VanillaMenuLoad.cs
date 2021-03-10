using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class VanillaMenuLoad
	{
		public static IEnumerator VanillaMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{

			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;

			while (!menuDataBase.JobFinished())
			{
				yield return null;
			}

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
			}

			int fileCount = menuDataBase.GetDataSize();

			//This entire for loop is what loads in normal game menus. It's been left relatively untouched.
			for (int i = 0; i < fileCount; i++)
			{
				menuDataBase.SetIndex(i);
				string fileName = menuDataBase.GetMenuFileName();

				if (GameMain.Instance.CharacterMgr.status.IsHavePartsItem(fileName))
				{

					SceneEdit.SMenuItem mi = new SceneEdit.SMenuItem
					{
						m_strMenuFileName = fileName,
						m_nMenuFileRID = fileName.GetHashCode()
					};
					try
					{
						SceneEdit.ReadMenuItemDataFromNative(mi, i);
					}
					catch (Exception ex)
					{
						Debug.LogError(string.Concat(new string[]
						{
					"ReadMenuItemDataFromNative 例外／",
					fileName,
					"／",
					ex.Message,
					" StackTrace／",
					ex.StackTrace
						}));
					}

					if (!mi.m_bMan && Main.@this.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
					{
						AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(Main.@this, new object[] { mi });
						menuList.Add(mi);
						Main.@this.m_menuRidDic[mi.m_nMenuFileRID] = mi;
						string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(Main.@this, new object[] { mi }) as string;
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
							mi.m_listMember = new List<SceneEdit.SMenuItem>
							{
								mi
							};
						}

						if (0.5f < Time.realtimeSinceStartup - Main.time)
						{
							yield return null;
							Main.time = Time.realtimeSinceStartup;
						}
					}
				}
			}
			Main.ThreadsDone++;
			Debug.Log($"Vanilla menus finished loading at: {Main.WatchOverall.Elapsed}");
		}
	}
}
