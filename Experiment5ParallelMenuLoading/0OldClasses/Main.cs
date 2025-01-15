using BepInEx;
using HarmonyLib;
using SceneEditWindow;
using System;
using System.Collections;
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
		public static Dictionary<SceneEdit.SMenuItem, string> ListOfContinues = new Dictionary<SceneEdit.SMenuItem, string>();
		static List<SceneEdit.SMenuItem> SavedmenuList;
		static Dictionary<int, List<int>> SavedmenuGroupMemberDic;
		private static bool Test2Done = false;
		private static bool CoRouteDone = false;
		private static bool Test2ContinuatonDone = false;
		private static Stopwatch test = new Stopwatch();

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

				ListOfContinues = new Dictionary<SceneEdit.SMenuItem, string>();
				Test2Done = false;
				CoRouteDone = false;
				Test2ContinuatonDone = false;

				//InitMenuNativeRe();
				//@this2.StartCoroutine(test2());
				@this.StartCoroutine(SetupTexturesCoRoutine());
				Task.Factory.StartNew(new Action(() => test2()));
				//Task.Factory.StartNew(new Action(() => Test2Continuation()));
			}),
			new CodeInstruction(OpCodes.Pop)
			)
			//.Insert(new CodeInstruction (OpCodes.Call, AccessTools.Method(typeof(Main), "InitMenuNativeRe")))
			.InstructionEnumeration();

			return custominstruc;
		}
		public static void test2()
		{
			Main.logger.LogError("Coroutine was successfully engaged!");

			while (GameMain.Instance.CharacterMgr.IsBusy())
			{
				Thread.Sleep(20);
				//yield return null;
			}
			MenuDataBase menuDataBase = GameMain.Instance.MenuDataBase;
			while (!menuDataBase.JobFinished())
			{
				Thread.Sleep(20);
				//yield return null;
			}

			test.Start();

			Main.logger.LogError("Reaching first access method.");

			AccessTools.Method(typeof(SceneEdit), "InitCategoryList").Invoke(@this, null);
			//@this.InitCategoryList();
			int fileCount = menuDataBase.GetDataSize();
			List<SceneEdit.SMenuItem> menuList = new List<SceneEdit.SMenuItem>();
			@this.m_menuRidDic = new Dictionary<int, SceneEdit.SMenuItem>(fileCount);
			Dictionary<int, List<int>> menuGroupMemberDic = new Dictionary<int, List<int>>();
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
						if (!@this.m_menuRidDic.ContainsKey(mi.m_nMenuFileRID))
						{
							@this.m_menuRidDic.Add(mi.m_nMenuFileRID, mi);
						}
						string parentMenuName = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi }) as string;
						//string parentMenuName = SceneEdit.GetParentMenuFileName(mi);
						if (!string.IsNullOrEmpty(parentMenuName))
						{
							int hashCode = parentMenuName.GetHashCode();
							if (!menuGroupMemberDic.ContainsKey(hashCode))
							{
								menuGroupMemberDic.Add(hashCode, new List<int>());
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
							//yield return null;
							time = Time.realtimeSinceStartup;
						}
					}
				}
			}

			//Parallel.ForEach(GameUty.ModOnlysMenuFiles, (strFileName) =>
			foreach (string strFileName in GameUty.ModOnlysMenuFiles)
			{
				Stopwatch test2 = new Stopwatch();

				test2.Start();
				Main.logger.LogError("Invoking GetMenuItemSetUP");

				SceneEdit.SMenuItem mi2 = new SceneEdit.SMenuItem();
				if (Main.GetMenuItemSetUP(mi2, strFileName, out string IconTex, false))
				{
					Main.logger.LogError("Managed to setup menu item correctly. Adding to list to continue processing...");
					ListOfContinues[mi2] = IconTex;
				}

				//Thread.Sleep(100);

				Main.logger.LogError("Finished GetMenuItemSetUP: " + test2.Elapsed);
			}//);

			//@this2.StartCoroutine(SetupTexturesCoRoutine(menuList, menuGroupMemberDic));

			SavedmenuList = menuList;
			SavedmenuGroupMemberDic = menuGroupMemberDic;
			Test2Done = true;
		}

		public static IEnumerator SetupTexturesCoRoutine()
		{
			Main.logger.LogError($"Starting coroutine to set textures to menu items.");

			while (Test2Done != true)
			{
				yield return null;
			}

			Main.logger.LogError($"Test2 part 1 finished in {test.Elapsed}\n. Creating textures....");

			foreach (KeyValuePair<SceneEdit.SMenuItem, string> dic in ListOfContinues)
			{
				dic.Key.m_texIcon = ImportCM.CreateTexture(dic.Value);
			}

			Main.logger.LogError($"Done creating textures at {test.Elapsed}, continuing Test2 ");

			CoRouteDone = true;

			Test2Continuation();

			while (Test2ContinuatonDone == false)
			{
				yield return null;
			}

			Main.logger.LogError($"Test2 finished it's work at {test.Elapsed}");

			Main.logger.LogError("Invoking coroutines...");

			//yield return @this.StartCoroutine(@this.CoLoadWait());
			@this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "CoLoadWait").Invoke(@this, null) as IEnumerator);

			//yield return @this.StartCoroutine(@this.FixedInitMenu(menuList, @this.m_menuRidDic, menuGroupMemberDic));
			@this.StartCoroutine(AccessTools.Method(typeof(SceneEdit), "FixedInitMenu").Invoke(@this, new object[] { SavedmenuList, @this.m_menuRidDic, SavedmenuGroupMemberDic }) as IEnumerator);

			Main.logger.LogError($"Finished work at {test.Elapsed}");
		}

		public static void Test2Continuation()
		{
			//Stopwatch test3 = new Stopwatch();

			//test3.Start();

			Main.logger.LogError("Starting task scheduler. Going three at a time. Waiting until all complete before doing another 3.");

			var list = ListOfContinues.Keys.ToArray();

			int i = 0;

			while (i < ListOfContinues.Keys.Count())
			{
				Main.logger.LogError("Relooping for.");

				int d = 0;
				var taskList = new List<Task>();

				Main.logger.LogError("Creating task...");

				while (d < 1 && i < ListOfContinues.Keys.Count())
				{
					int index = i;
					taskList.Add(Task.Factory.StartNew(new Action(() => ContinueSetup(list[index]))));

					//Task.Factory.StartNew(new Action(() => ContinueSetup(list[index]))).Wait();

					//ContinueSetup(list[index]);
					++i;
					++d;
				}

				//Main.logger.LogError($"Waiting for tasks to complete: {taskList.Count}");

				Task.WaitAll(taskList.ToArray());

				Main.logger.LogError("All tasks complete. Starting a new batch.");
			}

			//test3.Stop();
			//Main.logger.LogError($"Scheduler worked on {ListOfContinues.Keys.Count} menu files and finished the work at {test3.Elapsed}");

			Test2ContinuatonDone = true;

			//test.Stop();

			//Main.logger.LogError($"Done {test.Elapsed}");

			//yield break;
		}

		public static void ContinueSetup(SceneEdit.SMenuItem mi2)
		{
			Stopwatch test2 = new Stopwatch();

			if (!mi2.m_bMan && !(mi2.m_texIconRef == null))
			{
				test2.Start();
				Main.logger.LogError("Invoking addmenuitemtolist");
				AccessTools.Method(typeof(SceneEdit), "AddMenuItemToList").Invoke(@this, new object[] { mi2 });
				//@this.AddMenuItemToList(mi2);
				test2.Stop();
				Main.logger.LogError($"Done invoking AddMenuItemToList at: {test2.Elapsed}");
				if (!@this.m_menuRidDic.ContainsKey(mi2.m_nMenuFileRID))
				{
					@this.m_menuRidDic.Add(mi2.m_nMenuFileRID, mi2);
				}
				test2.Start();
				Main.logger.LogError("Invoking GetParentMenuFileName");
				string parentMenuName2 = AccessTools.Method(typeof(SceneEdit), "GetParentMenuFileName").Invoke(@this, new object[] { mi2 }) as string;
				//string parentMenuName2 = SceneEdit.GetParentMenuFileName(mi2);
				test2.Stop();
				Main.logger.LogError($"Done Invoking GetParentMenuFileName at: {test2.Elapsed}");
				test2.Start();
				if (!string.IsNullOrEmpty(parentMenuName2))
				{
					int hashCode2 = parentMenuName2.GetHashCode();
					if (!SavedmenuGroupMemberDic.ContainsKey(hashCode2))
					{
						SavedmenuGroupMemberDic.Add(hashCode2, new List<int>());
					}
					SavedmenuGroupMemberDic[hashCode2].Add(mi2.m_strMenuFileName.ToLower().GetHashCode());
				}
				else if (mi2.m_strCateName.IndexOf("set_") != -1 && mi2.m_strMenuFileName.IndexOf("_del") == -1)
				{
					mi2.m_bGroupLeader = true;
					mi2.m_listMember = new List<SceneEdit.SMenuItem>();
					mi2.m_listMember.Add(mi2);
				}
				/*
				if (true || 0.5f < Time.realtimeSinceStartup - time)
				{
					//yield return null;
					Main.logger.LogError($"Sleeping thread, 100ms...");
					//Thread.Sleep(20);
					time = Time.realtimeSinceStartup;
				}*/
				test2.Stop();
				Main.logger.LogError($"Finished processing one menu file in {test2.Elapsed}");
			}
		}

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

		public static IEnumerator ThrowConcurrentException(string error)
		{
			Main.logger.LogError("A concurrent thread ran into this error: " + error);

			yield return null;
		}
		/*
		public static IEnumerator LoadTexture(SceneEdit.SMenuItem menu, string texture)
		{
			menu.m_texIcon = ImportCM.CreateTexture(texture);

			yield return null;
		}*/
	}
}