using System;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class PreloadTexture
	{
		public static PreloadTexture WhiteTexture = new PreloadTexture(Texture2D.whiteTexture);

		private readonly int width;

		private readonly int height;

		private readonly TextureFormat format;

		private Rect[] uvRects = null;

		private byte[] data = null;

		private Texture2D Texture = null;

		public string TextureName { get; private set; } = string.Empty;

		private PreloadTexture(Texture2D tex)
		{
			Texture = tex;
		}

		public PreloadTexture(int width, int height, TextureFormat format, ref Rect[] uvRects, ref byte[] data, string texName = null)
		{
			this.width = width;
			this.height = height;
			this.format = format;
			this.TextureName = texName ?? string.Empty;
			if (uvRects != null && 0 < uvRects.Length)
			{
				this.uvRects = uvRects;
			}

			this.data = data;
		}

		public Texture2D CreateTexture2D()
		{
			if (Texture == null)
			{
				try
				{
					Texture = new Texture2D(width, height, format, false);
					Texture.LoadImage(data);
				}
				catch (Exception ex)
				{
					Main.logger.LogError($"Failed to create texture {TextureName} with an issue of: {ex.Message}\n" +
						$"Image Params: {width}x{height} {format.ToString()}\n" +
						$"We will return a blank texture as a placeholder. Please correct the file as it may have an issue...");
					Texture = Texture2D.whiteTexture;
				}

				Array.Resize(ref data, 0);
				Array.Resize(ref uvRects, 0);

				data = null;
				uvRects = null;
			}

			return Texture;
		}
	}
}