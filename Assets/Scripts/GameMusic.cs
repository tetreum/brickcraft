using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The game's music: the songs in Resources/Sound/Music (streamed, see their import settings), one after another in a
    /// random order, shuffled again after each round (never the same one twice in a row), at the settings' music volume.
    /// It starts with the main menu (see MainPanel) and goes on across scenes.
    /// </summary>
    public class GameMusic : MonoBehaviour
    {
        public const string Folder = "Sound/Music";

        private static GameMusic instance;

        private AudioSource source;
        private AudioClip[] songs;
        private readonly List<AudioClip> queue = new List<AudioClip>();
        private AudioClip last;

        /// <summary>Starts the music, if it isn't playing already.</summary>
        public static void Play() {
            if (instance == null) {
                GameObject go = new GameObject("GameMusic");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<GameMusic>();
            }
        }

        private void Awake() {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.volume = GameSettings.MusicVolume;
            songs = Resources.LoadAll<AudioClip>(Folder);
            EventManager.SettingChanged.Subscribe(onSettingChanged);
        }

        private void OnDestroy() {
            EventManager.SettingChanged.Unsubscribe(onSettingChanged);
            if (instance == this) {
                instance = null;
            }
        }

        private void onSettingChanged(SettingChangedEvent e) {
            if (e.setting == GameSettings.MusicVolumeSetting) {
                source.volume = GameSettings.MusicVolume;
            }
        }

        // the next song once one ends (a streamed song is still loading when it's started: not playing yet, but its time is 0)
        private void Update() {
            if (songs.Length == 0 || source.isPlaying || (source.clip != null && source.clip.loadState == AudioDataLoadState.Loading)) {
                return;
            }
            if (queue.Count == 0) {
                shuffle();
            }
            last = queue[0];
            queue.RemoveAt(0);
            source.clip = last;
            source.Play();
        }

        // every song once, in a random order that doesn't start with the one just played
        private void shuffle() {
            queue.AddRange(songs);
            for (int i = queue.Count - 1; i > 0; i--) {
                int other = Random.Range(0, i + 1);
                AudioClip swap = queue[i];
                queue[i] = queue[other];
                queue[other] = swap;
            }
            if (queue.Count > 1 && queue[0] == last) {
                queue.RemoveAt(0);
                queue.Add(last);
            }
        }
    }
}
