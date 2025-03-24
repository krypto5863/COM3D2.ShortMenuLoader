using BepInEx;
using COM3D2API;
using COM3D2API.Utilities;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShortMenuLoader
{
	internal class BackgroundIconLoader
	{
		private static CancellationTokenSource _cancellationTokenSource;

		private static readonly Dictionary<int, string> FRidsToStubs = new Dictionary<int, string>();
		private static readonly BlockingCollection<string> TextureQueue = new BlockingCollection<string>();
		private static readonly ConcurrentDictionary<string, Texture2D> LoadedTextures = new ConcurrentDictionary<string, Texture2D>();

		//private static readonly ConcurrentDictionary<string, Texture2D> Placeholders = new ConcurrentDictionary<string, Texture2D>();
		internal static void EngageVanillaPreloader()
		{
			_cancellationTokenSource?.Cancel();

			RegisterRidIcon(EditItemTextureCache.NoneTexFileName.GetHashCode(), EditItemTextureCache.NoneTexFileName);
#if DEBUG
			ShortMenuLoader.PLogger.LogDebug("Starting BackgroundIconLoader.");
#endif

			var cts = new CancellationTokenSource();
			_cancellationTokenSource = cts;
			var token = cts.Token;

			Task.Run(() => TextureLoader(token));

			SceneManager.sceneUnloaded += SceneManagerOnSceneUnloaded;
		}

		private static void SceneManagerOnSceneUnloaded(Scene arg0)
		{
			if (arg0.name.Equals("SceneEdit") == false)
			{
				return;
			}
			_cancellationTokenSource.Cancel();
		}

		internal static void RegisterRidIcon(int rid, string iconFileName)
		{
			FRidsToStubs[rid] = iconFileName;
		}

		private static void TextureLoader(CancellationToken token)
		{
			try
			{
				foreach (var queuedTexture in TextureQueue.GetConsumingEnumerable(token))
				{
					if (queuedTexture.IsNullOrWhiteSpace())
					{
						return;
					}

					var retryCounter = 0;
				retry:

					try
					{
						var loadedTex = ImportCM.LoadTexture(GameUty.FileSystem, queuedTexture, false);

						if (loadedTex == null && GameUty.IsExistFile(queuedTexture) == false)
						{
							return;
						}

						if (loadedTex?.data?.Length <= 0)
						{
							if (retryCounter++ <= 3)
							{
								ShortMenuLoader.PLogger.LogWarning(
									$"{queuedTexture} returned with no data. Going to retry it...");
								goto retry;
							}

							return;
						}

						UnityMainThreadDispatcher.Instance.EnqueueAsync(() =>
						{
							var texture = loadedTex.CreateTexture2D();

							if (LoadedTextures.TryGetValue(queuedTexture, out var placeholder))
							{
								placeholder.Reinitialize(texture.width, texture.height, texture.format, texture.mipmapCount > 1);
								placeholder.LoadRawTextureData(texture.GetRawTextureData());
								placeholder.Apply(true);
								texture = placeholder;
							}

							if (texture.width != 80 || texture.height != 80)
							{
								texture.ResizeTexture(80, 80, loadedTex.format, false);
							}

							LoadedTextures[queuedTexture] = texture;
						});
					}
					catch (Exception ex)
					{
						ShortMenuLoader.PLogger.LogError(
							$"An exception was caught while trying to load {queuedTexture}!\n" +
							ex);
					}
				}
			}
			catch (OperationCanceledException ex)
			{
			}
			catch (Exception e)
			{
				ShortMenuLoader.PLogger.LogError(e);
				throw;
			}

#if DEBUG
			ShortMenuLoader.PLogger.LogDebug($"Texture loader has finished.");
#endif
		}


		//A helper function to everyone else.
		private static Texture2D GetTexture(string fileName)
		{
			if (fileName.IsNullOrWhiteSpace())
			{
				return null;
			}

			var result = LoadedTextures.GetValueOrDefault(fileName);

			if (result != null)
			{
				return result;
			}

			result = new Texture2D(80, 80);
			LoadedTextures[fileName] = result;
			TextureQueue.Add(fileName);

			return result;
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
	}
}