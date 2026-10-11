using System.Collections.Generic;
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
	/// <summary>The head going under water.</summary>
	public const string EFFECT_SUBMERGE = "Submerge";
	/// <summary>A melee weapon swung.</summary>
	public const string EFFECT_SWORD_SWING = "SwordSwing";
	/// <summary>Out of air under water, while it hurts.</summary>
	public const string EFFECT_DROWNING = "Drowning";
	/// <summary>A bow being drawn (as long as a full draw), its shot, and its arrow hitting something.</summary>
	public const string EFFECT_STRING_BOW = "StringBow";
	public const string EFFECT_SHOOT_BOW = "ShootBow";
	public const string EFFECT_BOW_IMPACT = "BowImpact";
	/// <summary>Bare hand hits, one picked at random each time (see Punch).</summary>
	public static readonly string[] EFFECT_PUNCHES = { "Punch", "Punch2" };

	/// <summary>A bare hand hit's sound, one of EFFECT_PUNCHES.</summary>
	public static string Punch {
		get { return EFFECT_PUNCHES[Random.Range(0, EFFECT_PUNCHES.Length)]; }
	}

	/// <summary>What a hit with that in hand sounds like: a weapon's swing, a ranged one's shot, a punch with anything else.</summary>
	public static string HitSound (Item held) {
		if (held == null || held.type != Item.Type.Weapon) {
			return Punch;
		}
		return held.IsRanged ? EFFECT_SHOOT_BOW : EFFECT_SWORD_SWING;
	}

	// an effect that lasts while something is held (a bow drawn), see startHeld
	private AudioSource held;

	private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

	private AudioSource stereo;
	private AudioSource music;

	public static SoundManager Instance;

	void Awake () {
		Instance = this;
		stereo = GetComponent<AudioSource>();

		held = gameObject.AddComponent<AudioSource>();
		held.playOnAwake = false;
		held.spatialBlend = 0;

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

	/// <summary>Plays a short effect over whatever else is playing (hits, swings).</summary>
	public void playOnce (string name) {
		AudioClip clip = clipOf(name);
		if (clip != null) {
			stereo.PlayOneShot(clip);
		}
	}

	/// <summary>Plays an effect that stops early with stopHeld (a bow's string while it's drawn).</summary>
	public void startHeld (string name) {
		AudioClip clip = clipOf(name);
		if (clip != null) {
			held.clip = clip;
			held.Play();
		}
	}

	public void stopHeld () {
		held.Stop();
	}

	/// <summary>Plays an effect from somewhere in the world (another player's hit...), quieter the farther it is.</summary>
	public void playAt (string name, Vector3 position) {
		AudioClip clip = clipOf(name);
		if (clip != null) {
			AudioSource.PlayClipAtPoint(clip, position, GameSettings.EffectsVolume);
		}
	}

	private AudioClip clipOf (string name) {
		if (string.IsNullOrEmpty(name)) {
			return null;
		}
		if (!clips.TryGetValue(name, out AudioClip clip)) {
			clip = Resources.Load<AudioClip>("Sound/Effects/" + name);
			if (clip == null) {
				Debug.LogWarning("There's no sound effect " + name + " in Resources/Sound/Effects");
			}
			clips[name] = clip;
		}
		return clip;
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
		held.volume = GameSettings.EffectsVolume;
		music.volume = GameSettings.MusicVolume;
	}
}
