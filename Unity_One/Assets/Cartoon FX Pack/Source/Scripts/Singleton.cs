using System;
using System.Linq;
using UnityEngine;


namespace CartoonFXPack
{

	public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
	{
		protected static T instance;
		public static T GetInstance()
		{
			T obj = FindFirstObjectByType<T>();
			if (instance == null)
				instance = obj;

			return instance;
		}

		protected virtual void Awake()
		{
			GetInstance(); // general init
		}

		protected static bool IsAvailable(out T instance)
		{
			instance = GetInstance();
			return instance != null;
		}
	}

	public abstract class SingletonScriptableObject<T> : ScriptableObject where T : SingletonScriptableObject<T>
	{
		static T _instance;
		public static T GetInstance()
		{
			if (_instance == null)
			{
				T[] assets = Resources.LoadAll<T>("");
				if (assets == null || assets.Length < 1)
					throw new Exception($"Not found Singleton Scriptable Object of type: {typeof(T)}");
				else if (assets.Length > 1)
					throw new Exception($"More than 1 instance of Singleton Scriptable Object of type: {typeof(T)} found");
				_instance = assets[0];
			}

			return _instance;
		}
	}

}