using BepInEx;
using HarmonyLib;
using SceneEditWindow;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Experiment5ParallelMenuLoading
{
	[BepInPlugin("Experiment5ParallelMenuLoading", "Experiment5ParallelMenuLoading", "1.0.0.0")]
	public class Main : BaseUnityPlugin
	{

		public static Harmony harmony;
		public static SceneEdit @this;
		public static Main @this2;
		public static Dictionary<SceneEdit.SMenuItem, string> ListOfIconLoads = new Dictionary<SceneEdit.SMenuItem, string>();
		public static HashSet<SceneEdit.SMenuItem> ListOfContinues = new HashSet<SceneEdit.SMenuItem>();
		private static bool SetupDone = false;
		private static bool IconLoadDone = false;


		/*
		static Dictionary<int, SceneEdit.SMenuItem> menuRidDicThreadSafe;
		static Dictionary<int, HashSet<int>> menuGroupMemberDic;
		static HashSet<SceneEdit.SMenuItem> menuList;*/
		//static float time;

		static Stopwatch test = new Stopwatch();

		void Awake()
		{
			//We set our patcher so we can call it back and patch dynamically as needed.
			harmony = Harmony.CreateAndPatchAll(typeof(Main));
			@this2 = this;
		}

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyPrefix]
		static void GetInstance(ref SceneEdit __instance)
		{
			@this = __instance;
		}

		/*
		[HarmonyPatch(typeof(ImportCM), "CreateTexture")]
		[HarmonyPrefix]
		static bool SmurfCreateTexture1(ref string __0, ref Texture2D __result)
		{
			__result = ImportCM.LoadTexture(GameUty.FileSystem, __0, true).CreateTexture2D();

			return false;
		}

		[HarmonyPatch(typeof(ImportCM), "CreateTexture")]
		[HarmonyPrefix]
		static bool SmurfCreateTexture2(ref AFileSystemBase __0, ref string __1, ref Texture2D __result)
		{
			__result = ImportCM.LoadTexture(__0, __1, true).CreateTexture2D();

			return false;
		}
		*/

		[HarmonyPatch(typeof(SceneEdit), "Start")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler1(IEnumerable<CodeInstruction> instructions)
		{
			var custominstruc = new CodeMatcher(instructions)
			.MatchForward(false,
			new CodeMatch(OpCodes.Ldarg_0),
			new CodeMatch(OpCodes.Ldarg_0),
			new CodeMatch(l => l.opcode == OpCodes.Call && l.Calls(AccessTools.Method(typeof(SceneEdit), "InitMenuNative"))))
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			.SetAndAdvance(OpCodes.Nop, null)
			//.SetAndAdvance(OpCodes.Nop, null)
			.Insert(
			new CodeInstruction(OpCodes.Ldarg_0),
			Transpilers.EmitDelegate<Action>(() =>
			{

				Main.logger.LogInfo("Calling your test coroutine.");

				//InitMenuNativeRe();
				//@this2.StartCoroutine(test2());
				//Task.Factory.StartNew(new Action(() => InitialBackgroundWorker()));
				@this2.StartCoroutine(InitialBackgroundWorker());
				//@this2.StartCoroutine(MenuLoaderWorker());
				//test2();

			}),
			new CodeInstruction(OpCodes.Pop)
			)
			//.Insert(new CodeInstruction (OpCodes.Call, AccessTools.Method(typeof(Main), "InitMenuNativeRe")))
			.InstructionEnumeration();

			return custominstruc;
		}
		public static IEnumerator InitialBackgroundWorker()
		{

			Main.logger.LogError("Coroutine was successfully engaged!");

			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				yield return null;
				//Thread.Sleep(20);
			}
			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;
			while (!menuDataBase.JobFinished())
			{
				//Thread.Sleep(20);
				yield return null;
			}

			test.Start();

			Main.logger.LogError("Reaching first access method.");

			AccessTools.Method(typeof(SceneEdit), "InitCategoryList").Invoke(@this, null);
			//@this.InitCategoryList();
			int fileCount = menuDataBase.GetDataSize();
			var menuList = new HashSet<SceneEdit.SMenuItem>();
			@this.m_menuRidDic = new Dictionary<int, SceneEdit.SMenuItem>(fileCount);
			var menuRidDicThreadSafe = @this.m_menuRidDic;
			var menuGroupMemberDic = new Dictionary<int, HashSet<int>>();
			float time = Time.realtimeSinceStartup;

			for (int i = 0; i < fileCount; i++)
			{
				menuDataBase.SetIndex(i);
				string fileName = menuDataBase.GetMenuFileName();
				string parent_filename = menuDataBase.GetParentMenuFileName();
				if (GameMain.Instance.CharacterMgr.status.IsHavePartsItem(fileName))
				{
					SceneEdit.SMenuItem mi = new SceneEdit.SMenuItem();
					mi.m_strMenuFileName = fileName;
					mi.m_nMenuFileRID = fileName.GetHashCode();
					try
					{
						SceneEdit.ReadMenuItemDataFromNative(mi, i);
					}
					catch (Exception ex)
					{
						Main.logger.LogError(string.Concat(new string[]
						{
						"ReadMenuItemDataFromNative 例外／",
						fileName,
						"／",
						ex.Message,
						" StackTrace／",
						ex.StackTrace
						}));
					}
					if (!mi.m_bMan && @this.editItemTextureCache.IsRegister(mi.m_nMenuFileRID))
					{
						AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { mi });
						//@this.AddMenuItemToList(mi);
						menuList.Add(mi);
						menuRidDicThreadSafe[mi.m_nMenuFileRID] = mi;
						string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi }) as string;
						//string parentMenuName = SceneEdit.GetParentMenuFileName(mi);
						if (!string.IsNullOrEmpty(parentMenuName))
						{
							int hashCode = parentMenuName.GetHashCode();
							if (!menuGroupMemberDic.ContainsKey(hashCode))
							{
								menuGroupMemberDic[hashCode] = new HashSet<int>();
							}
							menuGroupMemberDic[hashCode].Add(mi.m_strMenuFileName.ToLower().GetHashCode());
						}
						else if (mi.m_strCateName.IndexOf("set_") != -1 && mi.m_strMenuFileName.IndexOf("_del") == -1)
						{
							mi.m_bGroupLeader = true;
							mi.m_listMember = new List<SceneEdit.SMenuItem>();
							mi.m_listMember.Add(mi);
						}
						if (0.5f < Time.realtimeSinceStartup - time)
						{
							//Thread.Sleep(20);
							yield return null;
							time = Time.realtimeSinceStartup;
						}
					}
				}
			}

			Main.logger.LogError($"Reaching the load ForEach at {test.Elapsed}.");

			Stopwatch test2 = new Stopwatch();

			foreach (string strFileName in GameUty.ModOnlysMenuFiles)
			{
				test2.Reset();
				test2.Start();

				SceneEdit.SMenuItem mi2 = new SceneEdit.SMenuItem();
				if (Main.GetMenuItemSetUP(mi2, strFileName, out string iconTex, false))
				{
					ListOfIconLoads[mi2] = iconTex;
				}

				Main.logger.LogError("Finished One GetMenuItemSetUP in " + test2.Elapsed);
			}

			Main.logger.LogError($"We've finished SceneEdit.SMenuItem {test.Elapsed}");

			/*
			SetupDone = true;

			while (IconLoadDone == false)
			{
				yield return null;
			}*/

			//foreach (KeyValuePair<SceneEdit.SMenuItem, string> keyPair in ListOfIconLoads)
			while (ListOfIconLoads.Count > 0)
			{
				var keyPair = ListOfIconLoads.FirstOrDefault();

				if (keyPair.Key == null || keyPair.Value == null) 
				{
					ListOfIconLoads.Remove(keyPair.Key);
					continue;
				}

				//Main.logger.LogError("Icon Coroutine is loading an icon...");

				try
				{
					keyPair.Key.m_texIcon = ImportCM.CreateTexture(keyPair.Value);
				}
				catch
				{
					//ListOfIconLoads.Remove(keyPair.Key);
					continue;
				}

				if (!(keyPair.Key.m_texIconRef == null))
				{
					ListOfContinues.Add(keyPair.Key);
				}
				ListOfIconLoads.Remove(keyPair.Key);
			}

			Main.logger.LogError($"Now we've finished loading icons into each menu at {test.Elapsed}.");

			//Parallel.ForEach(ListOfContinues, (mi2) =>
			//foreach (SceneEdit.SMenuItem mi2 in ListOfContinues)
			while (ListOfContinues.Count > 0)
			{

				var mi2 = ListOfContinues.FirstOrDefault();

				if (mi2 == null) 
				{
					continue;
				}

				test2.Reset();

				if (!mi2.m_bMan)
				{
					test2.Start();
					//Main.logger.LogError("Invoking addmenuitemtolist");
					AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { mi2 });
					//@this.AddMenuItemToList(mi2);
					//Main.logger.LogError($"Done invoking AddMenuItemToList at: {test2.Elapsed}");
					menuRidDicThreadSafe[mi2.m_nMenuFileRID] = mi2;
					//Main.logger.LogError("Invoking GetParentMenuFileName");
					string parentMenuName2 = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi2 }) as string;
					//string parentMenuName2 = SceneEdit.GetParentMenuFileName(mi2);
					//Main.logger.LogError($"Done Invoking GetParentMenuFileName at: {test2.Elapsed}");
					if (!string.IsNullOrEmpty(parentMenuName2))
					{
						int hashCode2 = parentMenuName2.GetHashCode();
						if (!menuGroupMemberDic.ContainsKey(hashCode2))
						{
							menuGroupMemberDic[hashCode2] = new HashSet<int>();
						}
						menuGroupMemberDic[hashCode2].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
					}
					else if (mi2.m_strCateName.IndexOf("set_") != -1 && mi2.m_strMenuFileName.IndexOf("_del") == -1)
					{
						mi2.m_bGroupLeader = true;
						mi2.m_listMember = new List<SceneEdit.SMenuItem>();
						mi2.m_listMember.Add(mi2);
					}
					if (0.5f < Time.realtimeSinceStartup - time)
					{
						yield return null;
						Main.logger.LogError($"Sleeping thread, 100ms...");
						time = Time.realtimeSinceStartup;
					}
					Main.logger.LogError($"Finished processing one menu file in {test2.Elapsed}");
				}

				ListOfContinues.Remove(mi2);
			}//);

			Main.logger.LogError($"Finished previous foreach at {test.Elapsed}\nInvoking coroutines...");

			@this.m_menuRidDic = menuRidDicThreadSafe;

			//var localmenuRidDic = @this.m_menuRidDic;

			//var tempMenuList = menuList.ToList();
			var tempGroupMemberDic = menuGroupMemberDic.ToDictionary(t => t.Key, t => t.Value.ToList());

			Main.logger.LogError($"Converted Dictionary back...");

			//yield return @this.StartCoroutine(@this.CoLoadWait());
			yield return @this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "CoLoadWait").Invoke(@this, null) as IEnumerator);

			Main.logger.LogError($"Starting problematic coroutine.");

			//yield return @this.StartCoroutine(@this.FixedInitMenu(menuList, @this.m_menuRidDic, menuGroupMemberDic));
			yield return @this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "FixedInitMenu").Invoke(@this, new object[] { menuList.ToList(), @this.m_menuRidDic, tempGroupMemberDic }) as IEnumerator);

			test.Stop();

			Main.logger.LogError($"Done, loaded {ListOfContinues.Count} menus in {test.Elapsed}");

			ListOfContinues.Clear();

			test.Reset();

			yield break;

		}/*
		public static IEnumerator MenuLoaderWorker()
		{
		}*/
		public static bool GetMenuItemSetUP(SceneEdit.SMenuItem mi, string f_strMenuFileName, out string IconTex, bool f_bMan = false)
		{
			IconTex = null;

			if (f_strMenuFileName.Contains("_zurashi"))
			{
				return false;
			}
			if (f_strMenuFileName.Contains("_mekure"))
			{
				return false;
			}
			f_strMenuFileName = Path.GetFileName(f_strMenuFileName);
			mi.m_strMenuFileName = f_strMenuFileName;
			mi.m_nMenuFileRID = f_strMenuFileName.ToLower().GetHashCode();
			try
			{
				if (!Main.InitMenuItemScript(mi, f_strMenuFileName, f_bMan, out IconTex))
				{
					NDebug.Assert(false, "メニュースクリプトが読めませんでした。" + f_strMenuFileName);
				}
			}
			catch (Exception ex)
			{
				Main.logger.LogError(string.Concat(new string[]
				{
				"GetMenuItemSetUP 例外／",
				f_strMenuFileName,
				"／",
				ex.Message,
				" StackTrace／",
				ex.StackTrace
				}));
				return false;
			}
			return true;
		}
		public static bool InitMenuItemScript(SceneEdit.SMenuItem mi, string f_strMenuFileName, bool f_bMan, out string IconTex)
		{
			var fetchedField = AccessTools.DeclaredField(typeof(SceneEdit), "m_byItemFileBuffer");
			var fetchedVal = fetchedField.GetValue(@this) as byte[];
			IconTex = null;

			if (f_strMenuFileName.IndexOf("mod_") == 0)
			{
				string modPathFileName = Menu.GetModPathFileName(f_strMenuFileName);
				return !string.IsNullOrEmpty(modPathFileName) && SceneEdit.InitModMenuItemScript(mi, modPathFileName);
			}
			try
			{
				using (AFileBase afileBase = GameUty.FileOpen(f_strMenuFileName, null))
				{
					NDebug.Assert(afileBase.IsValid(), "メニューファイルが存在しません。 :" + f_strMenuFileName);
					if (fetchedVal == null)
					{
						fetchedVal = new byte[System.Math.Max(500000, afileBase.GetSize())];
					}
					else if (fetchedVal.Length < afileBase.GetSize())
					{
						fetchedVal = new byte[afileBase.GetSize()];
					}
					afileBase.Read(ref fetchedVal, afileBase.GetSize());
				}
			}
			catch (Exception ex)
			{
				Main.logger.LogError(string.Concat(new string[]
				{
				"メニューファイルがが読み込めませんでした。 : ",
				f_strMenuFileName,
				" : ",
				ex.Message,
				" : StackTrace ：\n",
				ex.StackTrace
				}));
				throw ex;
			}
			BinaryReader binaryReader = new BinaryReader(new MemoryStream(fetchedVal), Encoding.UTF8);
			string text = binaryReader.ReadString();
			NDebug.Assert(text == "CM3D2_MENU", "ProcScriptBin 例外 : ヘッダーファイルが不正です。" + text);
			int num = binaryReader.ReadInt32();
			string path = binaryReader.ReadString();
			string text2 = binaryReader.ReadString();
			string text3 = binaryReader.ReadString();
			string text4 = binaryReader.ReadString();
			long num2 = (long)binaryReader.ReadInt32();
			int num3 = 0;
			string text5 = null;
			string text6 = string.Empty;
			string text7 = string.Empty;
			try
			{
				for (; ; )
				{
					int num4 = (int)binaryReader.ReadByte();
					text7 = text6;
					text6 = string.Empty;
					if (num4 == 0)
					{
						break;
					}
					for (int i = 0; i < num4; i++)
					{
						text6 = text6 + "\"" + binaryReader.ReadString() + "\" ";
					}
					if (!(text6 == string.Empty))
					{
						string stringCom = UTY.GetStringCom(text6);
						string[] stringList = UTY.GetStringList(text6);
						if (stringCom == "name")
						{
							string text8 = stringList[1];
							string text9 = string.Empty;
							string arg = string.Empty;
							int j = 0;
							while (j < text8.Length && text8[j] != '\u3000' && text8[j] != ' ')
							{
								text9 += text8[j];
								j++;
							}
							while (j < text8.Length)
							{
								arg += text8[j];
								j++;
							}
							mi.m_strMenuName = text9;
						}
						else if (stringCom == "setumei")
						{
							mi.m_strInfo = stringList[1];
							mi.m_strInfo = mi.m_strInfo.Replace("《改行》", "\n");
						}
						else if (stringCom == "category")
						{
							string strCateName = stringList[1].ToLower();
							mi.m_strCateName = strCateName;
							try
							{
								mi.m_mpn = (MPN)Enum.Parse(typeof(MPN), mi.m_strCateName);
							}
							catch
							{
								Main.logger.LogWarning("カテゴリがありません。" + mi.m_strCateName);
								mi.m_mpn = MPN.null_mpn;
							}
						}
						else if (stringCom == "color_set")
						{
							try
							{
								mi.m_eColorSetMPN = (MPN)Enum.Parse(typeof(MPN), stringList[1].ToLower());
							}
							catch
							{
								Main.logger.LogWarning("カテゴリがありません。" + mi.m_strCateName);
							}
							if (stringList.Length >= 3)
							{
								mi.m_strMenuNameInColorSet = stringList[2].ToLower();
							}
						}
						else if (stringCom == "tex" || stringCom == "テクスチャ変更")
						{
							MaidParts.PARTS_COLOR pcMultiColorID = MaidParts.PARTS_COLOR.NONE;
							if (stringList.Length == 6)
							{
								string text10 = stringList[5];
								try
								{
									pcMultiColorID = (MaidParts.PARTS_COLOR)Enum.Parse(typeof(MaidParts.PARTS_COLOR), text10.ToUpper());
								}
								catch
								{
									NDebug.Assert("無限色IDがありません。" + text10, false);
								}
								mi.m_pcMultiColorID = pcMultiColorID;
							}
						}
						else if (stringCom == "icon" || stringCom == "icons")
						{
							text5 = stringList[1];
						}
						else if (!(stringCom == "iconl"))
						{
							if (!(stringCom == "setstr"))
							{
								if (!(stringCom == "アイテムパラメータ"))
								{
									if (stringCom == "saveitem")
									{
										string text11 = stringList[1];
										if (text11 == string.Empty)
										{
											Main.logger.LogError("err SaveItem \"" + text11);
										}
										if (text11 == null)
										{
											Main.logger.LogError("err SaveItem null=\"" + text11);
										}
										if (text11 != string.Empty)
										{
										}
									}
									else if (!(stringCom == "catno"))
									{
										if (stringCom == "additem")
										{
											num3++;
										}
										else if (stringCom == "unsetitem")
										{
											mi.m_boDelOnly = true;
										}
										else if (stringCom == "priority")
										{
											mi.m_fPriority = float.Parse(stringList[1]);
										}
										else if (stringCom == "メニューフォルダ" && stringList[1].ToLower() == "man")
										{
											mi.m_bMan = true;
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex2)
			{
				Main.logger.LogError(string.Concat(new string[]
				{
				"Exception ",
				Path.GetFileName(path),
				" 現在処理中だった行 = ",
				text6,
				" 以前の行 = ",
				text7,
				"   ",
				ex2.Message,
				"StackTrace：\n",
				ex2.StackTrace
				}));
				throw ex2;
			}
			if (text5 != null && text5 != string.Empty)
			{
				try
				{
					IconTex = text5;
					//mi.m_texIcon = ImportCM.CreateTexture(text5);
				}
				catch (Exception)
				{
					Main.logger.LogError("Error:");
				}
			}
			binaryReader.Close();
			return true;
		}
	}
}
