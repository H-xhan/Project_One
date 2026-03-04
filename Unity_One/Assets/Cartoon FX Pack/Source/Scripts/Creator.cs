using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace CartoonFXPack
{

	public abstract class Creator : MonoBehaviour
	{
#if UNITY_EDITOR
		internal abstract void Refresh();
		public abstract void Show();
#endif

		protected VisualEffect[] Effects => GetComponentsInChildren<VisualEffect>();

		protected void ApplyToAll(Action<VisualEffect> action)
		{
			foreach (var fx in Effects)
				action.Invoke(fx);
		}
	}

#if UNITY_EDITOR
	public abstract class CreatorEditor<T> : Editor where T : Creator
	{
		protected virtual void OnEnable()
		{
			if (target is not T m) return;
			Undo.undoRedoPerformed += m.Refresh;
		}
		protected virtual void OnDisable()
		{
			if (target is not T m) return;
			Undo.undoRedoPerformed -= m.Refresh;
		}

		protected void Show()
		{
			if (target is not T m) return;

			if (GUILayout.Button("SHOW"))
			{
				m.Show();
			}
			EditorGUILayout.Space();
		}

		protected void DefaultInspector()
		{
			if (target is not T m) return;

			EditorGUI.BeginChangeCheck();

			base.OnInspectorGUI();

			if (EditorGUI.EndChangeCheck())
			{
				m.Refresh();
			}
		}

		public override void OnInspectorGUI()
		{
			Show();
			DefaultInspector();
		}
	}
#endif

}