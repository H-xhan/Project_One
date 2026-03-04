using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CartoonFXPack
{

	[CreateAssetMenu(fileName = "Font", menuName = "Cartoon FX Pack/Font", order = 0)]
	public class FontAsset : ScriptableObject
	{
		[Tooltip("Texture of the font.")]
		public Texture2D texture;
		[Tooltip("How many letters are there in the texture.")]
		public int count;

		[Serializable]
		public class Character
		{
			public char character;
		}
		[SerializeField] private Character[] chars;

		public int GetIndex(char c)
		{
			bool foundPerfectMatch = chars.FirstOrDefault(e => e.character.Equals(c)) != null;
			for (int i = 0; i < chars.Length; i++)
			{
				var _c = chars[i].character;
				if (foundPerfectMatch ? _c.Equals(c) : _c.ToString().Equals(c.ToString(), StringComparison.OrdinalIgnoreCase))
					return i;
			}
			return 0;
		}
	}

}