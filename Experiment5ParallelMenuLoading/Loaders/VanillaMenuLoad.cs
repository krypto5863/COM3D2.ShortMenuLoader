﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using COM3D2API.Utilities;
using UnityEngine;

namespace ShortMenuLoader.Loaders
{
	internal class VanillaMenuLoad
	{
		public static IEnumerator VanillaMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			var filesToLoadFromDatabase = new Dictionary<SceneEdit.SMenuItem, int>();
			var filesToLoad = new Dictionary<SceneEdit.SMenuItem, string>();

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			if (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return new TimedWaitUntil(() => GameMain.Instance.CharacterMgr.IsBusy() == false, 0.25f);
			}

			var menuDataBase = GameMain.Instance.MenuDataBase;
			
			var waitOnKiss = new Stopwatch();
			waitOnKiss.Start();

			if (!menuDataBase.JobFinished())
			{
				yield return new TimedWaitUntil(() => menuDataBase.JobFinished(), 0.5f);
			}

			waitOnKiss.Stop();
			
			var fileCount = menuDataBase.GetDataSize();

			for (var i = 0; i < fileCount; i++)
			{
				menuDataBase.SetIndex(i);
				var fileName = menuDataBase.GetMenuFileName();

				if (GameMain.Instance.CharacterMgr.status.IsHavePartsItem(fileName))
				{
					var mi = new SceneEdit.SMenuItem
					{
						m_strMenuFileName = fileName,
						m_nMenuFileRID = fileName.GetHashCode()
					};

					filesToLoadFromDatabase[mi] = i;
				}
			}

			foreach (var mi in filesToLoadFromDatabase.Keys)
			{
				try
				{
					string iconFileName = null;

					if (string.IsNullOrEmpty(mi.m_strMenuName))
					{
						ReadMenuItemDataFromNative(mi, filesToLoadFromDatabase[mi], out iconFileName);
					}

					filesToLoad[mi] = null;

					if (!string.IsNullOrEmpty(iconFileName) && GameUty.FileSystem.IsExistentFile(iconFileName))
					{
						/*
						if (SceneEdit.Instance != null)
						{
							SceneEdit.Instance.editItemTextureCache.PreLoadRegister(mi.m_nMenuFileRID, iconFileName);
						}
						else
						{
							mi.m_texIcon = ImportCM.CreateTexture(iconFileName);
						}
						*/
						//Since Vanilla loader doesn't run threads, it can run just fine on main thread which we've designated as our only thread for adding icons to late loading.
						BackgroundIconLoader.RegisterRidIcon(mi.m_nMenuFileRID, iconFileName);
					}
				}
				catch (Exception ex)
				{
					ShortMenuLoader.PLogger.LogError(string.Concat("ReadMenuItemDataFromNative Exception(例外):", mi.m_strMenuFileName, "\n\n", ex.Message, " StackTrace／", ex.StackTrace));
				}
			}

			if (GsModMenuLoad.DictionaryBuilt == false)
			{
				yield return new TimedWaitUntil(() => GsModMenuLoad.DictionaryBuilt, 0.25f);
			}

			foreach (var mi in filesToLoad.Keys)
			{
				//Added the CRC checks to make this plug compatible with 3.xx
				if (!mi.m_bMan && !mi.m_strMenuFileName.Contains("_crc") && !mi.m_strMenuFileName.Contains("crc_") && !GsModMenuLoad.FilesDictionary.ContainsKey(mi.m_strMenuFileName) && ShortMenuLoader.SceneEditInstance.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
				{
					ShortMenuLoader.SceneEditInstance.AddMenuItemToList(mi);

					menuList.Add(mi);

					ShortMenuLoader.SceneEditInstance.m_menuRidDic[mi.m_nMenuFileRID] = mi;
					var parentMenuName = SceneEdit.GetParentMenuFileName(mi);

					if (!string.IsNullOrEmpty(parentMenuName))
					{
						var hashCode = parentMenuName.GetHashCode();
						if (!menuGroupMemberDic.ContainsKey(hashCode))
						{
							menuGroupMemberDic.Add(hashCode, new List<int>());
						}
						menuGroupMemberDic[hashCode].Add(mi.m_strMenuFileName.ToLower().GetHashCode());
					}
					else if (mi.m_strCateName.IndexOf("set_", StringComparison.Ordinal) != -1 && mi.m_strMenuFileName.IndexOf("_del", StringComparison.Ordinal) == -1)
					{
						mi.m_bGroupLeader = true;
						mi.m_listMember = new List<SceneEdit.SMenuItem>
							{
								mi
							};
					}

					if (ShortMenuLoader.BreakInterval.Value < Time.realtimeSinceStartup - ShortMenuLoader.Time)
					{
						yield return null;
						ShortMenuLoader.Time = Time.realtimeSinceStartup;
					}
				}
			}

			ShortMenuLoader.ThreadsDone++;
			ShortMenuLoader.PLogger.LogInfo($"Vanilla menus finished loading at: {ShortMenuLoader.WatchOverall.Elapsed}. "
			+ ($"We also spent {waitOnKiss.Elapsed} waiting for an unmodified database to finish loading..."));
		}

		public static void ReadMenuItemDataFromNative(SceneEdit.SMenuItem mi, int menuDataBaseIndex, out string iconStr)
		{
			var menuDataBase = GameMain.Instance.MenuDataBase;
			menuDataBase.SetIndex(menuDataBaseIndex);
			mi.m_strMenuName = menuDataBase.GetMenuName();
			mi.m_strInfo = menuDataBase.GetItemInfoText();
			mi.m_mpn = (MPN)menuDataBase.GetCategoryMpn();
			mi.m_strCateName = menuDataBase.GetCategoryMpnText();
			mi.m_eColorSetMPN = (MPN)menuDataBase.GetColorSetMpn();
			mi.m_strMenuNameInColorSet = menuDataBase.GetMenuNameInColorSet();
			mi.m_pcMultiColorID = (MaidParts.PARTS_COLOR)menuDataBase.GetMultiColorId();
			mi.m_boDelOnly = menuDataBase.GetBoDelOnly();
			mi.m_fPriority = menuDataBase.GetPriority();
			mi.m_bMan = menuDataBase.GetIsMan();
			mi.m_bOld = menuDataBase.GetVersion() < 2000;
			iconStr = menuDataBase.GetIconS();

			if (ShortMenuLoader.PutMenuFileNameInItemDescription.Value)
			{
				mi.m_strInfo += $"\n\n{menuDataBase.GetMenuFileName()}";
			}
		}
	}
}