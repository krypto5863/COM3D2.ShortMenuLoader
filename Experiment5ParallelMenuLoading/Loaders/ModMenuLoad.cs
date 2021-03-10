using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShortMenuLoader
{
	internal class ModMenuLoad
	{
		private static readonly Dictionary<string, MemoryStream> modFiles = new Dictionary<string, MemoryStream>();

		public static IEnumerator ModMenuLoadStart(List<SceneEdit.SMenuItem> menuList, Dictionary<int, List<int>> menuGroupMemberDic)
		{
			Dictionary<SceneEdit.SMenuItem, byte[]> modIconLoads = new Dictionary<SceneEdit.SMenuItem, byte[]>();

			Task loaderWorker = Task.Factory.StartNew(new Action(() =>
			{

				Mutex dicLock = new Mutex();

				Task servantWorker = Task.Factory.StartNew(new Action(() =>
				{
					foreach (string mod in Menu.GetModFiles())
					{
						try
						{
							dicLock.WaitOne();
							modFiles[mod] = new MemoryStream(File.ReadAllBytes(mod));
							dicLock.ReleaseMutex();
						}
						catch
						{
							Debug.LogError("Couldn't read .mod file at: " + mod);
						}
					}
				}));

				while (!servantWorker.IsCompleted || modFiles.Count > 0)
				{
					if (modFiles.Count == 0)
					{
						Thread.Sleep(1);
						continue;
					}

					string strFileName = modFiles.First().Key;

					SceneEdit.SMenuItem mi2 = new SceneEdit.SMenuItem();
					if (ModMenuLoad.InitModMenuItemScript(mi2, strFileName, out byte[] icon))
					{
						modIconLoads[mi2] = icon;
					}

					dicLock.WaitOne();
					modFiles.Remove(strFileName);
					dicLock.ReleaseMutex();
				}
			}));

			while (!loaderWorker.IsCompleted)
			{
				yield return null;
			}

			foreach (KeyValuePair<SceneEdit.SMenuItem, byte[]> kv in modIconLoads)
			{
				kv.Key.m_texIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				kv.Key.m_texIcon.LoadImage(kv.Value);
			}

			//We wait until the manager is not busy because starting work while the manager is busy causes egregious bugs.
			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
			}

			foreach (SceneEdit.SMenuItem mi2 in modIconLoads.Keys)
			{
				AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(Main.@this, new object[] { mi2 });
				//this.AddMenuItemToList(mi2);
				menuList.Add(mi2);
				if (!Main.@this.m_menuRidDic.ContainsKey(mi2.m_nMenuFileRID))
				{
					Main.@this.m_menuRidDic.Add(mi2.m_nMenuFileRID, mi2);
				}
				else
				{
					Main.@this.m_menuRidDic[mi2.m_nMenuFileRID] = mi2;
				}
				string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(Main.@this, new object[] { mi2 }) as string;
				//string parentMenuName = SceneEdit.GetParentMenuFileName(mi2);
				if (!string.IsNullOrEmpty(parentMenuName))
				{
					int hashCode = parentMenuName.GetHashCode();
					if (!menuGroupMemberDic.ContainsKey(hashCode))
					{
						menuGroupMemberDic.Add(hashCode, new List<int>());
					}
					menuGroupMemberDic[hashCode].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
				}
				else if (mi2.m_strCateName.IndexOf("set_") != -1 && mi2.m_strMenuFileName.IndexOf("_del") == -1)
				{
					mi2.m_bGroupLeader = true;
					mi2.m_listMember = new List<SceneEdit.SMenuItem>();
					mi2.m_listMember.Add(mi2);
				}
				if (0.5f < Time.realtimeSinceStartup - Main.time)
				{
					yield return null;
					Main.time = Time.realtimeSinceStartup;
				}
			}
			Main.ThreadsDone++;
			Debug.Log($".Mods finished loading at: {Main.WatchOverall.Elapsed}");
		}
		public static bool InitModMenuItemScript(SceneEdit.SMenuItem mi, string f_strModFileName, out byte[] Icon)
		{
			Icon = null;

			try
			{
				if (modFiles[f_strModFileName] == null)
				{
					modFiles[f_strModFileName] = new MemoryStream(File.ReadAllBytes(f_strModFileName));
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("InitModMenuItemScript MODアイテムメニューファイルが読み込めませんでした。 : " + f_strModFileName + " : " + ex.Message);
				return false;
			}

			BinaryReader binaryReader = new BinaryReader(modFiles[f_strModFileName], Encoding.UTF8);
			string text = binaryReader.ReadString();
			NDebug.Assert(text == "CM3D2_MOD", "InitModMenuItemScript 例外 : ヘッダーファイルが不正です。" + text);
			binaryReader.ReadInt32();
			string text2 = binaryReader.ReadString();
			binaryReader.ReadString();
			string strMenuName = binaryReader.ReadString();
			string strCateName = binaryReader.ReadString();
			string text4 = binaryReader.ReadString();
			string text5 = binaryReader.ReadString();
			MPN mpn = MPN.null_mpn;

			try
			{
				mpn = (MPN)Enum.Parse(typeof(MPN), text5);
			}
			catch
			{
				NDebug.Assert("カテゴリがありません。" + text5, false);
			}
			string text6 = string.Empty;

			if (mpn != MPN.null_mpn)
			{
				text6 = binaryReader.ReadString();
			}

			string s = binaryReader.ReadString();
			int num2 = binaryReader.ReadInt32();
			Dictionary<string, byte[]> dictionary = new Dictionary<string, byte[]>();

			for (int i = 0; i < num2; i++)
			{
				string key = binaryReader.ReadString();
				int count = binaryReader.ReadInt32();
				byte[] value = binaryReader.ReadBytes(count);
				dictionary.Add(key, value);
			}

			binaryReader.Close();
			binaryReader = null;
			mi.m_bMod = true;
			mi.m_strMenuFileName = Path.GetFileName(f_strModFileName);
			mi.m_nMenuFileRID = mi.m_strMenuFileName.ToLower().GetHashCode();
			mi.m_strMenuName = strMenuName;
			mi.m_strInfo = text4.Replace("《改行》", "\n");
			mi.m_strCateName = strCateName;

			try
			{
				mi.m_mpn = (MPN)Enum.Parse(typeof(MPN), mi.m_strCateName);
			}
			catch
			{
				Debug.LogWarning("カテゴリがありません。" + mi.m_strCateName);
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
				Icon = dictionary[text2];
				//mi.m_texIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				//mi.m_texIcon.LoadImage(data);
			}

			mi.m_fPriority = 999f;

			using (StringReader stringReader = new StringReader(s))
			{
				string empty = string.Empty;
				string text7;
				while ((text7 = stringReader.ReadLine()) != null)
				{
					string[] array = text7.Split(new char[]
					{
					'\t',
					' '
					}, StringSplitOptions.RemoveEmptyEntries);
					if (array[0] == "テクスチャ変更")
					{
						MaidParts.PARTS_COLOR pcMultiColorID = MaidParts.PARTS_COLOR.NONE;
						if (array.Length == 6)
						{
							string text8 = array[5];
							try
							{
								pcMultiColorID = (MaidParts.PARTS_COLOR)Enum.Parse(typeof(MaidParts.PARTS_COLOR), text8.ToUpper());
							}
							catch
							{
								NDebug.Assert("無限色IDがありません。" + text8, false);
							}
							mi.m_pcMultiColorID = pcMultiColorID;
						}
					}
				}
			}
			return true;
		}
	}
}
