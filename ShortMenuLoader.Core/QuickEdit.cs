using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using TMonitor = System.Threading.Monitor;

namespace ShortMenuLoader
{
	internal static class QuickEdit
	{
		private static long _amountOfDataPreloaded;

		//A cache of files
		private static Dictionary<string, string> _fTexInModFolder;

		//Will be accessed in a concurrent modality frequently. Locking is slow.
		internal static Dictionary<int, string> FRidsToStubs = new Dictionary<int, string>();

		private static readonly ConcurrentDictionary<string, PreLoadTexture> FProcessedTextures = new ConcurrentDictionary<string, PreLoadTexture>();

		//Should only be used in a non-concurrent modality.
		private static readonly Dictionary<string, Texture2D> FLoadedTextures = new Dictionary<string, Texture2D>();

		private static IEnumerator _fTextureLoaderCoRoute;

		private static bool _modDirScanned;

		internal static void EngageModPreLoader()
		{
			if (_fTextureLoaderCoRoute != null)
			{
				return;
			}

			_fTextureLoaderCoRoute = TextureLoader();
			ShortMenuLoader.SceneEditInstance.StartCoroutine(_fTextureLoaderCoRoute);
		}

		//Despite how it may appear, this is used, it's patched manually in ShortMenuLoader.Awake
		private static void MenuItemSet(ref SceneEdit.SMenuItem __1)
		{
			if (!FRidsToStubs.ContainsKey(__1.m_nMenuFileRID))
			{
				return;
			}
			if (__1.m_texIcon != null && __1.m_texIcon != Texture2D.whiteTexture)
			{
				return;
			}

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

		[HarmonyPatch(typeof(RandomPresetCtrl), "GetTextureByRid")]
		[HarmonyPatch(typeof(RandomPresetCtrl), "GetColorTextureByRid")]
		[HarmonyPatch(typeof(CostumePartsEnabledCtrl), "GetTextureByRid")]
		[HarmonyPrefix]
		private static bool GetTextureByRid(ref Texture2D __result, int __2)
		{
			if (!FRidsToStubs.ContainsKey(__2))
			{
				return true;
			}

			__result = GetTexture(__2);
			return false;
		}

		[HarmonyPatch(typeof(SceneEditWindow.CustomViewItem), "UpdateIcon")]
		[HarmonyPrefix]
		private static void UpdateIcon(ref SceneEditWindow.CustomViewItem __instance, Maid __0)
		{
			if (__0 == null)
			{
				__0 = GameMain.Instance.CharacterMgr.GetMaid(0);

				if (__0 == null)
				{
					return;
				}
			}

			var menuItem = __instance.GetMenuItem(__0, __instance.mpn);

			if (menuItem == null || menuItem.m_boDelOnly && __instance.defaultIconTexture != null || menuItem.m_texIcon == null && __instance.defaultIconTexture != null)
			{
				//Do Nothing
			}
			else if (menuItem.m_texIcon == null || menuItem.m_texIcon == Texture2D.whiteTexture)
			{
				if (FRidsToStubs.ContainsKey(menuItem.m_nMenuFileRID))
				{
					menuItem.m_texIcon = GetTexture(menuItem.m_nMenuFileRID);
					menuItem.m_texIconRandomColor = menuItem.m_texIcon;
				}
			}
		}

		//A helper function to everyone else.
		private static Texture2D GetTexture(int menuFileId)
		{
			if (!FRidsToStubs.TryGetValue(menuFileId, out var textureFileName))
			{
				return null;
			}

			if (ShortMenuLoader.UseIconPreloader.Value == false)
			{
				if (_modDirScanned)
				{
					var fetchedResource = LoadTextureFromModFolder(textureFileName);

					if (fetchedResource != null)
					{
						return fetchedResource.CreateTexture2D();
					}
				}

				return ImportCM.CreateTexture(textureFileName);
			}

			//If texture isn't loaded, load it.
			if (!FLoadedTextures.ContainsKey(textureFileName) || FLoadedTextures[textureFileName] == null)
			{
				if (FProcessedTextures.ContainsKey(textureFileName) && FProcessedTextures[textureFileName] != null)
				{
					FLoadedTextures[textureFileName] = FProcessedTextures[textureFileName].CreateTexture2D();
				}
				else
				{
#if DEBUG
					ShortMenuLoader.PLogger.LogWarning($"{textureFileName} wasn't loaded so it had to be loaded in manually...");
#endif

					if (_modDirScanned)
					{
						var fetchedResource = LoadTextureFromModFolder(textureFileName);

						if (fetchedResource != null)
						{
#if DEBUG
							ShortMenuLoader.PLogger.LogWarning($"{textureFileName} Loaded from mod folder...");
#endif
							FLoadedTextures[textureFileName] = fetchedResource.CreateTexture2D();
						}
					}

					if (!FLoadedTextures.ContainsKey(textureFileName) || FLoadedTextures[textureFileName] == null)
					{
#if DEBUG
						ShortMenuLoader.PLogger.LogWarning($"{textureFileName} Isn't in the mod folder, loading from game system...");
#endif

						FLoadedTextures[textureFileName] = ImportCM.CreateTexture(textureFileName);
					}
				}
			}

			FProcessedTextures.TryRemove(textureFileName, out _);
			return FLoadedTextures[textureFileName];
		}

		private static IEnumerator TextureLoader()
		{
			var watch1 = Stopwatch.StartNew();

			var maxThreadsToSpawn = Math.Min(4, (int)(Environment.ProcessorCount * 0.25));

			var loaderWorker = Task.Factory.StartNew(() =>
			{
				if (_fTexInModFolder == null)
				{
					_fTexInModFolder = new Dictionary<string, string>();

					foreach (var s in ShortMenuLoader.FilesInModFolder.Where(t => t.ToLower().EndsWith(".tex")))
					{
						if (!_fTexInModFolder.ContainsKey(Path.GetFileName(s).ToLower()))
						{
							_fTexInModFolder[Path.GetFileName(s).ToLower()] = s;
						}
					}

					_modDirScanned = true;
#if DEBUG
					ShortMenuLoader.PLogger.LogInfo($"Done Scanning Mod Dir @ {watch1.Elapsed}");
#endif
				}

				var filesLoadedCount = 0;

				while (ShortMenuLoader.ThreadsDone < 3)
				{
					Thread.Sleep(1000);
					//yield return null;
				}

				var watch2 = Stopwatch.StartNew();

				var modQueue = new ConcurrentQueue<string>(FRidsToStubs.Values.Where(val => !FProcessedTextures.ContainsKey(val) && !FLoadedTextures.ContainsKey(val)));

				ShortMenuLoader.PLogger.LogInfo($"Starting pre-loader... GC at {GC.GetTotalMemory(false) / 1000000}");

				Parallel.For(0, modQueue.Count, new ParallelOptions { MaxDegreeOfParallelism = maxThreadsToSpawn }, (count, state) =>
				//while(modQueue.Count > 0)
				{
					if (modQueue.Count > 0 && modQueue.TryDequeue(out var key))
					{
						var loadedTex = LoadTextureFromModFolder(key);

						if (loadedTex != null)
						{
							++filesLoadedCount;
							FProcessedTextures[key] = loadedTex;
						}/*
						else
						{
							loadedTex = ImportCM.LoadTexture(GameUty.FileSystem, key, false);

							if (loadedTex != null)
							{
								++filesLoadedCount;
								f_ProcessedTextures[key] = loadedTex;
							}
						}*/
					}
					else
					{
						if (ShortMenuLoader.ThreadsDone >= 3 && modQueue.Count <= 0)
						{
							state.Break();
							//break;
						}
					}
				});

				//ShortMenuLoader.LockDownForThreading = false;
				watch2.Stop();
				watch1.Stop();

				ShortMenuLoader.PLogger.LogInfo($"Mod Icon Preloader Done @ {watch1.Elapsed}\n" +
									 $"\nWorked for {watch2.Elapsed}\n" +
									 $"In total loaded {filesLoadedCount} mod files." +
									 $"{GC.GetTotalMemory(false) * 0.000001} currently in GC. We preloaded {_amountOfDataPreloaded * 0.000001} Mbs");
			});

			if (!loaderWorker.IsCompleted)
			{
				yield return new TimedWaitUntil(() => loaderWorker.IsCompleted, 0.5f);
			}

			if (loaderWorker.IsFaulted)
			{
				ShortMenuLoader.PLogger.LogError("The texture loader thread ran into an issue with the following exception:\n");

				if (loaderWorker.Exception?.InnerException != null)
				{
					throw loaderWorker.Exception.InnerException;
				}
			}

			//QuickEditVanilla.EngageVanillaPreloader();
		}

		public static PreLoadTexture LoadTextureFromModFolder(string fStrFileName)
		{
			if (!_fTexInModFolder.TryGetValue(fStrFileName.ToLower(), out var fullPathToFile))
			{
				//ShortMenuLoader.PLogger.LogWarning($"Couldn't find {f_strFileName} in mods folder...");
				return null;
			}

			try
			{
				using (var fileStream = File.OpenRead(fullPathToFile))
				using (var binaryReader = new BinaryReader(fileStream))
				{
					var text = binaryReader.ReadString();
					if (text != "CM3D2_TEX")
					{
						return null;
					}
					var num = binaryReader.ReadInt32();
					binaryReader.ReadString();

					var width = 0;
					var height = 0;
					var textureFormat = TextureFormat.ARGB32;

					Rect[] array = null;

					if (1010 <= num)
					{
						if (1011 <= num)
						{
							var num2 = binaryReader.ReadInt32();
							if (0 < num2)
							{
								array = new Rect[num2];
								for (var i = 0; i < num2; i++)
								{
									var num3 = binaryReader.ReadSingle();
									var num4 = binaryReader.ReadSingle();
									var num5 = binaryReader.ReadSingle();
									var num6 = binaryReader.ReadSingle();
									array[i] = new Rect(num3, num4, num5, num6);
								}
							}
						}
						width = binaryReader.ReadInt32();
						height = binaryReader.ReadInt32();
						textureFormat = (TextureFormat)binaryReader.ReadInt32();
					}

					var num7 = binaryReader.ReadInt32();

					if (num7 > binaryReader.BaseStream.Length)
					{
						Array.Resize(ref array, 0);
						ShortMenuLoader.PLogger.LogWarning($"{fStrFileName} may be corrupted. The loader will use a white texture instead. Please correct the issue or expect large RAM usage spikes.");
						return PreLoadTexture.WhiteTexture;
					}

					var array2 = new byte[num7];

					if (TMonitor.TryEnter(_amountOfDataPreloaded, ShortMenuLoader.TimeoutLimit.Value))
					{
						try
						{
							_amountOfDataPreloaded += num7;
						}
						finally
						{
							TMonitor.Exit(_amountOfDataPreloaded);
						}
					}

					var actuallyRead = binaryReader.Read(array2, 0, num7);

					if (num == 1000)
					{
						width = array2[16] << 24 | array2[17] << 16 | array2[18] << 8 | array2[19];
						height = array2[20] << 24 | array2[21] << 16 | array2[22] << 8 | array2[23];
					}

					if (num7 > actuallyRead)
					{
						Array.Resize(ref array, 0);
						Array.Resize(ref array2, 0);

						ShortMenuLoader.PLogger.LogWarning($"{fStrFileName} made a larger array than it needed {num7 * 0.000001}MBs. It may be corrupted. Please correct the issue or you may see RAM usage spikes..");
						return PreLoadTexture.WhiteTexture;
					}

					return new PreLoadTexture(width, height, textureFormat, ref array, ref array2, fStrFileName);
				}
			}
			catch
			{
				return null;
			}
		}

		/*
		public static void OverlayIcon(ref Texture2D texture2D)
		{
			Material systemMaterial = GameUty.GetSystemMaterial(GameUty.SystemMaterial.Alpha);

			if (TempRender == null)
			{
				TempRender = new RenderTexture(texture2D.width, texture2D.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
			}
			else
			{
				TempRender.DiscardContents();
			}

			if (ModIconOverlayLoaded == null)
			{
				ModIconOverlayLoaded = new Texture2D(80,80);
				ModIconOverlayLoaded.LoadImage(ModIconOverlay);
			}

			Graphics.Blit(texture2D, TempRender, systemMaterial);
			Graphics.Blit(ModIconOverlayLoaded, TempRender, systemMaterial);
			Graphics.CopyTexture(TempRender, texture2D);
		}
		*/
	}
}