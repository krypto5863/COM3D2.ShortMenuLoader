using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using COM3D2API.Utilities;
using UnityEngine;

namespace ShortMenuLoader.Loaders
{
	internal class ModMenuLoad
	{
		//private static Dictionary<string, MemoryStream> modFiles = new Dictionary<string, MemoryStream>();

		public static IEnumerator ModMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			var modIconLoads = new Dictionary<SceneEdit.SMenuItem, byte[]>();

			var loaderWorker = Task.Factory.StartNew(() =>
			{
				//Mutex dicLock = new Mutex();

				/*
				Task servantWorker = Task.Factory.StartNew(new Action(() =>
				{
					foreach (string mod in Directory.GetFiles(path + "\\Mod", "*.mod", SearchOption.AllDirectories))
					{
						try
						{
							if (TMonitor.TryEnter(modFiles, ShortMenuLoader.TimeoutLimit.Value))
							{
								try
								{
									var bytearray = File.ReadAllBytes(mod);
									modFiles[mod] = new MemoryStream(bytearray, 0, bytearray.Length, true, true);
								}
								finally
								{
									TMonitor.Exit(modFiles);
								}
							}
							else
							{
								ShortMenuLoader.PLogger.LogError($"Timed out waiting for mutex to allow entry...");
							}
						}
						catch
						{
							ShortMenuLoader.PLogger.LogError("Couldn't read .mod file at: " + mod);
						}
					}

					ShortMenuLoader.PLogger.LogInfo($"After loading all .mod files, we have allocated: {GC.GetTotalMemory(false) * 0.000001}");
				}));
				*/

				foreach (var mod in ShortMenuLoader.FilesInModFolder.Where(t => t.ToLower().EndsWith(".mod")))
				{
					var mi2 = new SceneEdit.SMenuItem();
					if (InitModMenuItemScript(mi2, mod, out var icon))
					{
						modIconLoads[mi2] = icon;
					}

					//modFiles.Remove(mod);
				}

				/*
				if (servantWorker.IsFaulted)
				{
					ShortMenuLoader.PLogger.LogError($"Servant task failed due to an unexpected error!");

					throw servantWorker.Exception;
				}

				servantWorker.Dispose();
				*/
			});

			if (!loaderWorker.IsCompleted)
			{
				yield return new TimedWaitUntil(() => loaderWorker.IsCompleted, 0.5f);
			}

			if (loaderWorker.IsFaulted)
			{
				ShortMenuLoader.PLogger.LogWarning($"Worker task failed due to an unexpected error! SceneEditInstance is considered a full failure: {loaderWorker.Exception?.InnerException?.Message}\n{loaderWorker.Exception.InnerException.StackTrace}\n\nwe will try restarting the load task...");

				yield return new WaitForSecondsRealtime(2);

				ShortMenuLoader.SceneEditInstance.StartCoroutine(ModMenuLoadStart(menuList, menuGroupMemberDic));

				yield break;
			}

			foreach (var kv in modIconLoads)
			{
				kv.Key.m_texIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				kv.Key.m_texIcon.LoadImage(kv.Value);
			}

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			if (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return new TimedWaitUntil(() => GameMain.Instance.CharacterMgr.IsBusy() == false, 0.5f);
			}

			foreach (var mi2 in modIconLoads.Keys)
			{
				//AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(ShortMenuLoader.@this, new object[] { mi2 });
				ShortMenuLoader.SceneEditInstance.AddMenuItemToList(mi2);
				//this.AddMenuItemToList(mi2);
				menuList.Add(mi2);
				if (!ShortMenuLoader.SceneEditInstance.m_menuRidDic.ContainsKey(mi2.m_nMenuFileRID))
				{
					ShortMenuLoader.SceneEditInstance.m_menuRidDic.Add(mi2.m_nMenuFileRID, mi2);
				}
				else
				{
					ShortMenuLoader.SceneEditInstance.m_menuRidDic[mi2.m_nMenuFileRID] = mi2;
				}
				//string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(ShortMenuLoader.@this, new object[] { mi2 }) as string;
				var parentMenuName = SceneEdit.GetParentMenuFileName(mi2);
				//string parentMenuName = SceneEdit.GetParentMenuFileName(mi2);
				if (!string.IsNullOrEmpty(parentMenuName))
				{
					var hashCode = parentMenuName.GetHashCode();
					if (!menuGroupMemberDic.ContainsKey(hashCode))
					{
						menuGroupMemberDic.Add(hashCode, new List<int>());
					}
					menuGroupMemberDic[hashCode].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
				}
				else if (mi2.m_strCateName.IndexOf("set_", StringComparison.Ordinal) != -1 && mi2.m_strMenuFileName.IndexOf("_del", StringComparison.Ordinal) == -1)
				{
					mi2.m_bGroupLeader = true;
					mi2.m_listMember = new List<SceneEdit.SMenuItem> { mi2 };
				}

				if (ShortMenuLoader.BreakInterval.Value < Time.realtimeSinceStartup - ShortMenuLoader.Time)
				{
					yield return null;
					ShortMenuLoader.Time = Time.realtimeSinceStartup;
				}
			}
			ShortMenuLoader.ThreadsDone++;
			ShortMenuLoader.PLogger.LogInfo($".Mods finished loading at: {ShortMenuLoader.WatchOverall.Elapsed}");
		}

		public static bool InitModMenuItemScript(SceneEdit.SMenuItem mi, string fStrModFileName, out byte[] icon)
		{
			icon = null;

			try
			{
				using (var fileStream = new FileStream(fStrModFileName, FileMode.Open))
				using (var binaryReader = new BinaryReader(fileStream, Encoding.UTF8))
				{
					var text = binaryReader.ReadString();
					if (text != "CM3D2_MOD")
					{
						ShortMenuLoader.PLogger.LogError("InitModMenuItemScript (例外 : ヘッダーファイルが不正です。) The following header for this file indicates that this is not a mod file: " + text + " @ " + fStrModFileName);

						return false;
					}
					binaryReader.ReadInt32();
					var text2 = binaryReader.ReadString();
					binaryReader.ReadString();
					var strMenuName = binaryReader.ReadString();
					var strCateName = binaryReader.ReadString();
					var text4 = binaryReader.ReadString();
					var text5 = binaryReader.ReadString();
					MPN mpn;

					try
					{
						mpn = (MPN)Enum.Parse(typeof(MPN), text5);
					}
					catch
					{
						ShortMenuLoader.PLogger.LogError("(カテゴリがありません。) There is no category called: " + text5 + " @ " + fStrModFileName);

						return false;
					}
					var text6 = string.Empty;

					if (mpn != MPN.null_mpn)
					{
						text6 = binaryReader.ReadString();
					}

					var s = binaryReader.ReadString();
					var num2 = binaryReader.ReadInt32();
					var dictionary = new Dictionary<string, byte[]>();

					for (var i = 0; i < num2; i++)
					{
						var key = binaryReader.ReadString();
						var count = binaryReader.ReadInt32();
						var value = binaryReader.ReadBytes(count);
						dictionary.Add(key, value);
					}

					binaryReader.Close();
					fileStream.Close();

					mi.m_bMod = true;
					mi.m_strMenuFileName = Path.GetFileName(fStrModFileName);
					mi.m_nMenuFileRID = mi.m_strMenuFileName.ToLower().GetHashCode();
					mi.m_strMenuName = strMenuName;
					mi.m_strInfo = text4.Replace("《改行》", "\n");
					mi.m_strCateName = strCateName;

					if (ShortMenuLoader.PutMenuFileNameInItemDescription.Value)
					{
						mi.m_strInfo += $"\n\n{Path.GetFileName(fStrModFileName)}";
					}

					try
					{
						mi.m_mpn = (MPN)Enum.Parse(typeof(MPN), mi.m_strCateName);
					}
					catch
					{
						ShortMenuLoader.PLogger.LogWarning("(カテゴリがありません。) There is no category called: " + mi.m_strCateName + " @ " + fStrModFileName);
						mi.m_mpn = MPN.null_mpn;
					}

					if (mpn != MPN.null_mpn)
					{
						mi.m_eColorSetMPN = mpn;
						if (!string.IsNullOrEmpty(text6))
						{
							mi.m_strMenuNameInColorSet = text6;
						}
					}

					if (!string.IsNullOrEmpty(text2))
					{
						//byte[] data = dictionary[text2];
						icon = dictionary[text2];
						//mi.m_texIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false);
						//mi.m_texIcon.LoadImage(data);
					}

					mi.m_fPriority = 999f;

					using (var stringReader = new StringReader(s))
					{
						string text7;
						while ((text7 = stringReader.ReadLine()) != null)
						{
							var array = text7.Split(new[]
							{
					'\t',
					' '
							}, StringSplitOptions.RemoveEmptyEntries);
							if (array[0] == "テクスチャ変更")
							{
								var pcMultiColorId = MaidParts.PARTS_COLOR.NONE;
								if (array.Length == 6)
								{
									var text8 = array[5];
									try
									{
										pcMultiColorId = (MaidParts.PARTS_COLOR)Enum.Parse(typeof(MaidParts.PARTS_COLOR), text8.ToUpper());
									}
									catch
									{
										ShortMenuLoader.PLogger.LogError("(無限色IDがありません。) There is no infinite color ID called: " + text8 + " @ " + fStrModFileName);
									}
									mi.m_pcMultiColorID = pcMultiColorId;
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ShortMenuLoader.PLogger.LogError("InitModMenuItemScript The following MOD item menu file could not be loaded (MODアイテムメニューファイルが読み込めませんでした。) : " + fStrModFileName + "\n\n" + ex.Message + "\n" + ex.StackTrace);
				return false;
			}
			return true;
		}
	}
}