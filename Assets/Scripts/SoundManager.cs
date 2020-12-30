using UnityEngine;

public class SoundManager : MonoBehaviour {
	
	public const string EFFECT_TAPPING = "Tapping";
	public const string EFFECT_DIG = "Dig";
	public const string EFFECT_REMOVE_BLOCK = "RemoveBlock";
	public const string EFFECT_ENTER_WATER = "EnterWater";
	public const string EFFECT_LEAVE_WATER = "LeaveWater";
	
	private AudioSource stereo;
	
	public static SoundManager Instance;
	
	void Awake () {
		Instance = this;
		stereo = GetComponent<AudioSource>();
	}
	
	public void play (string name) {
		if (stereo.isPlaying && stereo.clip.name == name) {
			return;
		}
		stereo.clip = Resources.Load("Sound/Effects/" + name) as AudioClip;
		stereo.Play();
	}
}
