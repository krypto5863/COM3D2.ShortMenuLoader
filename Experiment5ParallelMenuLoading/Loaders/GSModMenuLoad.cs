using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using COM3D2API.Utilities;
using MessagePack;
using MessagePack.Resolvers;
using UnityEngine;
using TMonitor = System.Threading.Monitor;

namespace ShortMenuLoader.Loaders
{
	internal class GsModMenuLoad
	{
		public static readonly Dictionary<string, string> FilesDictionary = new Dictionary<string, string>();
		public static bool DictionaryBuilt;
		private static readonly Dictionary<string, MemoryStream> FilesToRead = new Dictionary<string, MemoryStream>();

		private static readonly string CacheFile = BepInEx.Paths.CachePath + "\\ShortMenuLoaderCache";
		private static bool _cacheLoadDone;
		private static Dictionary<string, MenuStub> _menuCache = new Dictionary<string, MenuStub>();

		private static readonly MessagePackSerializerOptions SerializerOptions = MessagePackSerializerOptions.Standard.WithResolver(
			ContractlessStandardResolver.Instance
			);

		private static int _mutexTimeoutCounter;

		public static IEnumerator LoadCache(int retry = 0)
		{
			_cacheLoadDone = false;

			var cacheLoader = Task.Factory.StartNew(() =>
			{
				if (File.Exists(CacheFile))
				{
					var jsonString = File.ReadAllBytes(CacheFile);

					var tempDic = MessagePackSerializer.Deserialize<Dictionary<string, MenuStub>>(jsonString, SerializerOptions);//JsonConvert.DeserializeObject<Dictionary<string, MenuStub>>(jsonString);

					_menuCache = tempDic;
				}

				_cacheLoadDone = true;
			});

			while (cacheLoader.IsCompleted == false)
			{
				yield return null;
			}

			if (cacheLoader.IsFaulted)
			{
				if (retry < 3)
				{
					ShortMenuLoader.PLogger.LogError($"There was an error while attempting to load the mod cache: \n{cacheLoader.Exception?.InnerException?.Message}\n{cacheLoader.Exception?.InnerException?.StackTrace}\n\nAn attempt will be made to restart the load task...");

					yield return new WaitForSecondsRealtime(5);

					ShortMenuLoader.PlugInstance.StartCoroutine(LoadCache(++retry));
				}
				else
				{
					ShortMenuLoader.PLogger.LogError($"There was an error while attempting to load the mod cache: \n{cacheLoader.Exception?.InnerException?.Message}\n{cacheLoader.Exception?.InnerException?.StackTrace}\n\nThis is the 4th attempt to kickstart the task. Cache will be deleted and rebuilt next time.");

					_menuCache = new Dictionary<string, MenuStub>();

					File.Delete(CacheFile);

					_cacheLoadDone = true;
				}
			}
		}

		public static IEnumerator SaveCache(int retry = 0)
		{
			var cacheSaver = Task.Factory.StartNew(() =>
			{
				_menuCache = _menuCache
					.Where(k => FilesDictionary.Keys.Contains(k.Key))
					.ToDictionary(t => t.Key, l => l.Value);

				File.WriteAllBytes(CacheFile, MessagePackSerializer.Serialize(_menuCache, SerializerOptions));

				ShortMenuLoader.PLogger.LogDebug("Finished cleaning and saving the mod cache...");
			});

			while (cacheSaver.IsCompleted == false)
			{
				yield return null;
			}

			if (cacheSaver.IsFaulted)
			{
				if (retry < 3)
				{
					ShortMenuLoader.PLogger.LogError($"Cache saver task failed due to an unexpected error! SceneEditInstance is considered a minor failure: {cacheSaver.Exception?.InnerException?.Message}\n{cacheSaver.Exception?.InnerException?.StackTrace}\n\nAn attempt will be made to restart the task again...");

					yield return new WaitForSecondsRealtime(5);

					ShortMenuLoader.PlugInstance.StartCoroutine(SaveCache(++retry));

					yield break;
				}

				ShortMenuLoader.PLogger.LogFatal($"Cache saver task failed due to an unexpected error! SceneEditInstance is considered a minor failure: {cacheSaver.Exception?.InnerException?.Message}\n{cacheSaver.Exception?.InnerException?.StackTrace}\n\nNo further attempts will be made to start the task again...");

				throw cacheSaver.Exception?.InnerException;
			}
		}

		public static IEnumerator GsMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			var listOfLoads = new HashSet<SceneEdit.SMenuItem>();
			var listOfDuplicates = new List<string>();

			FilesToRead.Clear();
			FilesDictionary.Clear();

			DictionaryBuilt = false;

			var cts = new CancellationTokenSource();
			var token = cts.Token;

			var loaderWorker = Task.Factory.StartNew(() =>
			{
				ShortMenuLoader.PLogger.LogDebug($"Fetching files @ {ShortMenuLoader.WatchOverall.Elapsed}");

				foreach (var s in ShortMenuLoader.FilesInModFolder.Where(t => t.ToLower().EndsWith(".menu")))
				{
					if (!FilesDictionary.ContainsKey(Path.GetFileName(s).ToLower()))
					{
						FilesDictionary[Path.GetFileName(s).ToLower()] = s;
					}
					else
					{
						listOfDuplicates.Add(s);
					}
				}

				ShortMenuLoader.PLogger.LogDebug($"Done fetching files @ {ShortMenuLoader.WatchOverall.Elapsed}");

				DictionaryBuilt = true;

				while (_cacheLoadDone != true)
				{
					Thread.Sleep(100);
				}

				var servant = Task.Factory.StartNew(() =>
				{
					foreach (var s in FilesDictionary.Keys)
					{
						try
						{
							if (_menuCache.ContainsKey(s.ToLower()))
							{
								if (TMonitor.TryEnter(FilesToRead, ShortMenuLoader.TimeoutLimit.Value))
								{
									try
									{
										FilesToRead[s.ToLower()] = null;
									}
									finally
									{
										TMonitor.Exit(FilesToRead);
									}
								}
								else
								{
									ShortMenuLoader.PLogger.LogError("Timed out waiting for mutex to allow entry...");
									cts.Cancel();
									token.ThrowIfCancellationRequested();
								}
							}
							else
							{
								if (TMonitor.TryEnter(FilesToRead, ShortMenuLoader.TimeoutLimit.Value))
								{
									try
									{
										FilesToRead[s.ToLower()] = new MemoryStream(File.ReadAllBytes(FilesDictionary[s]), false);
									}
									finally
									{
										TMonitor.Exit(FilesToRead);
									}
								}
								else
								{
									ShortMenuLoader.PLogger.LogError("Timed out waiting for mutex to allow entry...");
									cts.Cancel();
									token.ThrowIfCancellationRequested();
								}
							}
						}
						catch
						{
							token.ThrowIfCancellationRequested();

							if (TMonitor.TryEnter(FilesToRead, ShortMenuLoader.TimeoutLimit.Value))
							{
								try
								{
									FilesToRead[s.ToLower()] = null;
								}
								finally
								{
									TMonitor.Exit(FilesToRead);
								}
							}
							else
							{
								ShortMenuLoader.PLogger.LogError("Timed out waiting for mutex to allow entry...");
								cts.Cancel();
								token.ThrowIfCancellationRequested();
							}
						}
					}
					ShortMenuLoader.PLogger.LogDebug($"Menu memory loader is finished @ {ShortMenuLoader.WatchOverall.Elapsed}!");
				}, token);

				while (servant.IsCanceled == false && (servant.IsCompleted == false || FilesToRead.Count > 0))
				{
					if (FilesToRead.Count == 0)
					{
						Thread.Sleep(3);
						continue;
					}

					string strFileName;

					if (servant.IsCompleted)
					{
						strFileName = FilesToRead.FirstOrDefault().Key;
					}
					else if (TMonitor.TryEnter(FilesToRead, ShortMenuLoader.TimeoutLimit.Value))
					{
						try
						{
							strFileName = FilesToRead.FirstOrDefault().Key;
						}
						finally
						{
							TMonitor.Exit(FilesToRead);
						}
					}
					else
					{
						ShortMenuLoader.PLogger.LogWarning("Timed out waiting for mutex to allow entry...");

						_mutexTimeoutCounter += 1;

						if (_mutexTimeoutCounter > 5)
						{
							continue;
						}

						_mutexTimeoutCounter = 0;
						cts.Cancel();
						break;
					}

					var mi2 = new SceneEdit.SMenuItem();
					//SceneEdit.GetMenuItemSetUP causes crash if parallel threaded. Our implementation is thread safe-ish.
					if (GetMenuItemSetUp(mi2, strFileName, out var iconLoad))
					{
						if (iconLoad != null)
						{
							listOfLoads.Add(mi2);
							BackgroundIconLoader.RegisterRidIcon(mi2.m_nMenuFileRID, iconLoad);
							//QuickEdit.FRidsToStubs[mi2.m_nMenuFileRID] = iconLoad;

							//Making it a blank texture makes it act differently. It's a bit weird but this way it can interact with the BackgroundIconLoader.
							//mi2.m_texIcon = Texture2D.whiteTexture;
						}
					}

					if (FilesToRead[strFileName] != null)
					{
						FilesToRead[strFileName].Close();
					}

					if (servant.IsCompleted)
					{
						FilesToRead.Remove(strFileName);
					}
					else if (TMonitor.TryEnter(FilesToRead, ShortMenuLoader.TimeoutLimit.Value))
					{
						try
						{
							FilesToRead.Remove(strFileName);
						}
						finally
						{
							TMonitor.Exit(FilesToRead);
						}
					}
					else
					{
						ShortMenuLoader.PLogger.LogWarning("Timed out waiting for mutex to allow entry...");

						_mutexTimeoutCounter += 1;

						if (_mutexTimeoutCounter > 5)
						{
							continue;
						}

						_mutexTimeoutCounter = 0;
						cts.Cancel();
						break;
					}
				}

				if (servant.IsFaulted)
				{
					ShortMenuLoader.PLogger.LogError("Servant task failed due to an unexpected error!");

					if (servant.Exception != null)
					{
						throw servant.Exception;
					}
				}

				if (token.IsCancellationRequested)
				{
					ShortMenuLoader.PLogger.LogError("A cancellation request was sent out! SceneEditInstance can be due to mutex failures...");

					token.ThrowIfCancellationRequested();
				}

				ShortMenuLoader.PLogger.LogDebug($"Mod Loader is finished in {ShortMenuLoader.WatchOverall.Elapsed}!\n");
			}, token);

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			if (!loaderWorker.IsCompleted || GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return new TimedWaitUntil(() => loaderWorker.IsCompleted && GameMain.Instance.CharacterMgr.IsBusy() == false, 0.5f);
			}

			if (loaderWorker.IsFaulted || loaderWorker.IsCanceled)
			{
				if (loaderWorker.Exception?.InnerException != null)
				{
					ShortMenuLoader.PLogger.LogError(
						$"Worker task failed due to an unexpected error! SceneEditInstance is considered a full failure: {loaderWorker.Exception.InnerException.Message}\n{loaderWorker.Exception.InnerException.StackTrace}\n\nwe will try restarting the load task...");
				}

				yield return new WaitForSecondsRealtime(2);

				ShortMenuLoader.SceneEditInstance.StartCoroutine(GsMenuLoadStart(menuList, menuGroupMemberDic));

				yield break;
			}

			ShortMenuLoader.PLogger.LogDebug($"Worker finished @ {ShortMenuLoader.WatchOverall.Elapsed}...");

			var watch2 = Stopwatch.StartNew();

			foreach (var mi2 in listOfLoads)
			{
				try
				{
					if (ShortMenuLoader.ChangeModPriority.Value)
					{
						if (mi2.m_fPriority <= 0)
						{
							mi2.m_fPriority = 1f;
						}

						mi2.m_fPriority += 10000;
					}

					if (!mi2.m_bMan)
					{
						//AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(ShortMenuLoader.@this, new object[] { mi2 });
						ShortMenuLoader.SceneEditInstance.AddMenuItemToList(mi2);
						menuList.Add(mi2);
						ShortMenuLoader.SceneEditInstance.m_menuRidDic[mi2.m_nMenuFileRID] = mi2;
						//string parentMenuName2 = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(ShortMenuLoader.@this, new object[] { mi2 }) as string;
						var parentMenuName2 = SceneEdit.GetParentMenuFileName(mi2);
						if (!string.IsNullOrEmpty(parentMenuName2))
						{
							var hashCode2 = parentMenuName2.GetHashCode();
							if (!menuGroupMemberDic.ContainsKey(hashCode2))
							{
								menuGroupMemberDic.Add(hashCode2, new List<int>());
							}
							menuGroupMemberDic[hashCode2].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
						}
						else if (mi2.m_strCateName.IndexOf("set_", StringComparison.Ordinal) != -1 && mi2.m_strMenuFileName.IndexOf("_del", StringComparison.Ordinal) == -1)
						{
							mi2.m_bGroupLeader = true;
							mi2.m_listMember = new List<SceneEdit.SMenuItem>
						{
							mi2
						};
						}
					}
				}
				catch (Exception e)
				{
					ShortMenuLoader.PLogger.LogError($"We caught the following exception while processing {mi2.m_strMenuFileName}:\n {e.StackTrace}");
				}
				if (ShortMenuLoader.BreakInterval.Value < Time.realtimeSinceStartup - ShortMenuLoader.Time)
				{
					yield return null;
					ShortMenuLoader.Time = Time.realtimeSinceStartup;
				}
			}

			ShortMenuLoader.PLogger.LogDebug($"Time capped foreach done @ {ShortMenuLoader.WatchOverall.Elapsed}...");

			ShortMenuLoader.ThreadsDone++;
			ShortMenuLoader.PLogger.LogInfo($"Standard mods finished loading at: {ShortMenuLoader.WatchOverall.Elapsed}\n");
			watch2.Reset();
			watch2.Start();

			ShortMenuLoader.SceneEditInstance.StartCoroutine(SaveCache());

			if (listOfDuplicates.Count > 0)
			{
				ShortMenuLoader.PLogger.LogWarning($"There are {listOfDuplicates.Count} duplicate menus in your mod folder!");

				foreach (var s in listOfDuplicates)
				{
					ShortMenuLoader.PLogger.LogWarning("We found a duplicate that should be corrected immediately in your mod folder at: " + s);
				}
			}
		}

		public static bool GetMenuItemSetUp(SceneEdit.SMenuItem mi, string fStrMenuFileName, out string iconTex)
		{
			iconTex = null;

			if (fStrMenuFileName.Contains("_zurashi"))
			{
				return false;
			}
			if (fStrMenuFileName.Contains("_mekure"))
			{
				return false;
			}

			fStrMenuFileName = Path.GetFileName(fStrMenuFileName);
			mi.m_strMenuFileName = fStrMenuFileName;
			mi.m_nMenuFileRID = fStrMenuFileName.ToLower().GetHashCode();

			try
			{
				if (!InitMenuItemScript(mi, fStrMenuFileName, out iconTex))
				{
					ShortMenuLoader.PLogger.LogError("(メニュースクリプトが読めませんでした。) The following menu file could not be read and will be skipped: " + fStrMenuFileName);
				}
			}
			catch (Exception ex)
			{
				ShortMenuLoader.PLogger.LogError(string.Concat("GetMenuItemSetUP tossed an exception while reading: ", fStrMenuFileName, "\n\n", ex.Message, "\n", ex.StackTrace));
				return false;
			}

			return true;
		}

		public static bool InitMenuItemScript(SceneEdit.SMenuItem mi, string fStrMenuFileName, out string iconTex)
		{
			iconTex = null;

			if (fStrMenuFileName.IndexOf("mod_", StringComparison.Ordinal) == 0)
			{
				var modPathFileName = Menu.GetModPathFileName(fStrMenuFileName);
				return !string.IsNullOrEmpty(modPathFileName) && SceneEdit.InitModMenuItemScript(mi, modPathFileName);
			}

			if (_menuCache.ContainsKey(fStrMenuFileName))
			{
				try
				{
					var tempStub = _menuCache[fStrMenuFileName];
					if (tempStub.DateModified == File.GetLastWriteTimeUtc(FilesDictionary[fStrMenuFileName]))
					{
						if (tempStub.Name != null)
						{
							mi.m_strMenuName = tempStub.Name;
						}

						if (tempStub.Description != null)
						{
							mi.m_strInfo = tempStub.Description;
						}

						mi.m_strCateName = Enum.GetName(typeof(MPN), tempStub.Category);
						mi.m_mpn = tempStub.Category;

						mi.m_eColorSetMPN = tempStub.ColorSetMpn;

						if (tempStub.ColorSetMenu != null)
						{
							mi.m_strMenuNameInColorSet = tempStub.ColorSetMenu;
						}

						mi.m_pcMultiColorID = tempStub.MultiColorId;

						mi.m_boDelOnly = tempStub.DelMenu;

						mi.m_fPriority = tempStub.Priority;

						mi.m_bMan = tempStub.ManMenu;

						iconTex = tempStub.Icon;

						if (ShortMenuLoader.PutMenuFileNameInItemDescription.Value && !mi.m_strInfo.Contains($"\n\n{fStrMenuFileName}"))
						{
							mi.m_strInfo += $"\n\n{fStrMenuFileName}";
						}

						return true;
					}

					ShortMenuLoader.PLogger.LogWarning("A cache entry was found outdated. SceneEditInstance should be automatically fixed and the cache reloaded.");
				}
				catch (Exception ex)
				{
					ShortMenuLoader.PLogger.LogError(string.Concat($"Encountered an issue while trying to load menu {fStrMenuFileName} from cache. SceneEditInstance should be automatically fixed and the cache reloaded.", "\n\n", ex.Message, "\n", ex.StackTrace));
				}
			}

			try
			{
				if (FilesToRead[fStrMenuFileName] == null)
				{
					FilesToRead[fStrMenuFileName] = new MemoryStream(File.ReadAllBytes(FilesDictionary[fStrMenuFileName]), false);
				}
			}
			catch (Exception ex)
			{
				ShortMenuLoader.PLogger.LogError(string.Concat("The following menu file could not be read! (メニューファイルがが読み込めませんでした。): ", fStrMenuFileName, "\n\n", ex.Message, "\n", ex.StackTrace));

				return false;
			}

			var text6 = string.Empty;
			var text7 = string.Empty;

			var cacheEntry = new MenuStub();

			try
			{
				cacheEntry.DateModified = File.GetLastWriteTimeUtc(FilesDictionary[fStrMenuFileName]);
				using (var binaryReader = new BinaryReader(FilesToRead[fStrMenuFileName], Encoding.UTF8))
				{
					var text = binaryReader.ReadString();

					if (text != "CM3D2_MENU")
					{
						ShortMenuLoader.PLogger.LogError("ProcScriptBin (例外 : ヘッダーファイルが不正です。) The header indicates a file type that is not a menu file!" + text + " @ " + fStrMenuFileName);

						return false;
					}

					binaryReader.ReadInt32();
					binaryReader.ReadString();
					binaryReader.ReadString();
					binaryReader.ReadString();
					binaryReader.ReadString();
					binaryReader.ReadInt32();
					string text5 = null;

					while (true)
					{
						int num4 = binaryReader.ReadByte();
						text7 = text6;
						text6 = string.Empty;
						if (num4 == 0)
						{
							break;
						}
						for (var i = 0; i < num4; i++)
						{
							text6 = text6 + "\"" + binaryReader.ReadString() + "\" ";
						}
						if (text6 != string.Empty)
						{
							var stringCom = UTY.GetStringCom(text6);
							var stringList = UTY.GetStringList(text6);
							switch (stringCom)
							{
								case "name" when stringList.Length > 1:
									{
										var text8 = stringList[1];
										var text9 = string.Empty;
										var j = 0;
										while (j < text8.Length && text8[j] != '\u3000' && text8[j] != ' ')
										{
											text9 += text8[j];
											j++;
										}
										while (j < text8.Length)
										{
											j++;
										}
										mi.m_strMenuName = text9;
										cacheEntry.Name = mi.m_strMenuName;
										break;
									}
								case "name":
									ShortMenuLoader.PLogger.LogWarning("Menu file has no name and an empty description will be used instead." + " @ " + fStrMenuFileName);

									mi.m_strMenuName = "";
									cacheEntry.Name = mi.m_strMenuName;
									break;

								case "setumei" when stringList.Length > 1:
									mi.m_strInfo = stringList[1];
									mi.m_strInfo = mi.m_strInfo.Replace("《改行》", "\n");
									cacheEntry.Description = mi.m_strInfo;
									break;

								case "setumei":
									ShortMenuLoader.PLogger.LogWarning("Menu file has no description (setumei) and an empty description will be used instead." + " @ " + fStrMenuFileName);

									mi.m_strInfo = "";
									cacheEntry.Description = mi.m_strInfo;
									break;

								case "category" when stringList.Length > 1:
									{
										var strCateName = stringList[1].ToLower();
										mi.m_strCateName = strCateName;
										cacheEntry.Category = (MPN)Enum.Parse(typeof(MPN), mi.m_strCateName);
										try
										{
											mi.m_mpn = (MPN)Enum.Parse(typeof(MPN), mi.m_strCateName);
											cacheEntry.Category = mi.m_mpn;
										}
										catch
										{
											ShortMenuLoader.PLogger.LogWarning("There is no category called (カテゴリがありません。): " + mi.m_strCateName + " @ " + fStrMenuFileName);
											return false;
										}

										break;
									}
								case "category":
									ShortMenuLoader.PLogger.LogWarning("The following menu file has a category parent with no category: " + fStrMenuFileName);
									return false;

								case "color_set" when stringList.Length > 1:
									{
										try
										{
											mi.m_eColorSetMPN = (MPN)Enum.Parse(typeof(MPN), stringList[1].ToLower());
											cacheEntry.ColorSetMpn = mi.m_eColorSetMPN;
										}
										catch
										{
											ShortMenuLoader.PLogger.LogWarning("There is no category called(カテゴリがありません。): " + mi.m_strCateName + " @ " + fStrMenuFileName);

											return false;
										}
										if (stringList.Length >= 3)
										{
											mi.m_strMenuNameInColorSet = stringList[2].ToLower();
											cacheEntry.ColorSetMenu = mi.m_strMenuNameInColorSet;
										}

										break;
									}
								case "color_set":
									ShortMenuLoader.PLogger.LogWarning("A color_set entry exists but is otherwise empty" + " @ " + fStrMenuFileName);
									break;

								case "tex":
								case "テクスチャ変更":
									{
										if (stringList.Length == 6)
										{
											var text10 = stringList[5];
											MaidParts.PARTS_COLOR pcMultiColorId;
											try
											{
												pcMultiColorId = (MaidParts.PARTS_COLOR)Enum.Parse(typeof(MaidParts.PARTS_COLOR), text10.ToUpper());
											}
											catch
											{
												ShortMenuLoader.PLogger.LogError("無限色IDがありません。(The following free color ID does not exist: )" + text10 + " @ " + fStrMenuFileName);

												return false;
											}
											mi.m_pcMultiColorID = pcMultiColorId;
											cacheEntry.MultiColorId = mi.m_pcMultiColorID;
										}

										break;
									}
								case "icon":
								case "icons":
									{
										if (stringList.Length > 1)
										{
											text5 = stringList[1];
										}
										else
										{
											ShortMenuLoader.PLogger.LogError("The following menu file has an icon entry but no field set: " + fStrMenuFileName);

											return false;
										}

										break;
									}
								case "saveitem" when stringList.Length > 1:
									{
										var text11 = stringList[1];
										if (string.IsNullOrEmpty(text11))
										{
											ShortMenuLoader.PLogger.LogWarning("SaveItem is either null or empty." + " @ " + fStrMenuFileName);
										}

										break;
									}
								case "saveitem":
									ShortMenuLoader.PLogger.LogWarning("A saveitem entry exists with nothing set in the field @ " + fStrMenuFileName);
									break;

								case "unsetitem":
									mi.m_boDelOnly = true;
									cacheEntry.DelMenu = mi.m_boDelOnly;
									break;

								case "priority" when stringList.Length > 1:
									mi.m_fPriority = float.Parse(stringList[1]);
									cacheEntry.Priority = mi.m_fPriority;
									break;

								case "priority":
									ShortMenuLoader.PLogger.LogError("The following menu file has a priority entry but no field set. A default value of 10000 will be used: " + fStrMenuFileName);

									mi.m_fPriority = 10000f;
									cacheEntry.Priority = mi.m_fPriority;
									break;

								case "メニューフォルダ" when stringList.Length > 1:
									{
										if (stringList[1].ToLower() == "man")
										{
											mi.m_bMan = true;
											cacheEntry.ManMenu = mi.m_bMan;
										}

										break;
									}
								case "メニューフォルダ":
									ShortMenuLoader.PLogger.LogError("A a menu with a menu folder setting (メニューフォルダ) has an entry but no field set: " + fStrMenuFileName);

									return false;
							}
						}
					}

					if (ShortMenuLoader.PutMenuFileNameInItemDescription.Value && !mi.m_strInfo.Contains($"\n\n{fStrMenuFileName}"))
					{
						mi.m_strInfo += $"\n\n{fStrMenuFileName}";
					}

					if (!string.IsNullOrEmpty(text5))
					{
						try
						{
							iconTex = text5;
							cacheEntry.Icon = text5;
							//mi.m_texIcon = ImportCM.CreateTexture(text5);
						}
						catch (Exception)
						{
							ShortMenuLoader.PLogger.LogError("Error setting some icon tex from a normal mod." + " @ " + fStrMenuFileName);

							return false;
						}
					}
				}
			}
			catch (Exception ex2)
			{
				ShortMenuLoader.PLogger.LogError(string.Concat("Exception when reading: ", fStrMenuFileName, "\nThe line currently being processed, likely the issue (現在処理中だった行): ", text6, "\nPrevious line (以前の行): ", text7, "\n\n", ex2.Message, "\n", ex2.StackTrace));

				return false;
			}

			_menuCache[fStrMenuFileName] = cacheEntry;

			return true;
		}
	}
}