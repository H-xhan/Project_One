using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CartoonFXPack.Utility
{

	public static class URandom
	{
		public static bool Randomize(this float v)
		{
			return UnityEngine.Random.Range(0f, 1f) <= v;
		}

		public static float Randomize(this Vector2 vector)
		{
			return UnityEngine.Random.Range(vector.x, vector.y);
		}
		public static int Randomize(this Vector2Int vector)
		{
			return UnityEngine.Random.Range(vector.x, vector.y + 1);
		}

		public static T RandomByWeight<T>(this IEnumerable<T> values, Func<T, float> getWeight)
			=> RandomByWeight(values, getWeight, e => "");

		public static T RandomByWeight<T>(
			IEnumerable<T> values,
			Func<T, float> getWeight,
			Func<T, string> getName,
			params T[] exceptions
		)
		{
			exceptions = exceptions.Where(e => e != null).ToArray();
			values = values.Where(v => !exceptions.Select(e => getName(e)).Contains(getName(v)));

			float totalWeight = 0;
			foreach (T value in values.Except(exceptions))
				totalWeight += getWeight(value);

			float randomValue = UnityEngine.Random.Range(0, totalWeight);
			float weightSum = 0;

			foreach (T value in values)
			{
				weightSum += getWeight(value);
				if (randomValue <= weightSum)
					return value;
			}

			// this line will never be reached unless the array is empty.
			throw new InvalidOperationException("No value selected");
		}

		public static T RandomElement<T>(this IEnumerable<T> values, params T[] exceptions)
		{
			return RandomElement(values, out int _, exceptions);
		}
		public static T RandomElement<T>(this IEnumerable<T> values, out int index, params T[] exceptions)
		{
			var filtered = values.Except(exceptions).ToList();
			var result = filtered[UnityEngine.Random.Range(0, filtered.Count)];
			index = values.ToList().IndexOf(result);
			return result;
		}
	}

}