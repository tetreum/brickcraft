using System.Collections;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Brickcraft.EditorTools
{
    /// <summary>
    /// Records the main menu's background video (Assets/Video/MainBackground.mp4): in play mode, in a world, a camera
    /// starts right above a grass block, its studs filling the screen, and climbs faster and faster, turning slowly,
    /// until the terrain around is seen from high above. Rendered frame by frame at a fixed rate, so it's smooth however
    /// slow the rendering. The player and the HUD aren't in it.
    /// </summary>
    public static class MenuVideoRecorder
    {
        public const string VideoPath = "Assets/Video/MainBackground.mp4";
        private const int Width = 1920, Height = 1080, FrameRate = 30;
        private const float Seconds = 28;
        // metres above the block's top at the start and at the end
        private const float StartHeight = 0.55f, EndHeight = 260f;
        // degrees away from looking straight down, and the turn around the block over the video
        private const float StartTilt = 8f, EndTilt = 14f;
        private const float StartYaw = -20f, EndYaw = -42f;
        // the time of day it's recorded at (0.5 noon), for the sun's shadows
        private const double TimeOfDay = 0.33;

        [MenuItem("Brickcraft/Record menu video")]
        private static void record() {
            if (!EditorApplication.isPlaying || WorldBehaviour.Instance == null || Player.Instance == null) {
                EditorUtility.DisplayDialog("Record menu video", "Play a world first: the video is recorded around the player.", "OK");
                return;
            }
            if (isRecording) {
                return;
            }
            // editor scripts can't be components: a game object of the game runs it
            BrickcraftNetworkManager.Instance.StartCoroutine(recording());
        }

        private static bool isRecording;

        private static IEnumerator recording() {
            isRecording = true;
            WorldBehaviour world = WorldBehaviour.Instance;
            WorldTime.Time = System.Math.Floor(WorldTime.Time) + TimeOfDay;
            WorldTime.IsPaused = true;

            Debug.Log("Menu video: waiting for the terrain around to be drawn");
            Vector2Int center = WorldBehaviour.ChunkAt(Player.Instance.transform.position);
            int radius = GameSettings.RenderDistance - 1;
            while (!isDrawn(world, center, radius)) {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3); // the last meshes uploading

            Vector3 target = findGrass(world, Player.Instance.transform.position);
            foreach (Renderer renderer in Player.Instance.GetComponentsInChildren<Renderer>()) {
                renderer.enabled = false;
            }

            Camera camera = createCamera();
            RenderTexture texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = texture;
            Texture2D frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            string path = Path.Combine(Path.GetTempPath(), "MainBackground.mp4");
            VideoTrackAttributes attributes = new VideoTrackAttributes() {
                frameRate = new MediaRational(FrameRate),
                width = Width,
                height = Height,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High,
            };

            int frames = Mathf.RoundToInt(Seconds * FrameRate);
            Time.captureFramerate = FrameRate;
            Debug.Log("Menu video: recording " + frames + " frames to " + path);
            using (MediaEncoder encoder = new MediaEncoder(path, attributes)) {
                // a few frames first so auto exposure settles on the first view
                for (int i = -15; i < frames; i++) {
                    place(camera.transform, target, Mathf.Max(0, i) / (float)(frames - 1));
                    yield return new WaitForEndOfFrame();
                    if (i < 0) {
                        continue;
                    }
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = texture;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    frame.Apply();
                    RenderTexture.active = previous;
                    encoder.AddFrame(frame);
                }
            }
            Time.captureFramerate = 0;
            WorldTime.IsPaused = false;

            Object.Destroy(camera.gameObject);
            Object.Destroy(texture);
            Object.Destroy(frame);
            foreach (Renderer renderer in Player.Instance.GetComponentsInChildren<Renderer>()) {
                renderer.enabled = true;
            }
            Debug.Log("Menu video: recorded " + path + ", copy it to " + VideoPath + " to use it");
            isRecording = false;
        }

        private static bool isDrawn(WorldBehaviour world, Vector2Int center, int radius) {
            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++) {
                    // far chunks have no colliders: drawn is enough
                    if (world.GetLoadProgress(center + new Vector2Int(x, z)) < 0.85f) {
                        return false;
                    }
                }
            }
            return true;
        }

        // the top of a grass block near the player (not under it), in the middle of a flat open patch of grass so
        // nothing gets in the way while the camera is low
        private static Vector3 findGrass(WorldBehaviour world, Vector3 near) {
            Vector3Int start = BrickGrid.CellToBlock(BrickGrid.WorldToCell(near));
            for (int distance = 12; distance < 120; distance++) {
                // the ring of columns that far
                for (int x = -distance; x <= distance; x++) {
                    for (int z = -distance; z <= distance; z += Mathf.Abs(x) == distance ? 1 : 2 * distance) {
                        Vector3Int column = start + new Vector3Int(x, 0, z);
                        if (isFlatGrass(world, column, out float top)) {
                            Vector3 centre = BrickGrid.CellToWorld(BrickGrid.BlockToCell(column), new Vector3(1, 0, 1));
                            return new Vector3(centre.x, top, centre.z);
                        }
                    }
                }
            }
            Debug.LogWarning("Menu video: no flat grass around, starting above the player");
            return near;
        }

        // grass at the same height all around it
        private static bool isFlatGrass(WorldBehaviour world, Vector3Int column, out float top) {
            const int Around = 3;
            byte grass = BlockDatabase.IdOf("grass");
            top = 0;
            for (int x = -Around; x <= Around; x++) {
                for (int z = -Around; z <= Around; z++) {
                    Vector3 position = BrickGrid.CellToWorld(BrickGrid.BlockToCell(column + new Vector3Int(x, 0, z)), new Vector3(1, 0, 1));
                    float height = world.GetSurfaceHeight(position);
                    Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(new Vector3(position.x, height - Server.brickHeight / 2, position.z)));
                    if ((byte)world.GetBlockType(block) != grass || (x != -Around || z != -Around) && Mathf.Abs(height - top) > 0.01f) {
                        return false;
                    }
                    top = height;
                }
            }
            return true;
        }

        // a camera like the player's, but rendering into the video only
        private static Camera createCamera() {
            Camera main = Camera.main;
            GameObject cameraObject = new GameObject("Menu Video Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
            if (main != null) {
                camera.fieldOfView = main.fieldOfView;
                camera.cullingMask = main.cullingMask;
                HDAdditionalCameraData mainData = main.GetComponent<HDAdditionalCameraData>();
                if (mainData != null) {
                    mainData.CopyTo(data);
                }
            }
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 3000;
            camera.depth = -10;
            return camera;
        }

        // where the camera is at that point of the video (0 to 1): higher and higher (exponentially, easing at both ends),
        // looking at the block
        private static void place(Transform camera, Vector3 target, float t) {
            float eased = t * t * (3 - 2 * t);
            float climb = Mathf.Lerp(t, eased, 0.5f);
            float height = StartHeight * Mathf.Pow(EndHeight / StartHeight, climb);
            Quaternion rotation = Quaternion.Euler(90 - Mathf.Lerp(StartTilt, EndTilt, t), Mathf.Lerp(StartYaw, EndYaw, t), 0);
            Vector3 forward = rotation * Vector3.forward;
            camera.rotation = rotation;
            camera.position = target - forward * (height / -forward.y);
        }
    }
}
