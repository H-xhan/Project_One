using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace CartoonFXPack
{

	public enum EText
	{
		Normal,
		Effect
	}

	public enum ETextAnimation
	{
		Appear,
		Slide,
		FastAppear,
		Rise,
		LinearSlide
		/* ADD YOUR OWN TRANSITION HERE */
	}

	[CreateAssetMenu(fileName = "Settings", menuName = "Cartoon FX Pack/Settings [SINGLETON]", order = 0)]
	public class Objects : SingletonScriptableObject<Objects>
	{
		public abstract class Type
		{
			public abstract Enum type { get; }
		}

		public abstract class Asset : Type
		{
			public VisualEffect asset;
		}

		[Serializable]
		public class Text : Asset
		{
			[SerializeField] private EText _type;
			public override Enum type => _type;
		}
		[SerializeField] private Text[] texts;

		[Serializable]
		public class TextAnimation : Type
		{
			[SerializeField] private ETextAnimation _type;
			public override Enum type => _type;
			[Tooltip("How should the angle change over life (this is multiplied by 360 degrees)")]
			public AnimationCurve angleOverLifeStart, angleOverLifeEnd;
			[Tooltip("How should the size change over life.")]
			public AnimationCurve sizeOverLifeStart, sizeOverLifeEnd;
			[Tooltip("How should the pivot (or position) change over life")]
			public AnimationCurve pivotOverLifeStart, pivotOverLifeEnd;
			[Tooltip("The amplitude of the pivot change.")]
			[Range(-10, 10)] public float pivotXOverLifeStartAmplitude = 0, pivotYOverLifeStartAmplitude = 0;
			[Tooltip("The amplitude of the pivot change.")]
			[Range(-10, 10)] public float pivotXOverLifeEndAmplitude = 0, pivotYOverLifeEndAmplitude = 0;
		}
		[SerializeField] private TextAnimation[] textAnimations;


		public static TextAnimation Get(ETextAnimation animation)
			=> (TextAnimation)Get(GetInstance().textAnimations, animation);

		public static VisualEffect Get(EText text)
			=> ((Asset)Get(GetInstance().texts, text)).asset;

		private static Type Get<T>(Type[] list, T type) where T : Enum
		{
			var asset = list.FirstOrDefault(e => e.type.Equals(type))
				?? throw new Exception($"Asset of type {type} not found in object list.");
			return asset;
		}
	}

}
