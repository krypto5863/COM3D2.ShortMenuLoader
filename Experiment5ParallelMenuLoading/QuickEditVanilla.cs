using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class QuickEditVanilla
	{
		internal static Dictionary<int, string> FRidsToStubs = new Dictionary<int, string>();
		//private static readonly Dictionary<string, TextureResource> FProcessedTextures = new Dictionary<string, TextureResource>();

		//private static readonly Dictionary<string, Texture2D> FLoadedTextures = new Dictionary<string, Texture2D>();

		/*
		internal static void EngageVanillaPreloader()
		{
			if (f_TextureLoaderCoroute == null)
			{
				f_TextureLoaderCoroute = TextureLoader();
				ShortMenuLoader.@this.StartCoroutine(f_TextureLoaderCoroute);
			}
		}*/

		//Checks if we have the icon and tells the texter to look no further if we do.
		[HarmonyPatch(typeof(EditItemTextureCache), "GetTexter")]
		[HarmonyPrefix]
		private static bool GetTextureByRid(ref Texture2D __result, int __0)
		{
			if (!FRidsToStubs.TryGetValue(__0, out var fileName))
			{
				return true;
			}

			__result = GetTexture(fileName);
			return false;
		}

		//Checks if we have the icon and says yes, look no further.
		[HarmonyPatch(typeof(EditItemTextureCache), "IsRegister")]
		[HarmonyPrefix]
		private static bool IsRegister(ref bool __result, int __0)
		{
			__result = FRidsToStubs.ContainsKey(__0);

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

				while (ShortMenuLoader.ThreadsDone < 3)
				{
					Thread.Sleep(3000);
				}

				var watch2 = Stopwatch.StartNew();

				var queue = new ConcurrentQueue<string>(f_RidsToStubs.Values.Where(val => !f_ProcessedTextures.ContainsKey(val) && !f_LoadedTextures.ContainsKey(val)));

				//ShortMenuLoader.LockDownForThreading = true;

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
						if (ShortMenuLoader.ThreadsDone >= 3 && queue.Count <= 0)
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

				//ShortMenuLoader.LockDownForThreading = false;

				watch2.Stop();
				watch1.Stop();

				ShortMenuLoader.PLogger.LogInfo($"Vanilla Icon Preloader Done @ {watch1.Elapsed}\n" +
				$"\nWorked for {watch2.Elapsed}\n" +
				$"In total loaded {filesLoadedCount} vanilla files...\n");
			}));

			while (!loaderWorker.IsCompleted && !loaderWorker.IsFaulted)
			{
				yield return new WaitForSecondsRealtime(2);
			}

			if (loaderWorker.IsFaulted)
			{
				ShortMenuLoader.PLogger.LogError("The texture loader thread ran into an issue with the following exception:\n");
				throw loaderWorker.Exception.InnerException;
			}
		}*/

		//A helper function to everyone else.
		private static Texture2D GetTexture(string fileName)
		{
			return fileName.IsNullOrWhiteSpace() ? null : ImportCM.CreateTexture(fileName);

			/*
			if (FRidsToStubs.TryGetValue(menuFileId, out var textureFileName) == false)
			{
				return null;
			}

			if (ShortMenuLoader.UseIconPreloader.Value == false)
			{
				var fetchedResource = ImportCM.CreateTexture(textureFileName);

				return fetchedResource;
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

					if (!FLoadedTextures.ContainsKey(textureFileName) || FLoadedTextures[textureFileName] == null)
					{
						FLoadedTextures[textureFileName] = ImportCM.CreateTexture(textureFileName);
					}
				}
			}

			FProcessedTextures.Remove(textureFileName);
			return FLoadedTextures[textureFileName];
			*/
		}
	}
}