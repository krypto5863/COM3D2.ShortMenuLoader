using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ShortMenuLoader
{
	internal static class QuickEdit
	{
		private static RenderTexture TempRender;
		private static readonly byte[] ModIconOverlay = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAYAAACOEfKtAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAGFSURBVHhe7dixLgRRFMbxM4u1sxoKpQegUHoClVeQSDSEQiLZQqXwChoiKJQ8gZonoPAAEgUhoRhWdse5c + 7KZPcOYjQy/18ye+49O9WXO3NvRgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIB/IpLJNPXjv3EfRX5UCTVf8UsEWBIBljQYYNoV6dz6SYHO3ff3VMRggC9LIo9Tek37Rp/us8jTrN3zduqb1RV4hN+tdG50eGHjvNcjXaUPfoIv3oEjGtaxH+e4AJvbfoLiAJtbFmCa+IZqn+vKvBKJ130DgQDdObgpMrajVc/YyX7WzSQHIqPLfgInvAJrE1brC7oKD23sdt32mWbbsjky4QCjcavxpgZ3rZvJpQa5JzI8p9eM/YdMQYC9FTgvMqSBucc42dVAN6yPTwUB5r4HNFb0vHdiB+zGom+ip2ATyYnX9Ec3lXjV5hk94iAz+DkrbVuN6lYd18vPnVDPqfznLBdKfzChoEK9Cgq/A/FjBFgSAZYi8gHGBEuPVABsTQAAAABJRU5ErkJggg==");
		private static Texture2D ModIconOverlayLoaded;

		//A cache of files 
		private static Dictionary<string, string> f_TexInModFolder = null;

		//Will be accessed in a concurrent modality frequently. Locking is slow.
		internal static Dictionary<int, string> f_RidsToStubs = new Dictionary<int, string>();
		private static readonly ConcurrentDictionary<string, TextureResource> f_ProcessedTextures = new ConcurrentDictionary<string, TextureResource>();

		//Should only be used in a non-concurrent modality.
		private static Dictionary<string, Texture2D> f_LoadedTextures = new Dictionary<string, Texture2D>();

		private static IEnumerator f_TextureLoaderCoroute;

		private static bool ModDirScanned = false;

		internal static void EngageModPreloader()
		{
			if (f_TextureLoaderCoroute == null)
			{
				f_TextureLoaderCoroute = TextureLoader();
				Main.@this.StartCoroutine(f_TextureLoaderCoroute);
			}
		}
		//Despite how it may appear, this is used, it's patched manually in Main.Awake
		private static void MenuItemSet(ref SceneEdit.SMenuItem __1)
		{
			if (!f_RidsToStubs.ContainsKey(__1.m_nMenuFileRID))
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

		[HarmonyPatch(typeof(RandomPresetCtrl), "GetTextureByRid")]
		[HarmonyPatch(typeof(RandomPresetCtrl), "GetColorTextureByRid")]
		[HarmonyPatch(typeof(CostumePartsEnabledCtrl), "GetTextureByRid")]
		[HarmonyPrefix]
		private static bool GetTextureByRid(ref Texture2D __result, int __2)
		{
			if (f_RidsToStubs.ContainsKey(__2))
			{
				__result = GetTexture(__2);

				return false;
			}

			return true;
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

			SceneEdit.SMenuItem menuItem = __instance.GetMenuItem(__0, __instance.mpn);

			if (menuItem == null || menuItem.m_boDelOnly && __instance.defaultIconTexture != null || menuItem.m_texIcon == null && __instance.defaultIconTexture != null)
			{
				//Do Nothing
			}
			else if (menuItem.m_texIcon == null || menuItem.m_texIcon == Texture2D.whiteTexture)
			{
				if (f_RidsToStubs.ContainsKey(menuItem.m_nMenuFileRID)) 
				{
					menuItem.m_texIcon = GetTexture(menuItem.m_nMenuFileRID);
					menuItem.m_texIconRandomColor = menuItem.m_texIcon;
				} 
			}
		}

		//A helper function to everyone else.
		private static Texture2D GetTexture(int menuFileID)
		{
			string textureFileName;

			if (!f_RidsToStubs.TryGetValue(menuFileID, out textureFileName))
			{
				return null;
			}

			//If texture isn't loaded, load it.
			if (!f_LoadedTextures.ContainsKey(textureFileName) || f_LoadedTextures[textureFileName] == null)
			{
				if (f_ProcessedTextures.ContainsKey(textureFileName) && f_ProcessedTextures[textureFileName] != null)
				{
					f_LoadedTextures[textureFileName] = f_ProcessedTextures[textureFileName].CreateTexture2D();
				}
				else
				{
#if DEBUG
					Main.logger.LogWarning($"{textureFileName} wasn't loaded so it had to be loaded in manually...");
#endif

					if (ModDirScanned)
					{
						var fetchedResource = LoadTextureFromModFolder(textureFileName);

						if (fetchedResource != null)
						{
#if DEBUG
							Main.logger.LogWarning($"{textureFileName} Loaded from mod folder...");
#endif
							f_LoadedTextures[textureFileName] = fetchedResource.CreateTexture2D();
						}
					}

					if (!f_LoadedTextures.ContainsKey(textureFileName) || f_LoadedTextures[textureFileName] == null)
					{
#if DEBUG
						Main.logger.LogWarning($"{textureFileName} Isn't in the mod folder, loading from game system...");
#endif

						f_LoadedTextures[textureFileName] = ImportCM.CreateTexture(textureFileName);
					}
				}

				/*
				if (f_LoadedTextures.TryGetValue(textureFileName, out var texture2D)) 
				{
					OverlayIcon(ref texture2D);
				}*/
			}

			f_ProcessedTextures.TryRemove(textureFileName, out _);
			return f_LoadedTextures[textureFileName];
		}

		private static IEnumerator TextureLoader()
		{
			var watch1 = Stopwatch.StartNew();

			int MaxThreadsToSpawn = Math.Min(4, (int)(Environment.ProcessorCount * 0.25));

			Task loaderWorker = Task.Factory.StartNew(new Action(() =>
			{
				if (f_TexInModFolder == null)
				{
					f_TexInModFolder = new Dictionary<string, string>();

					foreach (string s in Directory.GetFiles(BepInEx.Paths.GameRootPath + "\\Mod", "*.tex", SearchOption.AllDirectories))
					{
						if (!f_TexInModFolder.ContainsKey(Path.GetFileName(s).ToLower()))
						{
							f_TexInModFolder[Path.GetFileName(s).ToLower()] = s;
						}
					}

					ModDirScanned = true;
#if DEBUG
					Main.logger.LogInfo($"Done Scanning Mod Dir @ {watch1.Elapsed}");
#endif
				}

				int filesLoadedCount = 0;

				while (Main.ThreadsDone < 3)
				{
					Thread.Sleep(3000);
				}

				var watch2 = Stopwatch.StartNew();

				var modQueue = new ConcurrentQueue<string>(f_RidsToStubs.Values.Where(val =>  !f_ProcessedTextures.ContainsKey(val) && !f_LoadedTextures.ContainsKey(val)));

				Parallel.For(0, modQueue.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxThreadsToSpawn }, (count, state) =>
				{
					if (modQueue.Count > 0 && modQueue.TryDequeue(out var key))
					{
						var loadedTex = LoadTextureFromModFolder(key);

						if (loadedTex != null)
						{
							++filesLoadedCount;
							f_ProcessedTextures[key] = loadedTex;
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
						if (Main.ThreadsDone >= 3 && modQueue.Count <= 0)
						{
							state.Break();
						}

						return;
					}
				});

				//Main.LockDownForThreading = false;

				watch2.Stop();
				watch1.Stop();

				Main.logger.LogInfo($"Mod Icon Preloader Done @ {watch1.Elapsed}\n" +
				$"\nWorked for {watch2.Elapsed}\n" +
				$"In total loaded {filesLoadedCount} mod files...\n");
			}));

			while (!loaderWorker.IsCompleted && !loaderWorker.IsFaulted)
			{
				yield return new WaitForSecondsRealtime(2);
			}

			if (loaderWorker.IsFaulted)
			{
				Main.logger.LogError("The texture loader thread ran into an issue with the following exception:\n");
				throw loaderWorker.Exception.InnerException;
			}

			//QuickEditVanilla.EngageVanillaPreloader();
		}

		public static TextureResource LoadTextureFromModFolder(string f_strFileName)
		{
			if (!f_TexInModFolder.TryGetValue(f_strFileName.ToLower(), out var fullPathToFile))
			{
				//Main.logger.LogWarning($"Couldn't find {f_strFileName} in mods folder...");
				return null;
			}

			try
			{
				BinaryReader binaryReader = new BinaryReader(new FileStream(fullPathToFile, FileMode.Open), Encoding.UTF8);

				string text = binaryReader.ReadString();
				if (text != "CM3D2_TEX")
				{
					return null;
				}
				int num = binaryReader.ReadInt32();
				string text2 = binaryReader.ReadString();
				int width = 0;
				int height = 0;
				TextureFormat textureFormat = (TextureFormat)5;
				Rect[] array = null;
				if (1010 <= num)
				{
					if (1011 <= num)
					{
						int num2 = binaryReader.ReadInt32();
						if (0 < num2)
						{
							array = new Rect[num2];
							for (int i = 0; i < num2; i++)
							{
								float num3 = binaryReader.ReadSingle();
								float num4 = binaryReader.ReadSingle();
								float num5 = binaryReader.ReadSingle();
								float num6 = binaryReader.ReadSingle();
								array[i] = new Rect(num3, num4, num5, num6);
							}
						}
					}
					width = binaryReader.ReadInt32();
					height = binaryReader.ReadInt32();
					textureFormat = (TextureFormat)binaryReader.ReadInt32();
				}

				int num7 = binaryReader.ReadInt32();
				byte[] array2;

				array2 = new byte[num7];
				binaryReader.Read(array2, 0, num7);

				if (num == 1000)
				{
					width = ((int)array2[16] << 24 | (int)array2[17] << 16 | (int)array2[18] << 8 | (int)array2[19]);
					height = ((int)array2[20] << 24 | (int)array2[21] << 16 | (int)array2[22] << 8 | (int)array2[23]);
				}

				binaryReader.Close();

				return new TextureResource(width, height, textureFormat, array, array2);
			}
			catch
			{
				return null;
			}
		}

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
	}
}
