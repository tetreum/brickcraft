using Brickcraft;
using Brickcraft.Events;
using UnityEngine;

/// <summary>
/// Plays the game's sound effects and music, at the volumes of the settings: the master volume
/// is the whole game's (AudioListener.volume), effects and music each have their own on top.
/// </summary>
public class SoundManager : MonoBehaviour {

	public const string EFFECT_TAPPING = "Tapping";
	public const string EFFECT_DIG = "Dig";
	public const string EFFECT_REMOVE_BLOCK = "RemoveBlock";
	public const string EFFECT_ENTER_WATER = "EnterWater";
	public const string EFFECT_LEAVE_WATER = "LeaveWater";

	private AudioSource stereo;
	private AudioSource music;

	public static SoundManager Instance;

	void Awake () {
		Instance = this;
		stereo = GetComponent<AudioSource>();

		music = gameObject.AddComponent<AudioSource>();
		music.loop = true;
		music.playOnAwake = false;
		music.spatialBlend = 0;

		EventManager.SettingChanged.Subscribe(onSettingChanged);
		applyVolumes();
	}

	void OnDestroy () {
		EventManager.SettingChanged.Unsubscribe(onSettingChanged);
	}

	public void play (string name) {
		if (stereo.isPlaying && stereo.clip.name == name) {
			return;
		}
		stereo.clip = Resources.Load("Sound/Effects/" + name) as AudioClip;
		stereo.Play();
	}

	/// <summary>Plays the clip as music, looping, until another or StopMusic.</summary>
	public void PlayMusic (AudioClip clip) {
		if (music.clip == clip && music.isPlaying) {
			return;
		}
		music.clip = clip;
		music.Play();
	}

	public void StopMusic () {
		music.Stop();
	}

	private void onSettingChanged (SettingChangedEvent e) {
		if (e.setting == GameSettings.MasterVolumeSetting || e.setting == GameSettings.MusicVolumeSetting || e.setting == GameSettings.EffectsVolumeSetting) {
			applyVolumes();
		}
	}

	private void applyVolumes () {
		AudioListener.volume = GameSettings.MasterVolume;
		stereo.volume = GameSettings.EffectsVolume;
		music.volume = GameSettings.MusicVolume;
	}
}
