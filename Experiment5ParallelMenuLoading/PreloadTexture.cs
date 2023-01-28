using System;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class PreLoadTexture
	{
		public static PreLoadTexture WhiteTexture = new PreLoadTexture(Texture2D.whiteTexture);

		private readonly int _width;

		private readonly int _height;

		private readonly TextureFormat _format;

		private Rect[] _uvRects;

		private byte[] _data;

		private Texture2D _texture;

		public string TextureName { get; } = string.Empty;

		private PreLoadTexture(Texture2D tex)
		{
			_texture = tex;
		}

		public PreLoadTexture(int width, int height, TextureFormat format, ref Rect[] uvRects, ref byte[] data, string texName = null)
		{
			_width = width;
			_height = height;
			_format = format;
			TextureName = texName ?? string.Empty;
			if (uvRects != null && 0 < uvRects.Length)
			{
				_uvRects = uvRects;
			}

			_data = data;
		}

		public Texture2D CreateTexture2D()
		{
			if (_texture != null)
			{
				return _texture;
			}

			try
			{
				_texture = new Texture2D(_width, _height, _format, false);
				_texture.LoadImage(_data);
			}
			catch (Exception ex)
			{
				Main.PLogger.LogError($"Failed to create texture {TextureName} with an issue of: {ex.Message}\n" +
									  $"Image Params: {_width}x{_height} {_format.ToString()}\n" +
									  "We will return a blank texture as a placeholder. Please correct the file as it may have an issue...");
				_texture = Texture2D.whiteTexture;
			}

			Array.Resize(ref _data, 0);
			Array.Resize(ref _uvRects, 0);

			_data = null;
			_uvRects = null;

			return _texture;
		}
	}
}