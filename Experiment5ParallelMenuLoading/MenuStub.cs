using System;

namespace ShortMenuLoader
{
	internal class MenuStub
	{
		public string Name { get; set; }
		public string Icon { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string ColorSetMPN { get; set; }
		public string ColorSetMenu { get; set; }
		public string MultiColorID { get; set; }
		public bool DelMenu { get; set; }
		public bool ManMenu { get; set; }
		public float Priority { get; set; }
		public bool LegacyMenu { get; set; }
		public DateTime DateModified { get; set; }
		public MenuStub()
		{
		}
	}
}
