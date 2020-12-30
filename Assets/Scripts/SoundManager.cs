using UnityEngine;

public class SoundManager : MonoBehaviour {
	
	public const string EFFECT_TAPPING = "tapping";
	public const string EFFECT_DIG = "dig";
	public const string EFFECT_REMOVE_BLOCK = "removeblock";
	public const string EFFECT_ENTER_WATER = "enterwater";
	
	private AudioSource stereo;
	
	public static SoundManager Instance;
	
	void Awake () {
		Instance = this;
		stereo = GetComponent<AudioSource>();
	}
	
	public void play (string name) {
		stereo.clip = Resources.Load("Sound/Effects/" + name) as AudioClip;
		stereo.Play();
	}
}
