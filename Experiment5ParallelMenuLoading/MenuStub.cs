using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace ShortMenuLoader
{
	internal class MenuStub
	{
		public string Name { get; set; }
		public string SourceArc { get; set; }
		public string Icon { get; set; }
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter))]
		public MPN Category { get; set; }

		[JsonConverter(typeof(StringEnumConverter))]
		public MPN ColorSetMpn { get; set; }

		public string ColorSetMenu { get; set; }

		[JsonConverter(typeof(StringEnumConverter))]
		public MaidParts.PARTS_COLOR MultiColorId { get; set; }

		public bool DelMenu { get; set; }
		public bool ManMenu { get; set; }
		public float Priority { get; set; }
		public bool LegacyMenu { get; set; }
		public DateTime DateModified { get; set; }
	}
}