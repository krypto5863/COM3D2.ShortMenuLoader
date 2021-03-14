using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShortMenuLoader
{
	internal class VanillaMenuLoad
	{

		private const string CacheFileName = "ShortMenuLoaderVanillaCache.json";
		private static bool CacheLoadDone = false;
		private static Dictionary<string, MenuStub> MenuCache = new Dictionary<string, MenuStub>();
		public static IEnumerator LoadCache(int Retry = 0)
		{
			CacheLoadDone = false;

			if (Main.UseVanillaCache.Value)
			{
				Task cacheLoader = Task.Factory.StartNew(new Action(() =>
				{
					if (File.Exists(BepInEx.Paths.ConfigPath + "\\" + CacheFileName))
					{
						string jsonString = File.ReadAllText(BepInEx.Paths.ConfigPath + "\\" + CacheFileName);

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
						Debug.LogWarning($"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the load task...");

						yield return new WaitForSecondsRealtime(5);

						Main.@this2.StartCoroutine(LoadCache(++Retry));

						yield break;
					}
					else
					{
						Debug.LogWarning($"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nThis is the 4th attempt to kickstart the task. Cache will be deleted and rebuilt next time.");

						MenuCache = new Dictionary<string, MenuStub>();

						File.Delete(BepInEx.Paths.ConfigPath + "\\" + CacheFileName);
					}
				}
			}
			CacheLoadDone = true;
		}
		public static IEnumerator SaveCache(Dictionary<SceneEdit.SMenuItem, string> filesToLoad, int Retry = 0)
		{
			if (Main.UseVanillaCache.Value)
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

					File.WriteAllText(BepInEx.Paths.ConfigPath + "\\" + CacheFileName, JsonConvert.SerializeObject(MenuCache));

					Debug.Log("Finished cleaning and saving the mod cache...");
				}));

				while (cacheSaver.IsCompleted == false)
				{
					yield return null;
				}

				if (cacheSaver.IsFaulted)
				{
					if (Retry < 3)
					{
						Debug.LogWarning($"Cache saver task failed due to an unexpected error! This is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the task again...");

						yield return new WaitForSecondsRealtime(5);

						Main.@this2.StartCoroutine(SaveCache(filesToLoad, ++Retry));
					}
					else
					{
						Debug.LogWarning($"Cache saver task failed due to an unexpected error! This is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nNo further attempts will be made to start the task again...");
					}
				}
			}
		}
		public static IEnumerator VanillaMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{

			Dictionary<SceneEdit.SMenuItem, int> filesToLoadFromDatabase = new Dictionary<SceneEdit.SMenuItem, int>();
			Dictionary<SceneEdit.SMenuItem, string> filesToLoad = new Dictionary<SceneEdit.SMenuItem, string>();

			Stopwatch watch1 = new Stopwatch();
			watch1.Start();

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
			}

			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;

			while (!menuDataBase.JobFinished())
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

					filesToLoadFromDatabase[mi] = i;
				}
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
							mi.m_mpn = (MPN)Enum.Parse(typeof(MPN), tempStub.Category);
							mi.m_strCateName = tempStub.Category;
							mi.m_eColorSetMPN = (MPN)Enum.Parse(typeof(MPN), tempStub.ColorSetMPN);
							mi.m_strMenuNameInColorSet = tempStub.ColorSetMenu;
							mi.m_pcMultiColorID = (MaidParts.PARTS_COLOR)Enum.Parse(typeof(MaidParts.PARTS_COLOR), tempStub.MultiColorID);
							mi.m_boDelOnly = tempStub.DelMenu;
							mi.m_fPriority = tempStub.Priority;
							mi.m_bMan = tempStub.ManMenu;
							mi.m_bOld = tempStub.LegacyMenu;

							iconFileName = tempStub.Icon;
						}
						else
						{
							Debug.LogWarning("GameData folder was changed! We'll be wiping the vanilla cache clean and rebuilding it now.");
							MenuCache = new Dictionary<string, MenuStub>();
						}
					}

					if (string.IsNullOrEmpty(mi.m_strMenuName))
					{
						//Debug.Log($"Loading {mi.m_strMenuFileName} from the database as it wasn't in cache...");
						VanillaMenuLoad.ReadMenuItemDataFromNative(mi, filesToLoadFromDatabase[mi], out iconFileName);
					}

					filesToLoad[mi] = null;

					if (!string.IsNullOrEmpty(iconFileName) && GameUty.FileSystem.IsExistentFile(iconFileName))
					{
						if (SceneEdit.Instance != null)
						{
							SceneEdit.Instance.editItemTextureCache.PreLoadRegister(mi.m_nMenuFileRID, iconFileName);
						}
						else
						{
							mi.m_texIcon = ImportCM.CreateTexture(iconFileName);
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogError(string.Concat(new string[]
					{
					"ReadMenuItemDataFromNative 例外／",
					mi.m_strMenuFileName,
					"／",
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
				if (!mi.m_bMan && !GSModMenuLoad.FilesDictionary.ContainsKey(mi.m_strMenuFileName) && Main.@this.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
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

			Main.ThreadsDone++;
			Debug.Log($"Vanilla menus finished loading in {watch1.Elapsed} at: {Main.WatchOverall.Elapsed}");

			Main.@this.StartCoroutine(SaveCache(filesToLoad));
		}
		public static void ReadMenuItemDataFromNative(SceneEdit.SMenuItem mi, int menuDataBaseIndex, out string iconStr)
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
					Category = mi.m_mpn.ToString(),
					ColorSetMPN = mi.m_eColorSetMPN.ToString(),
					ColorSetMenu = mi.m_strMenuNameInColorSet,
					MultiColorID = mi.m_pcMultiColorID.ToString(),
					DelMenu = mi.m_boDelOnly,
					Priority = mi.m_fPriority,
					ManMenu = mi.m_bMan,
					LegacyMenu = mi.m_bOld,
					Icon = iconStr,
					DateModified = File.GetLastWriteTimeUtc(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat")
				};

				MenuCache[mi.m_strMenuFileName] = newStub;
			}
		}
	}
}
