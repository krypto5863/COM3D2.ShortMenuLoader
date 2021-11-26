using HarmonyLib;
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
	class QuickEditVanilla
	{
		internal static Dictionary<int, string> f_RidsToStubs = new Dictionary<int, string>();
		private static ConcurrentDictionary<string, TextureResource> f_ProcessedTextures = new ConcurrentDictionary<string, TextureResource>();

		private static Dictionary<string, Texture2D> f_LoadedTextures = new Dictionary<string, Texture2D>();

		private static IEnumerator f_TextureLoaderCoroute;
		/*
		internal static void EngageVanillaPreloader()
		{
			if (f_TextureLoaderCoroute == null)
			{
				f_TextureLoaderCoroute = TextureLoader();
				Main.@this.StartCoroutine(f_TextureLoaderCoroute);
			}
		}*/

		//Checks if we have the icon and tells the texter to look no further if we do.
		[HarmonyPatch(typeof(EditItemTextureCache), "GetTexter")]
		[HarmonyPrefix]
		private static bool GetTextureByRid(ref Texture2D __result, int __0)
		{

			if (f_RidsToStubs.ContainsKey(__0))
			{
				__result = GetTexture(__0);

				return false;
			}

			return true;
		}
		//Checks if we have the icon and says yes, look no further.
		[HarmonyPatch(typeof(EditItemTextureCache), "IsRegister")]
		[HarmonyPrefix]
		private static bool IsRegister(ref bool __result, int __0)
		{
			__result = f_RidsToStubs.ContainsKey(__0);

			return !__result;
		}

		/*
		private static IEnumerator TextureLoader()
		{
			var watch1 = Stopwatch.StartNew();

			int MaxThreadsToSpawn = 2;

			Task loaderWorker = Task.Factory.StartNew(new Action(() =>
			{
				int filesLoadedCount = 0;

				while (Main.ThreadsDone < 3)
				{
					Thread.Sleep(3000);
				}

				var watch2 = Stopwatch.StartNew();

				var queue = new ConcurrentQueue<string>(f_RidsToStubs.Values.Where(val => !f_ProcessedTextures.ContainsKey(val) && !f_LoadedTextures.ContainsKey(val)));

				//Main.LockDownForThreading = true;

				Parallel.For(0, queue.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxThreadsToSpawn }, (count, state) =>
				{
					if (queue.Count > 0 && queue.TryDequeue(out var key))
					{
						var loadedTex = ImportCM.LoadTexture(GameUty.FileSystem, key, false);

						if (loadedTex != null)
						{
							++filesLoadedCount;
							f_ProcessedTextures[key] = loadedTex;
						}
					}
					else
					{
						if (Main.ThreadsDone >= 3 && queue.Count <= 0)
						{
							state.Break();
						}
						else if (queue.Count <= 0)
						{
							Thread.Sleep(500);
						}
						return;
					}
				});

				//Main.LockDownForThreading = false;

				watch2.Stop();
				watch1.Stop();

				Main.logger.LogInfo($"Vanilla Icon Preloader Done @ {watch1.Elapsed}\n" +
				$"\nWorked for {watch2.Elapsed}\n" +
				$"In total loaded {filesLoadedCount} vanilla files...\n");
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
		}*/

		//A helper function to everyone else.
		private static Texture2D GetTexture(int menuFileID)
		{
			string textureFileName;

			if (f_RidsToStubs.TryGetValue(menuFileID, out textureFileName)) ;

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

					if (!f_LoadedTextures.ContainsKey(textureFileName) || f_LoadedTextures[textureFileName] == null)
					{
						f_LoadedTextures[textureFileName] = ImportCM.CreateTexture(textureFileName);
					}
				}
			}

			f_ProcessedTextures.TryRemove(textureFileName, out _);
			return f_LoadedTextures[textureFileName];
		}
	}
}