using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using COM3D2API.Utilities;
using HarmonyLib;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class BackgroundIconLoader
	{
		private static CancellationTokenSource _tokenSource;

		private static readonly LinkedList<string> PriorityQueue = new LinkedList<string>();

		private static readonly Dictionary<int, string> FRidsToStubs = new Dictionary<int, string>();
		private static readonly ConcurrentDictionary<string, Texture2D> LoadedTextures = new ConcurrentDictionary<string, Texture2D>();
		private static readonly ConcurrentDictionary<string, Texture2D> Placeholders = new ConcurrentDictionary<string, Texture2D>();
		internal static void EngageVanillaPreloader()
		{
			_tokenSource?.Cancel();

			var newTokenSource = new CancellationTokenSource();

			var collection = new LinkedList<string>(FRidsToStubs
				.Values
				.Distinct()
				.Where(m => LoadedTextures.ContainsKey(m) == false));

			Task.Run(async () => await TextureLoader(collection, newTokenSource.Token), newTokenSource.Token);

			_tokenSource = newTokenSource;
		}

		internal static void RegisterRidIcon(int rid, string iconFileName)
		{
			FRidsToStubs[rid] = iconFileName;
		}

		//Checks if we have the icon and tells the texter to look no further if we do.
		[HarmonyPatch(typeof(EditItemTextureCache), nameof(EditItemTextureCache.GetTexter))]
		[HarmonyPrefix]
		private static bool GetVanillaTextureByRid(ref Texture2D __result, int __0)
		{
			if (!FRidsToStubs.TryGetValue(__0, out var fileName))
			{
				return true;
			}

			__result = GetTexture(fileName);
			return false;
		}

		//Checks if we have the icon and says yes, look no further.
		[HarmonyPatch(typeof(EditItemTextureCache), nameof(EditItemTextureCache.IsRegister))]
		[HarmonyPrefix]
		private static bool IsRegister(ref bool __result, int __0)
		{
			__result = FRidsToStubs.ContainsKey(__0);

			return !__result;
		}
		private static async Task TextureLoader(LinkedList<string> texturesToLoad, CancellationToken token)
		{
			var watch1 = Stopwatch.StartNew();

			while (texturesToLoad.Count > 1)
			{
				string textureName;

				lock (PriorityQueue)
				{
					if (PriorityQueue.Count > 0)
					{
						textureName = PriorityQueue.First.Value;
						PriorityQueue.RemoveFirst();
						texturesToLoad.Remove(textureName);
					}
					else
					{
						textureName = texturesToLoad.First.Value;
						texturesToLoad.RemoveFirst();
					}
				}

				var loadedTex = ImportCM.LoadTexture(GameUty.FileSystem, textureName, true);

				if (loadedTex == null)
				{
					return;
				}

				LoadedTextures[textureName] = await AsyncAlternatives.CreateTexture2DAsync(loadedTex.data, loadedTex.height, loadedTex.width, loadedTex.format, Placeholders.GetValueOrDefault(textureName));

				if (token.IsCancellationRequested)
				{
					return;
				}
			}

			lock (Placeholders)
			{
				Placeholders.Clear();
			}

			lock (PriorityQueue)
			{
				PriorityQueue.Clear();
			}

			ShortMenuLoader.PLogger.LogInfo($"An Icon Preloader Done @ {watch1.Elapsed}\n" +
											$"In total loaded {LoadedTextures.Count} vanilla icons...\n");
			/*
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
			*/
		}

		//A helper function to everyone else.
		private static Texture2D GetTexture(string fileName)
		{
			if (fileName.IsNullOrWhiteSpace())
			{
				return null;
			}

			var result = LoadedTextures.GetValueOrDefault(fileName) ?? Placeholders.GetValueOrDefault(fileName);

			if (result == null)
			{
				result = new Texture2D(80, 80);
				Placeholders[fileName] = result;
				lock (PriorityQueue)
				{
					PriorityQueue.Remove(fileName);
					PriorityQueue.AddLast(fileName);
				}
			}

			return result;
		}
	}
}