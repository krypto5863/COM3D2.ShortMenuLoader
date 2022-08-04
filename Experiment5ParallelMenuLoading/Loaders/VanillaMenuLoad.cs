using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class VanillaMenuLoad
	{
		private static readonly string CacheFile = BepInEx.Paths.CachePath + "\\ShortMenuLoaderVanillaCache.json";
		private static bool CacheLoadDone = false;
		private static Dictionary<string, MenuStub> MenuCache = new Dictionary<string, MenuStub>();

		public static IEnumerator LoadCache(int Retry = 0)
		{
			CacheLoadDone = false;

			if (!Main.SMVDLoaded && Main.UseVanillaCache.Value)
			{
				Task cacheLoader = Task.Factory.StartNew(new Action(() =>
				{
					if (File.Exists(CacheFile))
					{
						string jsonString = File.ReadAllText(CacheFile);

						var tempDic = JsonConvert.DeserializeObject<Dictionary<string, MenuStub>>(jsonString);

						MenuCache = tempDic;
					}
				}));

				while (cacheLoader.IsCompleted == false)
				{
					yield return null;
				}

				if (cacheLoader.IsFaulted)
				{
					if (Retry < 3)
					{
						Main.logger.LogError($"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the load task...");

						yield return new WaitForSecondsRealtime(5);

						Main.@this2.StartCoroutine(LoadCache(++Retry));

						yield break;
					}
					else
					{
						Main.logger.LogError($"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nThis is the 4th attempt to kickstart the task. Cache will be deleted and rebuilt next time.");

						MenuCache = new Dictionary<string, MenuStub>();

						File.Delete(CacheFile);
					}
				}
			}
			CacheLoadDone = true;
		}

		public static IEnumerator SaveCache(Dictionary<SceneEdit.SMenuItem, string> filesToLoad, int Retry = 0)
		{
			if (!Main.SMVDLoaded && Main.UseVanillaCache.Value)
			{
				Task cacheSaver = Task.Factory.StartNew(new Action(() =>
				{
					MenuCache = MenuCache
					.Where(k => filesToLoad.Keys
					.Select(t => t.m_strMenuFileName)
					.ToList()
					.Contains(k.Key)
					)
					.ToDictionary(t => t.Key, l => l.Value);

					File.WriteAllText(CacheFile, JsonConvert.SerializeObject(MenuCache));

					Main.logger.LogInfo("Finished cleaning and saving the mod cache...");
				}));

				while (cacheSaver.IsCompleted == false)
				{
					yield return null;
				}

				if (cacheSaver.IsFaulted)
				{
					if (Retry < 3)
					{
						Main.logger.LogError($"Cache saver task failed due to an unexpected error! This is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the task again...");

						yield return new WaitForSecondsRealtime(5);

						Main.@this2.StartCoroutine(SaveCache(filesToLoad, ++Retry));
					}
					else
					{
						Main.logger.LogFatal($"Cache saver task failed due to an unexpected error! This is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nNo further attempts will be made to start the task again...");

						throw cacheSaver.Exception.InnerException;
					}
				}
			}
		}

		public static IEnumerator VanillaMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			Dictionary<SceneEdit.SMenuItem, int> filesToLoadFromDatabase = new Dictionary<SceneEdit.SMenuItem, int>();
			Dictionary<SceneEdit.SMenuItem, string> filesToLoad = new Dictionary<SceneEdit.SMenuItem, string>();

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
			}

			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;

			Stopwatch waitOnKiss = new Stopwatch();
			waitOnKiss.Start();

			//var LastDateModified = File.GetLastWriteTime(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat");

			while (!menuDataBase.JobFinished())
			{
				yield return null;
			}

			waitOnKiss.Stop();

			//This entire for loop is what loads in normal game menus. It's been left relatively untouched.

			if (!Main.SMVDLoaded)
			{
				int fileCount = menuDataBase.GetDataSize();

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

						filesToLoadFromDatabase[mi] = i;
					}
				}
			}
			else
			{
				VanillaMenuLoaderSMVDCompat.LoadFromSMVDDictionary(ref filesToLoadFromDatabase);
			}

			while (CacheLoadDone != true && Main.UseVanillaCache.Value)
			{
				yield return null;
			}

			foreach (SceneEdit.SMenuItem mi in filesToLoadFromDatabase.Keys)
			{
				try
				{
					string iconFileName = null;

					if (MenuCache.ContainsKey(mi.m_strMenuFileName) && Main.UseVanillaCache.Value)
					{
						MenuStub tempStub = MenuCache[mi.m_strMenuFileName];

						if (tempStub.DateModified == File.GetLastWriteTimeUtc(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat"))
						{
							mi.m_strMenuName = tempStub.Name;
							mi.m_strInfo = tempStub.Description;
							mi.m_mpn = tempStub.Category;
							mi.m_strCateName = Enum.GetName(typeof(MPN), tempStub.Category);
							mi.m_eColorSetMPN = tempStub.ColorSetMPN;
							mi.m_strMenuNameInColorSet = tempStub.ColorSetMenu;
							mi.m_pcMultiColorID = tempStub.MultiColorID;
							mi.m_boDelOnly = tempStub.DelMenu;
							mi.m_fPriority = tempStub.Priority;
							mi.m_bMan = tempStub.ManMenu;
							mi.m_bOld = tempStub.LegacyMenu;

							iconFileName = tempStub.Icon;
						}
						else
						{
							Main.logger.LogWarning("GameData folder was changed! We'll be wiping the vanilla cache clean and rebuilding it now.");
							MenuCache = new Dictionary<string, MenuStub>();
						}
					}

					if (string.IsNullOrEmpty(mi.m_strMenuName))
					{
						//Main.logger.LogInfo($"Loading {mi.m_strMenuFileName} from the database as it wasn't in cache...");
						VanillaMenuLoad.ReadMenuItemDataFromNative(mi, filesToLoadFromDatabase[mi], out iconFileName);
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
						QuickEditVanilla.f_RidsToStubs[mi.m_nMenuFileRID] = iconFileName;
					}
				}
				catch (Exception ex)
				{
					Main.logger.LogError(string.Concat(new string[]
					{
					"ReadMenuItemDataFromNative Exception(例外):",
					mi.m_strMenuFileName,
					"\n\n",
					ex.Message,
					" StackTrace／",
					ex.StackTrace
					}));
				}
			}

			while (GSModMenuLoad.DictionaryBuilt == false)
			{
				yield return null;
			}

			foreach (SceneEdit.SMenuItem mi in filesToLoad.Keys)
			{
				//Added the CRC checks to make this plug compatible with 3.xx
				if (!mi.m_bMan && !mi.m_strMenuFileName.Contains("_crc") && !mi.m_strMenuFileName.Contains("crc_") && !GSModMenuLoad.FilesDictionary.ContainsKey(mi.m_strMenuFileName) && Main.@this.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
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

					if (Main.BreakInterval.Value < Time.realtimeSinceStartup - Main.time)
					{
						yield return null;
						Main.time = Time.realtimeSinceStartup;
					}
				}
			}

			Main.ThreadsDone++;
			Main.logger.LogInfo($"Vanilla menus finished loading at: {Main.WatchOverall.Elapsed}. "
			+ ((Main.SMVDLoaded == false) ?
			$"We also spent {waitOnKiss.Elapsed} waiting for an unmodified database to finish loading..."
			: $"We also spent {waitOnKiss.Elapsed} waiting for SMVD's Database to load..."));

			Main.@this.StartCoroutine(SaveCache(filesToLoad));
		}

		public static void ReadMenuItemDataFromNative(SceneEdit.SMenuItem mi, int menuDataBaseIndex, out string iconStr)
		{
			if (!Main.SMVDLoaded)
			{
				MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;
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
				mi.m_bOld = (menuDataBase.GetVersion() < 2000);
				iconStr = menuDataBase.GetIconS();

				if (Main.UseVanillaCache.Value)
				{
					MenuStub newStub = new MenuStub()
					{
						Name = mi.m_strMenuName,
						Description = mi.m_strInfo,
						Category = mi.m_mpn,
						ColorSetMPN = mi.m_eColorSetMPN,
						ColorSetMenu = mi.m_strMenuNameInColorSet,
						MultiColorID = mi.m_pcMultiColorID,
						DelMenu = mi.m_boDelOnly,
						Priority = mi.m_fPriority,
						ManMenu = mi.m_bMan,
						LegacyMenu = mi.m_bOld,
						Icon = iconStr,
						DateModified = File.GetLastWriteTimeUtc(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat")
					};
					MenuCache[mi.m_strMenuFileName] = newStub;
				}

				if (Main.PutMenuFileNameInItemDescription.Value)
				{
					mi.m_strInfo = mi.m_strInfo + $"\n\n{menuDataBase.GetMenuFileName()}";
				}
			}
			else
			{
				VanillaMenuLoaderSMVDCompat.ReadMenuItemDataFromNative(mi, menuDataBaseIndex, out iconStr);
			}
		}
	}
}