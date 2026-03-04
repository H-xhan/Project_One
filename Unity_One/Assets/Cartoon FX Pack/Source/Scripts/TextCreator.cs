using UnityEngine;
using UnityEngine.VFX;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CartoonFXPack
{

	public class TextCreator : Creator
	{
#pragma warning disable CS0414
		[Header("Playback")]
		[Tooltip("Speed of the playback.")]
		[SerializeField, Range(0, 2)] private float playbackSpeed = 1;
		[Tooltip("Pause the playback.")]
		[SerializeField] private bool pause;

		[Header("Overhead")]
		[Tooltip("Initial delay before the effect will appear.")]
		[SerializeField] private float delay = 0;
		[Tooltip("Whether it should delay the letters left to right or not.")]
		[SerializeField] private bool shouldDelayLeftToRight = false;
		[Tooltip("The delay of each letter if 'Should Delay Left To Right' is checked.")]
		[SerializeField] private float delayLeftToRight = 0.05f;
		[Header("Text")]
		[Tooltip("The type of text.")]
		[SerializeField] private EText textType = EText.Normal;
		[Tooltip("The font of the text.")]
		[SerializeField] private FontAsset font;
		[Tooltip("The actual contents of the text.")]
		[SerializeField] private string text = "YOUR TEXT HERE!";
		[Header("Animation")]
		[Tooltip("The animation when starting and ending the effect.")]
		[SerializeField] private ETextAnimation textAnimation = ETextAnimation.Appear;
		[Tooltip("The delay of the drop shadow, if present.")]
		[SerializeField, Range(0, 0.5f)] private float dropShadowDelay = 0.05f;
		[Header("Properties")]
		[Tooltip("The general shape of the letters.")]
		[SerializeField] private AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 0);
		[Tooltip("The offset between each letter.")]
		[SerializeField] private float characterOffset = 1.0f;
		[Tooltip("The size of the text.")]
		[SerializeField] private float fontSize = 1.75f;
		[Tooltip("The lifetime of the text.")]
		[SerializeField] private float lifetime = 1.0f;
		[ColorUsage(true, true)]
		[SerializeField] private Color color1 = new(0.9911022f, 0.1746475f, 0.003676507f, 1);
		[ColorUsage(true, true)]
		[SerializeField] private Color color2 = new(0.9911022f, 0.8796226f, 0.1651322f, 1);
		[ColorUsage(true, true)]
		[SerializeField] private Color outline = new(0.2917707f, 0.01850022f, 0.01096009f, 1);
		[Header("Movement")]
		[Tooltip("How MUCH it should move. (always)")]
		[SerializeField] private float amplitude = 0.1f;
		[Tooltip("How OFTEN it should move. (always)")]
		[SerializeField] private float frequency = 0.5f;
		[Header("Overlay")]
		[SerializeField] private Texture2D overlay = null;
		[SerializeField] private Color overlayColor = new(0, 0, 0, 0);
		[SerializeField, Range(0, 2)] private float overlayTiling = 1;
		[Header("Drop Shadow")]
		[SerializeField] private float dropShadowSizeMultiplier = 1;
		[SerializeField] private Color dropShadowColor = new(0, 0, 0, 0);
		[SerializeField] private Vector2 dropShadowPosition = new(0, 0.1f);
#pragma warning restore


#if UNITY_EDITOR
		internal override void Refresh()
		{
			if (Application.isPlaying) return;

			if (pause)
			{
				ApplyToAll(fx => fx.pause = true);
				return;
			}

			ApplyToAll(fx =>
			{
				if (fx != null && fx.gameObject != null)
					DestroyImmediate(fx.gameObject);
			});

			int n = text.Length;
			for (int i = 0; i < n; i++)
			{
				float t = n == 1 ? 0 : (float)i / (n - 1);

				var c = text[i];
				if (c.Equals(' ')) continue;

				int index = font.GetIndex(c);

				float j = ((float)(n - i)) / 2f;

				float offset = j * characterOffset;
				offset -= characterOffset * ((float)n) / 4f;
				offset -= characterOffset / 4f;

				var asset = Objects.Get(textType);
				var animation = Objects.Get(textAnimation);
				var copy = (VisualEffect)PrefabUtility.InstantiatePrefab(asset, transform);

				copy.name = c.ToString();
				copy.SetFloat("Delay", delay + (shouldDelayLeftToRight ? ((float)i) * delayLeftToRight : 0));
				copy.SetTexture("Font", font.texture);
				copy.SetInt("Letters", font.count);
				copy.SetInt("Index", index);
				copy.SetFloat("Offset", offset);
				copy.SetFloat("OffsetY", curve.Evaluate(t));
				copy.SetFloat("Size", fontSize);
				copy.SetFloat("Lifetime", lifetime);
				copy.SetVector4("Color1", color1);
				copy.SetVector4("Color2", color2);
				copy.SetVector4("Outline", outline);
				copy.SetFloat("Amplitude", amplitude);
				copy.SetFloat("Frequency", frequency);
				if (overlay == null)
					copy.SetVector4("Overlay Color", new(0, 0, 0, 0));
				else
				{
					copy.SetTexture("Overlay", overlay);
					copy.SetVector4("Overlay Color", overlayColor);
				}
				copy.SetFloat("Overlay Tiling", overlayTiling);
				copy.SetFloat("Drop Shadow Size Multiplier", dropShadowSizeMultiplier);
				copy.SetVector4("Drop Shadow Color", dropShadowColor);
				copy.SetVector2("Drop Shadow Position", dropShadowPosition);
				copy.SetAnimationCurve("Angle Over Life Start", animation.angleOverLifeStart);
				copy.SetAnimationCurve("Angle Over Life End", animation.angleOverLifeEnd);
				copy.SetAnimationCurve("Size Over Life Start", animation.sizeOverLifeStart);
				copy.SetAnimationCurve("Size Over Life End", animation.sizeOverLifeEnd);
				copy.SetAnimationCurve("Pivot Over Life Start", animation.pivotOverLifeStart);
				copy.SetAnimationCurve("Pivot Over Life End", animation.pivotOverLifeEnd);
				copy.SetVector2("Pivot Over Life Start Amplitude", new(animation.pivotXOverLifeStartAmplitude, animation.pivotYOverLifeStartAmplitude));
				copy.SetVector2("Pivot Over Life End Amplitude", new(animation.pivotXOverLifeEndAmplitude, animation.pivotYOverLifeEndAmplitude));
				copy.SetFloat("Drop Shadow Delay", dropShadowDelay);
				copy.pause = false;
				copy.playRate = playbackSpeed;
			}
		}
#endif

#if UNITY_EDITOR
		public override void Show()
		{
			if (Application.isPlaying) return;

			pause = false;

			Refresh();
			ApplyToAll(fx =>
			{
				fx.Reinit();
			});
		}
#endif
	}

#if UNITY_EDITOR
	[CustomEditor(typeof(TextCreator))]
	public class TextCreatorEditor : CreatorEditor<TextCreator>
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
		}
	}
#endif

}
