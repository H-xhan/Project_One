using System;
using UnityEngine;
using UnityEngine.UI;

namespace CartoonFXPack.Demo
{

	public class Navigator : MonoBehaviour
	{
		[SerializeField] private Text tabName;

		[Serializable]
		public class Category
		{
			public string name;
			public Asset[] assets;
		}

		[Serializable]
		public class Asset
		{
			public Particle[] types;
		}

		[SerializeField] private Category[] categories;

		private string TabName => categories[Tab].name;
		private Asset[] Assets => categories[Tab].assets;
		private Asset Current => Assets[Index];
		private Particle Particle => Current.types[Type];

		private int tab;
		private int Tab
		{
			get => tab;
			set
			{
				tab = ((value % categories.Length) + categories.Length) % categories.Length;
				tabName.text = TabName;
				Index = 0;
			}
		}
		private int index;
		private int Index
		{
			get => index;
			set
			{
				index = ((value % Assets.Length) + Assets.Length) % Assets.Length;
				Type = 0;
			}
		}
		private int type;
		private int Type
		{
			get => type;
			set
			{
				type = ((value % Current.types.Length) + Current.types.Length) % Current.types.Length;
				TypeChanged();
			}
		}

		private Particle obj;


		public void ChangeTab(bool up)
		{
			Tab += up ? 1 : -1;
		}


		private void Awake()
		{
			Tab = 0;
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
				Index--;
			else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
				Index++;
			else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
				Type--;
			else if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
				Type++;

			if (Input.GetKeyDown(KeyCode.Space))
			{
				Spawn();
			}
		}


		private void TypeChanged()
		{
			Spawn();
		}

		private void Spawn()
		{
			if (obj != null)
				Destroy(obj.gameObject);

			var particle = Particle;
			obj = Instantiate(particle, transform.position, Quaternion.identity, transform);
		}
	}

}
