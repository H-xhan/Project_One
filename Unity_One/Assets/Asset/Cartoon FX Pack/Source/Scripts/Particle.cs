using UnityEngine;
using UnityEngine.VFX;
using System.Collections.Generic;
using UnityEngine.Events;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CartoonFXPack
{

	public class Particle : Creator
	{
#pragma warning disable CS0414
		[Header("Editor Playback")]
		[Tooltip("Speed of the playback.")]
		[SerializeField, Range(0, 2)] private float playbackSpeed = 1;
		[Tooltip("Pause the playback.")]
		[SerializeField] private bool pause = false;
#pragma warning restore

		[Header("On Init")]
		[SerializeField] private bool cameraShake;

		public enum ClearBehavior
		{
			None,
			Disable,
			Destroy
		}

		[Header("On Finished")]
		[SerializeField] private ClearBehavior clearBehavior = ClearBehavior.Destroy;
		[SerializeField] private UnityEvent callbacks;

		private float age;


		private void Start()
		{
			if (cameraShake)
				CameraController.Shake();

			float maxDelay = 0;
			foreach (var fx in Effects)
			{
				fx.pause = false;
				fx.playRate = 1;
				float delay = fx.GetFloat("Delay");
				maxDelay = Mathf.Max(maxDelay, delay);
			}
			age = maxDelay * 2 + 1f;
		}

		private void Update()
		{
			if (age < 0)
			{
				foreach (var fx in Effects)
				{
					if (fx.aliveParticleCount > 0)
						return;
				}
				Dispose();

				return;
			}
			age -= Time.deltaTime;
		}


		private void Dispose()
		{
			callbacks?.Invoke();
			switch (clearBehavior)
			{
				case ClearBehavior.None:
					break;
				case ClearBehavior.Disable:
					gameObject.SetActive(false);
					break;
				case ClearBehavior.Destroy:
					Destroy(gameObject);
					break;
			}
		}


#if UNITY_EDITOR
		internal override void Refresh()
		{
			if (Application.isPlaying) return;

			ApplyToAll(fx =>
			{
				fx.pause = pause;
				fx.playRate = playbackSpeed;
			});
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
	[CustomEditor(typeof(Particle))]
	public class ParticleEditor : CreatorEditor<Particle>
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
		}
	}
#endif

}