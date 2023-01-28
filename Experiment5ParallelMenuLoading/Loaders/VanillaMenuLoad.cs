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
		private static bool _cacheLoadDone;
		private static Dictionary<string, MenuStub> _menuCache = new Dictionary<string, MenuStub>();

		public static IEnumerator LoadCache(int retry = 0)
		{
			_cacheLoadDone = false;

			if (!Main.SmvdLoaded && Main.UseVanillaCache.Value)
			{
				var cacheLoader = Task.Factory.StartNew(() =>
				{
					if (File.Exists(CacheFile))
					{
						var jsonString = File.ReadAllText(CacheFile);

						var tempDic = JsonConvert.DeserializeObject<Dictionary<string, MenuStub>>(jsonString);

						_menuCache = tempDic;
					}
				});

				while (cacheLoader.IsCompleted == false)
				{
					yield return null;
				}

				if (cacheLoader.IsFaulted)
				{
					if (retry < 3)
					{
						if (cacheLoader.Exception?.InnerException != null)
						{
							Main.PLogger.LogError(
								$"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the load task...");
						}

						yield return new WaitForSecondsRealtime(5);

						Main.PlugInstance.StartCoroutine(LoadCache(++retry));

						yield break;
					}

					if (cacheLoader.Exception?.InnerException != null)
					{
						Main.PLogger.LogError(
							$"There was an error while attempting to load the vanilla cache: \n{cacheLoader.Exception.InnerException.Message}\n{cacheLoader.Exception.InnerException.StackTrace}\n\nThis is the 4th attempt to kick-start the task. Cache will be deleted and rebuilt next time.");
					}

					_menuCache = new Dictionary<string, MenuStub>();

					File.Delete(CacheFile);
				}
			}
			_cacheLoadDone = true;
		}

		public static IEnumerator SaveCache(Dictionary<SceneEdit.SMenuItem, string> filesToLoad, int retry = 0)
		{
			if (Main.SmvdLoaded || !Main.UseVanillaCache.Value)
			{
				yield break;
			}

			var cacheSaver = Task.Factory.StartNew(() =>
			{
				_menuCache = _menuCache
					.Where(k => filesToLoad.Keys
						.Select(t => t.m_strMenuFileName)
						.ToList()
						.Contains(k.Key)
					)
					.ToDictionary(t => t.Key, l => l.Value);

				File.WriteAllText(CacheFile, JsonConvert.SerializeObject(_menuCache));

				Main.PLogger.LogInfo("Finished cleaning and saving the mod cache...");
			});

			while (cacheSaver.IsCompleted == false)
			{
				yield return null;
			}

			if (!cacheSaver.IsFaulted)
			{
				yield break;
			}

			if (retry < 3)
			{
				if (cacheSaver.Exception?.InnerException != null)
				{
					Main.PLogger.LogError(
						$"Cache saver task failed due to an unexpected error! SceneEditInstance is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nAn attempt will be made to restart the task again...");
				}

				yield return new WaitForSecondsRealtime(5);

				Main.PlugInstance.StartCoroutine(SaveCache(filesToLoad, ++retry));
			}
			else
			{
				if (cacheSaver.Exception?.InnerException != null)
				{
					Main.PLogger.LogFatal(
						$"Cache saver task failed due to an unexpected error! SceneEditInstance is considered a minor failure: {cacheSaver.Exception.InnerException.Message}\n{cacheSaver.Exception.InnerException.StackTrace}\n\nNo further attempts will be made to start the task again...");

					throw cacheSaver.Exception.InnerException;
				}
			}
		}

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

			//var LastDateModified = File.GetLastWriteTime(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat");

			if (!menuDataBase.JobFinished())
			{
				//yield return null;
				yield return new TimedWaitUntil(() => menuDataBase.JobFinished(), 0.5f);
			}

			waitOnKiss.Stop();

			//SceneEditInstance entire for loop is what loads in normal game menus. It's been left relatively untouched.

			if (!Main.SmvdLoaded)
			{
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
			}
			else
			{
				VanillaMenuLoaderSmvdCompat.LoadFromSmvdDictionary(ref filesToLoadFromDatabase);
			}

			if (_cacheLoadDone != true && Main.UseVanillaCache.Value)
			{
				yield return new TimedWaitUntil(() => _cacheLoadDone, 0.5f);
			}

			foreach (var mi in filesToLoadFromDatabase.Keys)
			{
				try
				{
					string iconFileName = null;

					if (_menuCache.ContainsKey(mi.m_strMenuFileName) && Main.UseVanillaCache.Value)
					{
						var tempStub = _menuCache[mi.m_strMenuFileName];

						if (tempStub.DateModified == File.GetLastWriteTimeUtc(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat"))
						{
							mi.m_strMenuName = tempStub.Name;
							mi.m_strInfo = tempStub.Description;
							mi.m_mpn = tempStub.Category;
							mi.m_strCateName = Enum.GetName(typeof(MPN), tempStub.Category);
							mi.m_eColorSetMPN = tempStub.ColorSetMpn;
							mi.m_strMenuNameInColorSet = tempStub.ColorSetMenu;
							mi.m_pcMultiColorID = tempStub.MultiColorId;
							mi.m_boDelOnly = tempStub.DelMenu;
							mi.m_fPriority = tempStub.Priority;
							mi.m_bMan = tempStub.ManMenu;
							mi.m_bOld = tempStub.LegacyMenu;

							iconFileName = tempStub.Icon;
						}
						else
						{
							Main.PLogger.LogWarning("GameData folder was changed! We'll be wiping the vanilla cache clean and rebuilding it now.");
							_menuCache = new Dictionary<string, MenuStub>();
						}
					}

					if (string.IsNullOrEmpty(mi.m_strMenuName))
					{
						//Main.PLogger.LogInfo($"Loading {mi.m_strMenuFileName} from the database as it wasn't in cache...");
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
						QuickEditVanilla.FRidsToStubs[mi.m_nMenuFileRID] = iconFileName;
					}
				}
				catch (Exception ex)
				{
					Main.PLogger.LogError(string.Concat("ReadMenuItemDataFromNative Exception(例外):", mi.m_strMenuFileName, "\n\n", ex.Message, " StackTrace／", ex.StackTrace));
				}
			}

			if (GsModMenuLoad.DictionaryBuilt == false)
			{
				yield return new TimedWaitUntil(() => GsModMenuLoad.DictionaryBuilt, 0.25f);
			}

			foreach (var mi in filesToLoad.Keys)
			{
				//Added the CRC checks to make this plug compatible with 3.xx
				if (!mi.m_bMan && !mi.m_strMenuFileName.Contains("_crc") && !mi.m_strMenuFileName.Contains("crc_") && !GsModMenuLoad.FilesDictionary.ContainsKey(mi.m_strMenuFileName) && Main.SceneEditInstance.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
				{
					AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(Main.SceneEditInstance, new object[] { mi });

					menuList.Add(mi);

					Main.SceneEditInstance.m_menuRidDic[mi.m_nMenuFileRID] = mi;
					var parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(Main.SceneEditInstance, new object[] { mi }) as string;

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

					if (Main.BreakInterval.Value < Time.realtimeSinceStartup - Main.Time)
					{
						yield return null;
						Main.Time = Time.realtimeSinceStartup;
					}
				}
			}

			Main.ThreadsDone++;
			Main.PLogger.LogInfo($"Vanilla menus finished loading at: {Main.WatchOverall.Elapsed}. "
			+ (Main.SmvdLoaded == false ?
			$"We also spent {waitOnKiss.Elapsed} waiting for an unmodified database to finish loading..."
			: $"We also spent {waitOnKiss.Elapsed} waiting for SMVD's Database to load..."));

			Main.SceneEditInstance.StartCoroutine(SaveCache(filesToLoad));
		}

		public static void ReadMenuItemDataFromNative(SceneEdit.SMenuItem mi, int menuDataBaseIndex, out string iconStr)
		{
			if (!Main.SmvdLoaded)
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

				if (Main.UseVanillaCache.Value)
				{
					var newStub = new MenuStub
					{
						Name = mi.m_strMenuName,
						Description = mi.m_strInfo,
						Category = mi.m_mpn,
						ColorSetMpn = mi.m_eColorSetMPN,
						ColorSetMenu = mi.m_strMenuNameInColorSet,
						MultiColorId = mi.m_pcMultiColorID,
						DelMenu = mi.m_boDelOnly,
						Priority = mi.m_fPriority,
						ManMenu = mi.m_bMan,
						LegacyMenu = mi.m_bOld,
						Icon = iconStr,
						DateModified = File.GetLastWriteTimeUtc(BepInEx.Paths.GameRootPath + "\\GameData\\paths.dat")
					};
					_menuCache[mi.m_strMenuFileName] = newStub;
				}

				if (Main.PutMenuFileNameInItemDescription.Value)
				{
					mi.m_strInfo += $"\n\n{menuDataBase.GetMenuFileName()}";
				}
			}
			else
			{
				VanillaMenuLoaderSmvdCompat.ReadMenuItemDataFromNative(mi, menuDataBaseIndex, out iconStr);
			}
		}
	}
}