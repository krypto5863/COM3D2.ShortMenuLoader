using System.Collections.Generic;

namespace ShortMenuLoader
{
	internal class VanillaMenuLoaderSmvdCompat
	{
		internal static void ReadMenuItemDataFromNative(SceneEdit.SMenuItem mi, int menuDataBaseIndex, out string iconStr)
		{
			var menuDataBase = ShortMenuVanillaDatabase.Main.Database.MenusList;
			mi.m_strMenuName = menuDataBase[menuDataBaseIndex].Name;
			mi.m_strInfo = menuDataBase[menuDataBaseIndex].Description;
			mi.m_mpn = menuDataBase[menuDataBaseIndex].Category;
			mi.m_strCateName = menuDataBase[menuDataBaseIndex].Category.ToString();
			mi.m_eColorSetMPN = menuDataBase[menuDataBaseIndex].ColorSetMpn;
			mi.m_strMenuNameInColorSet = menuDataBase[menuDataBaseIndex].ColorSetMenu;
			mi.m_pcMultiColorID = menuDataBase[menuDataBaseIndex].MultiColorId;
			mi.m_boDelOnly = menuDataBase[menuDataBaseIndex].DelMenu;
			mi.m_fPriority = menuDataBase[menuDataBaseIndex].Priority;
			mi.m_bMan = menuDataBase[menuDataBaseIndex].ManMenu;
			mi.m_bOld = menuDataBase[menuDataBaseIndex].Version < 2000;
			iconStr = menuDataBase[menuDataBaseIndex].Icon;

			if (Main.PutMenuFileNameInItemDescription.Value)
			{
				mi.m_strInfo += $"\n\n{menuDataBase[menuDataBaseIndex].FileName}";
			}
		}

		internal static void LoadFromSmvdDictionary(ref Dictionary<SceneEdit.SMenuItem, int> filesToLoadFromDatabase)
		{
			foreach (var menu in ShortMenuVanillaDatabase.Main.Database.MenusList)
			{
				var fileName = menu.FileName;

				if (GameMain.Instance.CharacterMgr.status.IsHavePartsItem(fileName))
				{
					var mi = new SceneEdit.SMenuItem
					{
						m_strMenuFileName = fileName,
						m_nMenuFileRID = fileName.GetHashCode()
					};

					filesToLoadFromDatabase[mi] = ShortMenuVanillaDatabase.Main.Database.MenusList.FindIndex(r => r.Equals(menu));
				}
			}
		}
	}
}