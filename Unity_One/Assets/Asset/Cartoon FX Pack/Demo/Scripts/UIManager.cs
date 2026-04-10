using UnityEngine;

namespace CartoonFXPack.Demo
{

	public class UIManager : MonoBehaviour
	{
		[SerializeField] private GameObject pausedUI;
		[SerializeField] private GameObject pauseButton;


		private bool paused;
		private bool Paused
		{
			get => paused;
			set
			{
				paused = value;
				pausedUI.SetActive(paused);
				pauseButton.SetActive(!paused);
			}
		}


		private void Awake()
		{
			Paused = false;
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
				Paused = !Paused;
		}

		public void Quit()
		{
			Application.Quit();
		}

		public void Pause(bool paused)
		{
			Paused = paused;
		}
	}

}
